using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>한 번 발동한 공격의 저장 정보와 결과 통지</summary>
public sealed class SkillExecutionContext
{
    bool _areaStarted;
    bool _areaEnded;
    bool _publishingAreaStart;
    bool _areaEndRequested;

    public CombatActionInfo Action { get; }
    public CombatSource Source { get; }
    public Vector3 SourcePosition { get; }
    public Vector3 SourceAimPosition { get; }
    public Quaternion SourceRotation { get; }
    public Scene SourceScene { get; }
    public int Level { get; }
    public SkillGrowthBonus Growth { get; }
    public float AttackPower { get; }
    public float SpellPower { get; }
    public float CriticalChance { get; }
    public float CriticalDamageMultiplier { get; }

    // 발동한 씬이 유지되는 동안의 실행 가능 여부>
    public bool CanContinue => SourceScene.IsValid() && SourceScene.isLoaded;
    public bool IsAreaEnded => _areaEnded;

    public event Action<CombatActionInfo, Vector3> OnExecuted;
    public event Action<SkillImpactInfo> OnImpact;
    // 범위 시작 위치, 회전, 반복 여부와 선택적 연출 추적 기준 통지
    public event Action<Vector3, Quaternion, bool, Transform> OnAreaStarted;
    // 해당 범위 실행의 종료 통지
    public event Action OnAreaEnded;

    static bool IsNonNegativeFinite(float value)
    {
        return value >= 0f && !float.IsInfinity(value);
    }

    public SkillExecutionContext(CombatTarget caster, SkillInstance skill, CombatActionInfo action)
    {
        if (caster == null)
            throw new ArgumentNullException(nameof(caster));

        if (!caster.IsAlive)
            throw new InvalidOperationException("살아 있는 시전자의 발동 시점에서 실행 정보 생성 필요");

        Source = caster.Source;

        if (!Source.IsValid)
            throw new InvalidOperationException("초기화된 전투 개체 식별값 필요");

        Action = action;
        SourcePosition = caster.Position;
        SourceAimPosition = caster.AimPosition;
        SourceRotation = caster.ActorTransform.rotation;
        SourceScene = caster.gameObject.scene;

        Level = skill != null ? skill.Level : 1;
        Growth = skill != null ? skill.Definition.GetGrowth(Level) : default;

        CharacterStatus status = caster.Status;

        AttackPower = status.AttackPower;
        SpellPower = status.SpellPower;
        CriticalChance = status.CriticalChance;
        CriticalDamageMultiplier = status.CriticalDamageMultiplier;

        if (!IsNonNegativeFinite(AttackPower) || !IsNonNegativeFinite(SpellPower) || !IsNonNegativeFinite(CriticalChance) || !IsNonNegativeFinite(CriticalDamageMultiplier) || CriticalDamageMultiplier < 1f)
            throw new InvalidOperationException("유효한 공격력·주문력·치명타 설정 필요");
    }

    /// <summary>
    /// 범위 시작 정보와 선택적 연출 추적 기준 통지
    /// </summary>
    /// <param name="position">범위 시작 위치</param>
    /// <param name="rotation">범위 기준 회전</param>
    /// <param name="hasRuntime">반복 실행 객체의 존재 여부</param>
    /// <param name="followAnchor">연출이 읽을 위치 기준, 고정형은 null</param>
    public void PublishAreaStarted(Vector3 position, Quaternion rotation, bool hasRuntime, Transform followAnchor)
    {
        if (_areaStarted || !CanContinue)
            return;

        _areaStarted = true;
        _publishingAreaStart = true;

        try
        {
            OnAreaStarted?.Invoke(position, rotation, hasRuntime, followAnchor);
        }
        finally
        {
            _publishingAreaStart = false;
            OnAreaStarted = null;

            // 시작 알림 중 발생한 종료를 구독 처리 이후 전달
            if (_areaEndRequested)
                PublishAreaEnded();
        }
    }

    public void PublishExecuted(CombatActionInfo action, Vector3 position)
    {
        if (CanContinue)
            OnExecuted?.Invoke(action, position);
    }

    public void PublishImpact(SkillImpactInfo impact)
    {
        if (CanContinue)
            OnImpact?.Invoke(impact);
    }

    public void PublishAreaEnded()
    {
        if (_areaEnded)
            return;

        _areaEndRequested = true;

        if (!_areaStarted || _publishingAreaStart)
            return;

        _areaEnded = true;

        Action ended = OnAreaEnded;
        OnAreaEnded = null;

        // 씬 종료 중에도 기존 연출의 반환 알림 허용
        ended?.Invoke();
    }
}