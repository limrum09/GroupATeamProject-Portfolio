using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 공격 이펙트와 플레이어 피격에 맞춘 메인 추적 카메라의 위치 흔들림
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public class CombatCameraShake : CinemachineExtension
{
    [Header("----- 흔들림 설정 -----")]
    [SerializeField, Min(0.01f)] float _duration = 0.10f;
    [SerializeField, Min(0f)] float _amplitude = 0.06f;
    [SerializeField, Min(0.01f)] float _hitDuration = 0.18f;
    [SerializeField, Min(0f)] float _hitAmplitude = 0.16f;

    bool _sessionActive;
    bool _attackPlaybackEnabled;
    double _startedAt = double.NegativeInfinity;
    double _hitStartedAt = double.NegativeInfinity;

    /// <summary>
    /// 최종 추적 위치에 감쇠하는 화면 방향 오프셋 적용
    /// </summary>
    /// <param name="vcam">계산 중인 가상 카메라</param>
    /// <param name="stage">현재 파이프라인 단계</param>
    /// <param name="state">현재 카메라 상태</param>
    /// <param name="deltaTime">파이프라인 갱신 간격</param>
    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize || !isActiveAndEnabled)
            return;

        // 정산 화면에서 게임 시간이 멈춰도 피격 흔들림 감쇠
        float hitProgress = (float)((Time.unscaledTimeAsDouble - _hitStartedAt) / _hitDuration);
        bool isHitPlaying = hitProgress < 1f;
        float progress = isHitPlaying ? hitProgress : (float)((Time.timeAsDouble - _startedAt) / _duration);
        if (progress >= 1f)
            return;

        float strength = (isHitPlaying ? _hitAmplitude : _amplitude) * (1f - progress);
        float phase = progress * Mathf.PI * 4f;
        Vector3 offset = new Vector3(Mathf.Sin(phase), Mathf.Sin(phase * 1.25f) * 0.5f, 0f);
        state.PositionCorrection += state.GetCorrectedOrientation() * offset * strength;
    }

    /// <summary>
    /// 새 세션의 흔들림 재생 허용
    /// </summary>
    public void BeginSession()
    {
        Clear();
        _sessionActive = true;
        _attackPlaybackEnabled = true;
    }

    /// <summary>
    /// 종료 요청 시 공격 흔들림을 중단하고 현재 프레임의 피격 통지 허용
    /// </summary>
    public void StopAttackPlayback()
    {
        _attackPlaybackEnabled = false;
        _startedAt = double.NegativeInfinity;
    }

    /// <summary>
    /// 세션 정리 시 새 요청 차단과 이미 시작된 피격 흔들림의 감쇠 유지
    /// </summary>
    public void EndSession()
    {
        StopAttackPlayback();
        _sessionActive = false;
    }

    /// <summary>
    /// 진행 중인 흔들림의 진폭을 누적하지 않고 재생 갱신
    /// </summary>
    public void Play()
    {
        if (_attackPlaybackEnabled && isActiveAndEnabled)
            _startedAt = Time.timeAsDouble;
    }

    /// <summary>
    /// 실제 피격에 대한 흔들림 시작 또는 갱신
    /// </summary>
    public void PlayHit()
    {
        if (_sessionActive && isActiveAndEnabled)
            _hitStartedAt = Time.unscaledTimeAsDouble;
    }

    /// <summary>
    /// 진행 중인 흔들림 해제
    /// </summary>
    public void Clear()
    {
        _startedAt = double.NegativeInfinity;
        _hitStartedAt = double.NegativeInfinity;
    }
}
