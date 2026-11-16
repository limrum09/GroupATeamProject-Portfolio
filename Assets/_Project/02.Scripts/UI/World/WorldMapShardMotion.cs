using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 월드맵 배경 조각의 방향별 부유 연출과 기준 배치 복원
/// </summary>
[DisallowMultipleComponent]
public class WorldMapShardMotion : MonoBehaviour
{
    [Serializable]
    class Shard
    {
        public RectTransform Rect;
        public Vector2 MoveOffset;
        public float RotationZ;
        [Min(0.01f)] public float OutboundTime = 2f;
        public Vector2 ReturnTimeRange = new Vector2(2f, 4f);
    }

    [Header("----- 샤드별 부유 설정 -----")]
    [SerializeField, Min(0f)] float _startInterval = 0.15f;
    [SerializeField] Ease _motionEase = Ease.OutCirc;
    [SerializeField] Shard[] _shards;

    readonly System.Random _random = new System.Random();
    Tween[] _tweens;
    (Vector2 Position, Quaternion Rotation)[] _restPoses;
    bool _playing;

    void Awake()
    {
        if (_shards == null || _shards.Length == 0)
            throw new MissingReferenceException($"{name}: 부유 연출 대상 샤드 연결 필요");

        foreach (Shard shard in _shards)
        {
            if (shard.Rect == null || shard.Rect.parent != transform)
                throw new MissingReferenceException($"{name}: Shards 직속 RectTransform 연결 필요");
            if (!(shard.OutboundTime > 0f && shard.ReturnTimeRange.x > 0f &&
                  shard.ReturnTimeRange.y >= shard.ReturnTimeRange.x))
                throw new InvalidOperationException($"{shard.Rect.name}: 양수 이동 시간과 복귀 시간 범위 필요");
        }

        _tweens = new Tween[_shards.Length];
        _restPoses = new (Vector2, Quaternion)[_shards.Length];
    }

    void OnDisable()
    {
        Stop();
    }

    void ApplyPose(int index, float progress)
    {
        Shard shard = _shards[index];
        var rest = _restPoses[index];
        Quaternion rotation = rest.Rotation * Quaternion.Euler(0f, 0f, shard.RotationZ * progress);
        // 화면 가장자리 앵커를 유지한 채 이미지 중앙 기준으로 회전
        Vector3 center = Vector3.Scale(shard.Rect.rect.center, shard.Rect.localScale);
        Vector2 pivotOffset = rest.Rotation * center - rotation * center;
        shard.Rect.localRotation = rotation;
        shard.Rect.anchoredPosition = rest.Position + shard.MoveOffset * progress + pivotOffset;
    }

    void PlayCycle(int index, float delay)
    {
        Shard shard = _shards[index];
        float returnTime = Mathf.Lerp(shard.ReturnTimeRange.x, shard.ReturnTimeRange.y,
            (float)_random.NextDouble());
        _tweens[index] = DOTween.Sequence()
            .SetUpdate(UpdateType.Normal, true)
            .AppendInterval(delay)
            .Append(DOVirtual.Float(0f, 1f, shard.OutboundTime, value => ApplyPose(index, value))
                .SetEase(_motionEase))
            .Append(DOVirtual.Float(1f, 0f, returnTime, value => ApplyPose(index, value))
                .SetEase(_motionEase))
            .OnComplete(() => PlayCycle(index, 0f));
    }

    /// <summary>
    /// 기준 배치에서 무작위 순서와 공통 시작 간격으로 부유 시작
    /// </summary>
    public void Play()
    {
        if (_playing || !isActiveAndEnabled) return;
        _playing = true;

        int[] order = new int[_shards.Length];
        for (int index = 0; index < order.Length; index++)
        {
            RectTransform rect = _shards[index].Rect;
            _restPoses[index] = (rect.anchoredPosition, rect.localRotation);
            order[index] = index;
        }

        for (int index = order.Length - 1; index > 0; index--)
        {
            int other = _random.Next(index + 1);
            (order[index], order[other]) = (order[other], order[index]);
        }

        for (int index = 0; index < order.Length; index++)
            PlayCycle(order[index], index * _startInterval);
    }

    /// <summary>
    /// 진행 중인 트윈 종료와 기준 배치 복원
    /// </summary>
    public void Stop()
    {
        if (!_playing) return;
        _playing = false;

        for (int index = 0; index < _tweens.Length; index++)
        {
            _tweens[index]?.Kill();
            _tweens[index] = null;
            if (_shards[index].Rect != null)
                ApplyPose(index, 0f);
        }
    }
}
