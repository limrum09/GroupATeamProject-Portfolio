using UnityEngine;

/// <summary>공통 적 Animator의 상태 진행 알림 종류</summary>
public enum EnemyAnimationSignal
{
    Idle,
    Emergence,
    Hit,
    Die
}

/// <summary>명시적으로 연결된 수신자에 모델 애니메이션 진행 전달</summary>
public sealed class EnemyAnimationStateNotifier : StateMachineBehaviour
{
    [SerializeField] EnemyAnimationSignal _signal;

    EnemyAnimation _receiver;
    int _version;

    public EnemyAnimationSignal Signal => _signal;

    /// <summary>수신자와 현재 재생 세대 연결</summary>
    /// <param name="receiver">이번 모델의 수신자, 반환 시 null</param>
    public void Bind(EnemyAnimation receiver)
    {
        _receiver = receiver;
        _version = receiver != null ? receiver.PlaybackVersion : 0;
    }

    /// <summary>상태 진입 시 재생 세대와 대기 선택 소비</summary>
    /// <param name="animator">상태를 재생하는 Animator</param>
    /// <param name="stateInfo">진입한 상태 정보</param>
    /// <param name="layerIndex">재생 레이어 번호</param>
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 비활성 초기화와 풀 반환 시 수신자 미연결 구간
        if (_receiver == null) return;
        _version = _receiver.PlaybackVersion;
        if (_signal == EnemyAnimationSignal.Idle)
            _receiver.ConsumeIdleVariant(animator, stateInfo);
        _receiver.ObserveState(animator, _signal, stateInfo, _version);
    }

    /// <summary>현재 상태의 재생 진행 전달</summary>
    /// <param name="animator">상태를 재생하는 Animator</param>
    /// <param name="stateInfo">재생 중인 상태 정보</param>
    /// <param name="layerIndex">재생 레이어 번호</param>
    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (_receiver == null) return;
        _receiver.ObserveState(animator, _signal, stateInfo, _version);
    }

    /// <summary>종료 프레임의 재생 시간 전달</summary>
    /// <param name="animator">상태를 재생하는 Animator</param>
    /// <param name="stateInfo">종료하는 상태 정보</param>
    /// <param name="layerIndex">재생 레이어 번호</param>
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (_receiver == null) return;
        _receiver.ObserveState(animator, _signal, stateInfo, _version);
    }
}
