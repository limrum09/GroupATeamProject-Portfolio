using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 투사체 비행 중 검기 또는 월드 공간 잔상 재생과 정리
/// </summary>
[DisallowMultipleComponent]
public class ProjectileFlightVfx : MonoBehaviour
{
    [Header("----- 투사체 연출 설정 -----")]
    [SerializeField] TargetProjectile _projectile;
    [SerializeField] ParticleSystem _effectPrefab;
    [SerializeField] bool _trailOnly;
    [SerializeField, Min(0f)] float _trailLifetime = 0.55f;

    ParticleSystem _effect;
    Quaternion _rotationOffset;

    void Awake()
    {
        if (_projectile == null || _projectile.gameObject != gameObject || _effectPrefab == null)
            throw new InvalidOperationException($"{name}: 같은 오브젝트의 투사체와 연출 프리팹 연결 필요");
    }

    void Start()
    {
        float flightTime = _projectile.RemainingPositionFlightTime;
        if (_projectile.IsFlightFinished || (!_trailOnly && flightTime <= 0f))
            return;

        Transform anchor = _projectile.transform;
        _rotationOffset = _effectPrefab.transform.localRotation;
        _effect = Instantiate(_effectPrefab, anchor.position, anchor.rotation * _rotationOffset);

        if (_effect.gameObject.scene != gameObject.scene)
            SceneManager.MoveGameObjectToScene(_effect.gameObject, gameObject.scene);

        if (!_trailOnly)
        {
            // 검기의 크기·텍스처 애니메이션을 실제 비행 시간에 맞춰 재생
            ParticleSystem.MainModule main = _effect.main;
            main.startLifetime = flightTime;
        }

        _effect.Play(true);
    }

    void LateUpdate()
    {
        if (_effect == null)
            return;

        Transform anchor = _projectile.transform;
        _effect.transform.SetPositionAndRotation(anchor.position, anchor.rotation * _rotationOffset);

        if (_projectile.IsFlightFinished)
            FinishPlayback();
    }

    void OnDisable()
    {
        FinishPlayback();
    }

    void FinishPlayback()
    {
        if (_effect == null)
            return;

        // 검기는 즉시 제거하고 이미 방출한 월드 공간 잔상의 수명 유지
        _effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (!_trailOnly)
            _effect.Clear(false);

        Destroy(_effect.gameObject, _trailLifetime);
        _effect = null;
    }
}
