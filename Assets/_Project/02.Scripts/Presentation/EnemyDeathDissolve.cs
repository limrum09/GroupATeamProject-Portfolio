using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 사망 모델의 렌더 패스 등록, 진행도 전달 및 풀 반환 전 상태 복원
/// </summary>
public class EnemyDeathDissolve : MonoBehaviour
{
    public const string LayerName = "EnemyDeathDissolve";
    static readonly int ProgressId = Shader.PropertyToID("_DeathDissolveProgress");
    static readonly int SpreadProgressId = Shader.PropertyToID("_DeathSpreadProgress");

    [Header("----- 사망 연출 -----")]
    [SerializeField, Min(0.001f)] float _startDelay = 0.15f;
    [SerializeField, Min(0.001f)] float _duration = 1.05f;
    [SerializeField, Min(0.001f)] float _spreadDuration = 0.9f;

    readonly List<RendererState> _renderers = new List<RendererState>();
    static int _activePlaybacks;
    MaterialPropertyBlock _properties;
    Coroutine _playback;
    bool _registered;

    public static bool HasActivePlaybacks => _activePlaybacks > 0;

    class RendererState
    {
        public Renderer Renderer;
        public int Layer;
        public ShadowCastingMode Shadows;
        public MaterialPropertyBlock OriginalProperties;
        public Dictionary<int, MaterialPropertyBlock> SlotProperties;
    }

    void OnDisable()
    {
        if (_playback != null)
            StopCoroutine(_playback);

        _playback = null;
        RestoreRenderers();
    }

    void RegisterRenderers(Transform content, int layer)
    {
        foreach (Renderer renderer in content.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer))
                continue;

            MaterialPropertyBlock original = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(original);
            RendererState state = new RendererState
            {
                Renderer = renderer,
                Layer = renderer.gameObject.layer,
                Shadows = renderer.shadowCastingMode,
                OriginalProperties = original
            };

            // 슬롯별 오버라이드가 Renderer 전체의 진행도를 가리는 경우도 지원
            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                renderer.GetPropertyBlock(_properties, slot);
                if (_properties.isEmpty) continue;

                state.SlotProperties ??= new Dictionary<int, MaterialPropertyBlock>();
                MaterialPropertyBlock originalSlot = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(originalSlot, slot);
                state.SlotProperties.Add(slot, originalSlot);
            }

            _renderers.Add(state);
        }

        foreach (RendererState state in _renderers)
        {
            // 같은 GameObject의 렌더러 상태를 모두 수집한 뒤 레이어 전환
            state.Renderer.gameObject.layer = layer;
            state.Renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        if (_renderers.Count == 0) return;
        _activePlaybacks++;
        _registered = true;
    }

    void RestoreRenderers()
    {
        foreach (RendererState state in _renderers)
        {
            Renderer renderer = state.Renderer;
            if (renderer == null) continue;

            renderer.gameObject.layer = state.Layer;
            renderer.shadowCastingMode = state.Shadows;
            renderer.SetPropertyBlock(state.OriginalProperties.isEmpty ? null : state.OriginalProperties);

            if (state.SlotProperties == null) continue;
            foreach (KeyValuePair<int, MaterialPropertyBlock> slot in state.SlotProperties)
                renderer.SetPropertyBlock(slot.Value, slot.Key);
        }

        _renderers.Clear();
        if (_registered)
        {
            _activePlaybacks--;
            _registered = false;
        }
    }

    void SetProgress(float elapsed)
    {
        float progress = Mathf.Clamp01(elapsed / _duration);
        float spreadProgress = Mathf.Clamp01(elapsed / _spreadDuration);

        foreach (RendererState state in _renderers)
        {
            Renderer renderer = state.Renderer;
            if (renderer == null) continue;

            renderer.GetPropertyBlock(_properties);
            _properties.SetFloat(ProgressId, progress);
            _properties.SetFloat(SpreadProgressId, spreadProgress);
            renderer.SetPropertyBlock(_properties);

            if (state.SlotProperties == null) continue;
            foreach (int slot in state.SlotProperties.Keys)
            {
                renderer.GetPropertyBlock(_properties, slot);
                _properties.SetFloat(ProgressId, progress);
                _properties.SetFloat(SpreadProgressId, spreadProgress);
                renderer.SetPropertyBlock(_properties, slot);
            }
        }
    }

    bool TryRegisterRenderers(Transform content, int layer)
    {
        try
        {
            RegisterRenderers(content, layer);
            SetProgress(0f);
            return true;
        }
        catch (Exception error)
        {
            RestoreRenderers();
            Debug.LogException(error, this);
            return false;
        }
    }

    IEnumerator Animate(Transform content, int layer, Action completed)
    {
        yield return new WaitForSeconds(_startDelay);

        if (TryRegisterRenderers(content, layer))
        {
            float elapsed = 0f;
            while (elapsed < _duration)
            {
                yield return null;

                elapsed += Time.deltaTime;
                SetProgress(elapsed);
            }
        }

        // 모션이 남은 경우 풀에서 회수할 때까지 완전히 투명한 상태 유지
        _playback = null;
        completed.Invoke();
    }

    /// <summary>
    /// 사망 모션 시작 후 지연을 두고 공용 렌더 패스용 진행도 재생
    /// </summary>
    /// <param name="content">이번 소환의 적 콘텐츠 루트</param>
    /// <param name="completed">재생 완료 알림</param>
    public void Play(Transform content, Action completed)
    {
        if (_playback != null || _registered) return;

        int layer = LayerMask.NameToLayer(LayerName);
        if (layer < 0 || !isActiveAndEnabled || !(_startDelay > 0f) ||
            float.IsInfinity(_startDelay) || float.IsInfinity(_duration) ||
            !(_spreadDuration > 0f && _spreadDuration <= _duration))
            throw new InvalidOperationException($"{name}: 유효한 사망 연출 레이어와 재생 시간 설정 필요");

        _properties ??= new MaterialPropertyBlock();
        _playback = StartCoroutine(Animate(content, layer, completed));
    }
}
