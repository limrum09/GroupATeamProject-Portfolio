using System;
using UnityEngine;

/// <summary>
/// 플레이어의 실제 피격에 따른 카메라 혈흔 재생
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public class PlayerHitScreenVfx : MonoBehaviour
{
    static readonly int GlobalAlpha = Shader.PropertyToID("_GlobalAlpha");

    [Header("----- 피격 연출 설정 -----")]
    [SerializeField] MeshRenderer _renderer;
    [SerializeField, Min(0.01f)] float _duration = 0.35f;

    CombatTarget _target;
    MaterialPropertyBlock _properties;
    float _hitTime;

    void Awake()
    {
        _properties = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (_target == null || !_renderer.enabled) return;

        float progress = (Time.unscaledTime - _hitTime) / _duration;
        if (progress >= 1f)
        {
            Clear();
            return;
        }

        float alpha = 1f - progress;
        SetAlpha(alpha * alpha);
    }

    void OnDisable()
    {
        Unbind();
    }

    void HandleDamage(DamageRequest request, float appliedDamage)
    {
        if (appliedDamage <= 0f) return;

        _hitTime = Time.unscaledTime;
        SetAlpha(1f);
        _renderer.enabled = true;
    }

    void SetAlpha(float alpha)
    {
        _renderer.GetPropertyBlock(_properties);
        _properties.SetFloat(GlobalAlpha, alpha);
        _renderer.SetPropertyBlock(_properties);
    }

    /// <summary>
    /// 현재 런 플레이어의 피격 이벤트 연결
    /// </summary>
    /// <param name="target">피격 연출을 재생할 플레이어 전투 대상</param>
    public void Bind(CombatTarget target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (_renderer == null || _renderer.sharedMaterial == null ||
            !_renderer.sharedMaterial.HasProperty(GlobalAlpha))
            throw new MissingReferenceException($"{name}: 혈흔 MeshRenderer 및 Global Alpha 머티리얼 연결 필요");
        if (!isActiveAndEnabled || _target != null || _duration <= 0f ||
            float.IsNaN(_duration) || float.IsInfinity(_duration))
            throw new InvalidOperationException($"{name}: 활성 상태 및 미연결 피격 연출과 양수 재생 시간 필요");

        Clear();
        _target = target;
        _target.OnDamaged += HandleDamage;
    }

    /// <summary>
    /// 재생 중인 혈흔 제거
    /// </summary>
    public void Clear()
    {
        _renderer.enabled = false;
    }

    /// <summary>
    /// 플레이어 피격 이벤트 해제와 혈흔 제거
    /// </summary>
    public void Unbind()
    {
        if (_target != null)
            _target.OnDamaged -= HandleDamage;
        _target = null;

        if (_renderer != null)
            Clear();
    }
}
