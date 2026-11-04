using System;
using System.Collections.Generic;

/// <summary>
/// 런 점수 진행 객체와 처치 점수 수신기의 수명 관리
/// </summary>
public class RunScoreSession
{
    readonly EnemyScoreRewardReceiver _receiver;
    readonly SessionProgressManager _world;
    readonly SessionEconomyManager _economy;
    readonly CombatTarget _playerTarget;
    RunStatistics _statistics;
    RunScoreProgression _progression;
    bool _ownsReceiver;

    void HandlePlayerDamaged(DamageRequest request, float appliedDamage)
    {
        _statistics.RecordDamageTaken(appliedDamage);
    }

    void StopRecordingStatistics()
    {
        if (_statistics == null)
            return;

        _receiver.Source.OnPlayerDamageApplied -= _statistics.RecordDamageDealt;
        _world.OnNodeEntered -= _statistics.RecordVisit;
        _economy.OnGoldAcquired -= _statistics.RecordGoldAcquired;
        _playerTarget.OnDamaged -= HandlePlayerDamaged;
        _statistics = null;
    }

    /// <summary>
    /// 이번 씬의 점수 수신기 연결
    /// </summary>
    /// <param name="receiver">적 처치 점수 수신기</param>
    /// <param name="world">노드 진입 결과를 전달하는 월드</param>
    /// <param name="economy">골드 획득 결과를 전달하는 세션 재화</param>
    /// <param name="playerTarget">실제 피격 결과를 전달하는 플레이어</param>
    public RunScoreSession(EnemyScoreRewardReceiver receiver, SessionProgressManager world,
        SessionEconomyManager economy, CombatTarget playerTarget)
    {
        if (receiver == null)
            throw new ArgumentNullException(nameof(receiver));

        _receiver = receiver;
        _world = world != null ? world : throw new ArgumentNullException(nameof(world));
        _economy = economy != null ? economy : throw new ArgumentNullException(nameof(economy));
        _playerTarget = playerTarget != null ? playerTarget : throw new ArgumentNullException(nameof(playerTarget));
    }

    /// <summary>
    /// 런 점수 객체 생성과 처치 점수 수신 시작
    /// </summary>
    /// <param name="runId">레벨 진행과 공유하는 런 식별자</param>
    /// <param name="difficulty">런 시작 시 선택한 난이도</param>
    public void Begin(Guid runId, Difficulty difficulty)
    {
        if (_progression != null || _receiver.IsBound || !_receiver.isActiveAndEnabled)
            throw new InvalidOperationException("이전 연결이 없는 활성 처치 점수 수신기 필요");

        _statistics = new RunStatistics();
        _progression = new RunScoreProgression(runId, _statistics, difficulty);
        _progression.Start();
        // Bind 도중 실패해도 세션 정리에서 연결 해제
        _ownsReceiver = true;
        _receiver.Bind(_progression);
        _receiver.Source.OnPlayerDamageApplied += _statistics.RecordDamageDealt;
        _world.OnNodeEntered += _statistics.RecordVisit;
        _economy.OnGoldAcquired += _statistics.RecordGoldAcquired;
        _playerTarget.OnDamaged += HandlePlayerDamaged;
    }

    /// <summary>
    /// 추가 처치 점수 수신 중단
    /// </summary>
    public void StopReceiving()
    {
        if (!_ownsReceiver)
            return;

        _receiver.Unbind();
        _ownsReceiver = false;
    }

    /// <summary>
    /// 정상 종료 사유로 점수 결과 확정
    /// </summary>
    /// <param name="reason">확정된 종료 사유</param>
    /// <param name="playerLevel">종료 시점 세션 레벨</param>
    /// <param name="loadout">종료 시점 스킬 슬롯</param>
    /// <returns>이번 런의 점수 정산 결과</returns>
    public RunSettlementResult Finish(RunEndReason reason, int playerLevel, SkillLoadout loadout)
    {
        if (_progression == null)
            throw new InvalidOperationException("점수 세션 시작 필요");

        StopReceiving();
        // 사망을 일으킨 타격의 피격 통지까지 수신한 뒤 해제
        StopRecordingStatistics();

        List<(int Slot, SkillDefinition Definition, int Level)> skills =
            new List<(int, SkillDefinition, int)>();
        for (int slot = 0; slot < loadout.SlotCount; slot++)
        {
            SkillInstance skill = loadout.GetSkill(slot);
            if (skill != null)
                skills.Add((slot, skill.Definition, skill.Level));
        }

        return _progression.Finish(reason, playerLevel, skills.ToArray());
    }

    /// <summary>
    /// 점수 수신 해제와 런 참조 폐기
    /// </summary>
    public void Release()
    {
        StopRecordingStatistics();
        StopReceiving();
        _progression = null;
    }
}
