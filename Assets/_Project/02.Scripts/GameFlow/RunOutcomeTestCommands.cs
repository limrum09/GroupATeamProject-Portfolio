using System;
using UnityEngine;

/// <summary>
/// 에디터에서 사망과 클리어 결산을 확인하는 테스트 명령
/// </summary>
[DisallowMultipleComponent]
public class RunOutcomeTestCommands : MonoBehaviour
{
    [Header("----- 테스트 대상 -----")]
    [SerializeField] GameFlowController _gameFlow;
    [SerializeField] PlayerActor _player;

#if UNITY_EDITOR
    bool CanRunTest()
    {
        if (!Application.isPlaying)
            return false;

        if (_gameFlow == null || _player == null)
            throw new MissingReferenceException($"{name}: 런 흐름과 플레이어 연결 필요");

        return _gameFlow.IsRunning && !_gameFlow.IsEndRequested;
    }

    [ContextMenu("테스트/플레이어 강제 사망")]
    void KillPlayer()
    {
        if (!CanRunTest())
            return;

        CombatSource source = new CombatSource(Guid.NewGuid(), CombatTeam.Enemy);
        DamageRequest request = new DamageRequest(source, float.MaxValue);
        _player.Target.ReceiveDamage(request);
    }

    [ContextMenu("테스트/강제 클리어")]
    void ClearRun()
    {
        if (CanRunTest())
            _gameFlow.RequestEndRun(RunEndReason.Cleared);
    }
#endif
}
