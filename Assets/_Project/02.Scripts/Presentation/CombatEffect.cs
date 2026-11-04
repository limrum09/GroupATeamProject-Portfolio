using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬 수명으로 전투 이펙트 재생과 정리
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatEffect : MonoBehaviour
{
    [Header("----- 풀 보관 설정 -----")]
    [SerializeField, Min(1)] int _maxInactivePerPrefab = 4;

    [Header("----- 1회성 연출 설정 -----")]
    [SerializeField, Min(0.01f)] float _oneShotMaxLifetime = 10f;

    sealed class Playback
    {
        public CombatEffectPool.Entry Entry;
        public SkillExecutionContext AreaOwner;
        public Action AreaEnded;
        public double ReturnAt;
        public Transform AreaAnchor;
        public Vector3 AreaPositionOffset;
        public Quaternion AreaRotationOffset;
    }

    readonly Dictionary<long, Playback> _playing = new Dictionary<long, Playback>();
    readonly List<long> _returnIds = new List<long>();

    CombatEffectPool _pool;
    long _nextPlaybackId;

    public int ActiveEffectCount => _playing.Count;

    void Awake()
    {
        if (_maxInactivePerPrefab < 1)
            throw new InvalidOperationException($"{name}: 프리팹별 보관 개수는 1 이상 필요");

        if (!(_oneShotMaxLifetime > 0f) || float.IsInfinity(_oneShotMaxLifetime))
            throw new InvalidOperationException($"{name}: 유한한 양수의 1회성 최대 재생시간 필요");

        _pool = new CombatEffectPool(transform, _maxInactivePerPrefab);
    }

    void LateUpdate()
    {
        _returnIds.Clear();

        double now = Time.timeAsDouble;

        foreach (KeyValuePair<long, Playback> pair in _playing)
        {
            Playback playback = pair.Value;
            ParticleSystem effect = playback.Entry.Root;

            if (effect == null)
            {
                Debug.LogError($"{name}: 재생 중 이펙트가 외부에서 삭제됨", this);

                _returnIds.Add(pair.Key);
                continue;
            }

            if (!effect.gameObject.activeInHierarchy)
            {
                _returnIds.Add(pair.Key);
                continue;
            }

            if (playback.AreaOwner != null)
            {
                if (playback.AreaOwner.IsAreaEnded || !playback.AreaOwner.CanContinue)
                {
                    _returnIds.Add(pair.Key);
                    continue;
                }

                // 추적 기준이 전달된 재생 건만 위치 갱신
                if (!object.ReferenceEquals(playback.AreaAnchor, null))
                {
                    if (playback.AreaAnchor == null)
                    {
                        _returnIds.Add(pair.Key);
                        continue;
                    }

                    Transform anchor = playback.AreaAnchor;

                    effect.transform.SetPositionAndRotation(anchor.position + anchor.rotation * playback.AreaPositionOffset, anchor.rotation * playback.AreaRotationOffset);
                }

                continue;
            }

            if (now >= playback.ReturnAt || !effect.IsAlive(true))
                _returnIds.Add(pair.Key);
        }

        foreach (long playbackId in _returnIds)
            Stop(playbackId);
    }

    void OnDisable()
    {
        StopAll();
    }

    void OnDestroy()
    {
        StopAll();

        if (_pool != null)
        {
            _pool.Dispose();
            _pool = null;
        }
    }

    CombatEffectHandle PlayInternal(ParticleSystem prefab, Vector3 position, Quaternion rotation, Vector3 rotationOffset, SkillExecutionContext areaOwner, Transform areaAnchor = null, Vector3 areaPositionOffset = default, AreaEffectDefinition areaDefinition = null)
    {
        // 종료된 씬 재생기와 선택적 프리팹의 재생 생략
        if (this == null || !isActiveAndEnabled || prefab == null)
            return default;

        if (_pool == null)
            throw new InvalidOperationException($"{name}: 이펙트 풀 초기화 필요");

        long playbackId = checked(++_nextPlaybackId);

        CombatEffectPool.Entry entry = _pool.Rent(prefab);
        CombatEffectHandle handle = new CombatEffectHandle(this, playbackId);

        Playback playback = new Playback
        {
            Entry = entry,
            AreaOwner = areaOwner,
            AreaAnchor = areaAnchor,
            AreaPositionOffset = areaPositionOffset,
            AreaRotationOffset = Quaternion.Euler(rotationOffset),
            AreaEnded = areaOwner != null ? new Action(handle.Stop) : null,
            ReturnAt = Time.timeAsDouble + _oneShotMaxLifetime
        };

        _playing.Add(playbackId, playback);

        if (areaOwner != null)
            areaOwner.OnAreaEnded += playback.AreaEnded;

        try
        {
            Transform effectTransform = entry.Root.transform;
            effectTransform.SetParent(transform, false);
            effectTransform.SetPositionAndRotation(position, rotation * Quaternion.Euler(rotationOffset));

            effectTransform.localScale = entry.OriginalScale;

            // 활성화 전에 대여 등록과 종료 구독 완료
            entry.Root.gameObject.SetActive(true);

            // 활성화 콜백에서 이미 반환된 대여 건
            if (!_playing.ContainsKey(playbackId))
                return default;

            if (entry.Root == null)
                throw new InvalidOperationException($"{name}: 활성화 중 이펙트가 외부에서 삭제됨");

            // BakedSpellPlayer는 OnEnable에서 초기화와 재생 수행
            if (entry.BakedPlayer == null)
                entry.Root.Play(true);

            // 최초 재생 이후 선택적 반복 컴포넌트에 설정만 전달
            if (areaDefinition != null && entry.Root.TryGetComponent(out AreaEffectParticleRepeater repeater))
                repeater.BeginAfterInitialPlay(areaDefinition);

            return handle;
        }
        catch
        {
            // 재생 시작 실패 시 대여와 구독 회수
            Stop(playbackId);
            throw;
        }
    }

    void StopAll()
    {
        while (_playing.Count > 0)
        {
            long playbackId = 0;

            foreach (long activeId in _playing.Keys)
            {
                playbackId = activeId;
                break;
            }

            Stop(playbackId);
        }
    }



    /// <summary>행동 방향과 이펙트 회전 보정에 따른 발동 이펙트 재생</summary>
    /// <param name="prefab">재생할 발동 이펙트</param>
    /// <param name="action">확정된 행동 정보</param>
    /// <param name="origin">발동 위치 또는 탄환 발사 위치</param>
    /// <param name="rotationOffset">이펙트 로컬 회전 보정각, 도 단위</param>
    public void PlayAction(ParticleSystem prefab, CombatActionInfo action, Vector3 origin, Vector3 rotationOffset)
    {
        if (prefab == null)
            return;

        bool targetUseProjectile = action.Skill is TargetDamageSkillDefinition targeted && targeted.UseProjectile;
        bool areaPorjectile = action.Skill is AreaDamageSkillDefinition area && area.DeliveryMode == AreaDeliveryMode.Projectile;
        bool projectileAction = targetUseProjectile || areaPorjectile;

        Vector3 direction = action.Direction;

        if (!projectileAction)
            direction.y = 0f;

        Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        Vector3 position = origin;

        if (!projectileAction && action.Skill != null)
            position += rotation * action.Skill.EffectOffset;

        Play(prefab, position, rotation, rotationOffset);
    }

    internal bool IsPlaying(long playbackId)
    {
        return _playing.ContainsKey(playbackId);
    }

    internal void Stop(long playbackId)
    {
        if (!_playing.TryGetValue(playbackId, out Playback playback))
            return;

        // 재진입과 이전 핸들의 중복 반환 방지
        _playing.Remove(playbackId);

        if (playback.AreaOwner != null)
            playback.AreaOwner.OnAreaEnded -= playback.AreaEnded;

        _pool.Return(playback.Entry);
    }

    /// <summary>자체 종료 또는 최대 재생시간을 사용하는 연출 재생</summary>
    /// <param name="prefab">선택적 연출 프리팹</param>
    /// <param name="position">월드 재생 위치</param>
    /// <param name="rotation">기준 월드 회전</param>
    /// <param name="rotationOffset">연출 전용 회전 보정값</param>
    /// <returns>해당 재생 건의 제어 핸들, 재생 생략 시 기본값</returns>
    public CombatEffectHandle Play(ParticleSystem prefab, Vector3 position, Quaternion rotation, Vector3 rotationOffset)
    {
        return PlayInternal(prefab, position, rotation, rotationOffset, null);
    }

    /// <summary>
    /// 범위 실행의 수명과 선택적 위치 기준을 사용하는 장판 연출 재생
    /// </summary>
    /// <param name="execution">해당 범위의 실행 컨텍스트</param>
    /// <param name="prefab">선택적 장판 연출 프리팹</param>
    /// <param name="origin">범위 시작 위치</param>
    /// <param name="rotation">범위 기준 회전</param>
    /// <param name="positionOffset">연출 전용 로컬 위치 보정값</param>
    /// <param name="rotationOffset">연출 전용 회전 보정값</param>
    /// <param name="hasRuntime">반복 실행 객체의 존재 여부</param>
    /// <param name="followAnchor">읽기 전용 위치 기준, 고정형은 null</param>
    /// <returns>해당 재생 건의 제어 핸들, 재생 생략 시 기본값</returns>
    public CombatEffectHandle PlayArea(SkillExecutionContext execution, ParticleSystem prefab, Vector3 origin, Quaternion rotation, Vector3 positionOffset, Vector3 rotationOffset, bool hasRuntime, Transform followAnchor)
    {
        if (execution == null)
            throw new ArgumentNullException(nameof(execution));

        if (!execution.CanContinue || (hasRuntime && execution.IsAreaEnded))
            return default;

        AreaEffectDefinition areaDefinition = null;

        if (execution.Action.Skill is AreaDamageSkillDefinition area)
            areaDefinition = area.AreaEffect;

        return PlayInternal(prefab, origin + rotation * positionOffset, rotation, rotationOffset, hasRuntime ? execution : null, followAnchor, positionOffset, areaDefinition);
    }
}