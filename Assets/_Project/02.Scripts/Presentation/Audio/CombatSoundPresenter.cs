using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 실제 전투 발동과 명중 알림을 캐릭터별 소리 설정에 연결
/// </summary>
[DisallowMultipleComponent]
public class CombatSoundPresenter : MonoBehaviour
{
    [Serializable]
    class SkillSoundBinding
    {
        public SkillDefinition Skill;
        public SoundCue ExecutedCue;
        public SoundCue ImpactCue;
    }

    // 실행 객체의 수명을 연장하지 않고 살아 있는 구독만 해제하기 위한 기록
    class ExecutionSubscription
    {
        readonly WeakReference<SkillExecutionContext> _execution;
        readonly SoundPlayer _player;
        readonly UnityEngine.Object _owner;
        readonly SoundCue _executedCue;
        readonly SoundCue _impactCue;
        bool _active = true;
        bool _executed;

        /// <summary>
        /// 약한 참조가 가리키는 실행 객체의 잔존 여부
        /// </summary>
        public bool IsAlive => _execution.TryGetTarget(out _);

        void HandleExecuted(CombatActionInfo action, Vector3 position)
        {
            if (!_active || _executed) return;
            _executed = true;
            _player.Play(_executedCue, position, _owner);
        }

        void HandleImpact(SkillImpactInfo impact)
        {
            if (!_active) return;
            // 범위와 지속 피해도 실제 적중 위치 사용, 중첩 제한은 재생기 소유
            _player.Play(_impactCue, impact.Position, _owner);
        }

        /// <summary>
        /// 하나의 확정 실행에 발동음과 명중음 구독
        /// </summary>
        /// <param name="execution">관찰할 실행 객체</param>
        /// <param name="player">같은 씬의 소리 재생기</param>
        /// <param name="owner">재생 소유자</param>
        /// <param name="executedCue">발동음 설정</param>
        /// <param name="impactCue">명중음 설정</param>
        public ExecutionSubscription(SkillExecutionContext execution, SoundPlayer player, UnityEngine.Object owner, SoundCue executedCue, SoundCue impactCue)
        {
            _execution = new WeakReference<SkillExecutionContext>(execution);
            _player = player;
            _owner = owner;
            _executedCue = executedCue;
            _impactCue = impactCue;
            execution.OnExecuted += HandleExecuted;
            execution.OnImpact += HandleImpact;
        }

        /// <summary>
        /// 실행의 구독 해제와 이미 전달 중인 콜백 무효화
        /// </summary>
        public void Dispose()
        {
            _active = false;
            if (!_execution.TryGetTarget(out SkillExecutionContext execution)) return;
            execution.OnExecuted -= HandleExecuted;
            execution.OnImpact -= HandleImpact;
        }
    }

    [Header("----- 기본 공격 -----")]
    [SerializeField] SoundCue _basicExecutedCue;
    [SerializeField] SoundCue _basicImpactCue;

    [Header("----- 소리를 사용할 스킬 -----")]
    [Tooltip("등록하지 않은 스킬은 의도적으로 무음, 등록 항목은 발동음과 명중음 필수")]
    [SerializeField] SkillSoundBinding[] _skillSounds = Array.Empty<SkillSoundBinding>();

    readonly List<ExecutionSubscription> _executions = new List<ExecutionSubscription>();
    readonly Dictionary<SkillDefinition, SkillSoundBinding> _skillLookup = new Dictionary<SkillDefinition, SkillSoundBinding>();
    CombatController _combat;
    SoundPlayer _player;
    bool _listening;

    public bool IsListening => _listening;

    void OnEnable()
    {
        if (_combat == null || _player == null)
            throw new MissingReferenceException($"{name}: 활성화 전 전투와 씬 재생기 연결 필요");
        _combat.OnExecutionCreated += HandleExecutionCreated;
        _listening = true;
    }

    void OnDisable()
    {
        StopListening();
    }

    void OnDestroy()
    {
        StopListening();
    }

    void StopListening()
    {
        _listening = false;
        if (_combat != null)
            _combat.OnExecutionCreated -= HandleExecutionCreated;

        foreach (ExecutionSubscription subscription in _executions)
            subscription.Dispose();
        _executions.Clear();

        // 씬 종료 중 재생기가 먼저 제거된 경우만 제외
        if (_player != null)
            _player.StopOwner(this);
    }

    void ValidateCue(SoundCue cue)
    {
        if (cue == null)
            throw new MissingReferenceException($"{name}: 전투 발동음과 명중음 설정 필요");
        cue.ValidateConfiguration();
        if (cue.Loop)
            throw new InvalidOperationException($"{cue.name}: 전투 이벤트에는 단발음 설정 필요");
    }

    void HandleExecutionCreated(SkillExecutionContext execution)
    {
        if (!_listening) return;

        // 행동 종료 후에도 투사체와 지속 피해의 명중은 이어질 수 있음
        // 끝난 실행 객체는 강하게 보유하지 않고 다음 발동에서 기록 회수
        for (int index = _executions.Count - 1; index >= 0; index--)
        {
            if (!_executions[index].IsAlive)
                _executions.RemoveAt(index);
        }

        SoundCue executed;
        SoundCue impact;
        if (execution.Action.Kind == CombatActionKind.BasicAttack)
        {
            executed = _basicExecutedCue;
            impact = _basicImpactCue;
        }
        else
        {
            SkillDefinition skill = execution.Action.Skill;
            // 목록에 없는 스킬은 설정에서 제외한 무음 범위
            if (skill == null || !_skillLookup.TryGetValue(skill, out SkillSoundBinding binding)) return;
            executed = binding.ExecutedCue;
            impact = binding.ImpactCue;
        }

        _executions.Add(new ExecutionSubscription(execution, _player, this, executed, impact));
    }

    /// <summary>
    /// 비활성 캐릭터 구성 단계에서 전투 알림과 씬 재생기 연결
    /// </summary>
    /// <param name="combat">관찰할 전투 담당자</param>
    /// <param name="player">같은 씬의 활성 소리 재생기</param>
    public void Bind(CombatController combat, SoundPlayer player)
    {
        if (gameObject.activeInHierarchy || _listening || _combat != null)
            throw new InvalidOperationException($"{name}: 연결되지 않은 비활성 Presenter에서 구성 필요");
        if (combat == null || player == null)
            throw new MissingReferenceException($"{name}: CombatController와 SoundPlayer 연결 필요");
        if (!player.isActiveAndEnabled || player.gameObject.scene != gameObject.scene || combat.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 전투와 활성 재생기 필요");

        ValidateCue(_basicExecutedCue);
        ValidateCue(_basicImpactCue);
        if (_skillSounds == null)
            throw new MissingReferenceException($"{name}: 스킬 소리 목록 필요");

        _skillLookup.Clear();
        foreach (SkillSoundBinding binding in _skillSounds)
        {
            if (binding == null || binding.Skill == null)
                throw new MissingReferenceException($"{name}: 스킬 소리 항목의 Skill 연결 필요");
            ValidateCue(binding.ExecutedCue);
            ValidateCue(binding.ImpactCue);
            if (_skillLookup.ContainsKey(binding.Skill))
                throw new InvalidOperationException($"{name}: 동일 스킬의 소리 중복 연결");
            _skillLookup.Add(binding.Skill, binding);
        }

        _combat = combat;
        _player = player;
    }

    /// <summary>
    /// 재생 요청 구독과 캐릭터 및 씬 참조 해제
    /// </summary>
    public void Unbind()
    {
        StopListening();
        _combat = null;
        _player = null;
        _skillLookup.Clear();
    }
}
