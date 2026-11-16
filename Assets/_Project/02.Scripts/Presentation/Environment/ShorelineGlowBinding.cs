using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 수면 너울과 렌더링 경계 관리 및 지원 셰이더의 선택적 주변 표면 바인딩
/// 독립 수면은 다른 Renderer나 머티리얼에 의존하지 않고 자체 수면만 관리
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ShorelineGlowBinding : MonoBehaviour
{
    [Header("----- 수면 연결 -----")]
    [SerializeField] MeshRenderer _waterRenderer;
    [SerializeField] MeshFilter _waterMesh;

    [Header("----- 너울 조절 -----")]
    [Tooltip("GPU 너울 높이의 전체 배율: 0은 정지, 1은 머티리얼 기본 높이, 2는 두 배")]
    [SerializeField, Min(0f), InspectorName("너울 크기")] float _swellSize = 1f;

    [Header("----- 선택적 주변 표면 연결 -----")]
    [Tooltip("주변 표면 기능을 지원하는 기존 수면용 선택 목록. 독립 수면 셰이더에서는 사용하지 않음")]
    [SerializeField] Renderer[] _receivers = Array.Empty<Renderer>();

    // 이 속성의 선언 여부로 독립 수면 계약 판정, 속성 값은 모드 전환에 사용하지 않음
    // 속성이 없는 기존 Water 셰이더는 기존 수신 표면 바인딩 유지
    static readonly int IndependentWaterId = Shader.PropertyToID("_ShorelineIndependentWater");
    static readonly int EnabledId = Shader.PropertyToID("_WaterSpillEnabled");
    static readonly int PlaneId = Shader.PropertyToID("_WaterPlane");
    static readonly int AxisXId = Shader.PropertyToID("_WaterAxisX");
    static readonly int AxisZId = Shader.PropertyToID("_WaterAxisZ");
    static readonly int BoundsId = Shader.PropertyToID("_WaterBounds");
    static readonly int GlowColorId = Shader.PropertyToID("_WaterGlowColor");
    static readonly int SpillHeightId = Shader.PropertyToID("_WaterSpillHeight");
    static readonly int SpillStrengthId = Shader.PropertyToID("_WaterSpillStrength");
    static readonly int ShadeWidthId = Shader.PropertyToID("_WaterShadeWidth");
    static readonly int ShadeStrengthId = Shader.PropertyToID("_WaterShadeStrength");
    static readonly int GapScaleId = Shader.PropertyToID("_WaterGapScale");
    static readonly int GapThresholdId = Shader.PropertyToID("_WaterGapThreshold");
    static readonly int GapSoftnessId = Shader.PropertyToID("_WaterGapSoftness");
    static readonly int FlowSpeedId = Shader.PropertyToID("_WaterFlowSpeed");
    static readonly int SwellAId = Shader.PropertyToID("_WaterSwellA");
    static readonly int SwellBId = Shader.PropertyToID("_WaterSwellB");
    static readonly int SourceSwellAId = Shader.PropertyToID("_SwellAmplitudeA");
    static readonly int SourceSwellBId = Shader.PropertyToID("_SwellAmplitudeB");

    static readonly string[] SurfaceSourceProperties =
    {
        "_EdgeColor", "_SpillHeight", "_SpillStrength", "_SurfaceShadeWidth",
        "_SurfaceShadeStrength", "_FragmentScale", "_FragmentThreshold",
        "_FragmentSoftness", "_FlowSpeed"
    };

    // 모두 없는 머티리얼은 GPU 너울이 없는 기존 평면 수면
    static readonly string[] SwellProperties =
    {
        "_SwellAmplitudeA", "_SwellWavelengthA", "_SwellPeriodA",
        "_SwellAmplitudeB", "_SwellWavelengthB", "_SwellPeriodB"
    };

    Renderer[] _boundReceivers = Array.Empty<Renderer>();
    MaterialPropertyBlock _propertyBlock;
    MaterialPropertyBlock _waterPropertyBlock;
    MaterialPropertyBlock _originalWaterProperties;
    MeshRenderer _swellRenderer;
    Material _sourceMaterial;
    Shader _sourceShader;
    Mesh _sourceMesh;
    MeshRenderer _boundsRenderer;
    bool _hasSwellProperties;
    bool _isIndependentWater;
    BindingState _lastState;
    bool _hasState;
    bool _needsBindingRefresh = true;
    string _configurationError;

#if UNITY_EDITOR
    double _nextEditorValidationTime;
#endif

    struct BindingState
    {
        public Vector4 Plane;
        public Vector4 AxisX;
        public Vector4 AxisZ;
        public Vector4 Bounds;
        public Color GlowColor;
        public float SpillHeight;
        public float SpillStrength;
        public float ShadeWidth;
        public float ShadeStrength;
        public float GapScale;
        public float GapThreshold;
        public float GapSoftness;
        public float FlowSpeed;
        public Vector4 SwellA;
        public Vector4 SwellB;
        public Vector2 VerticalBounds;
        public Vector3 BoundsPadding;

        /// <summary>
        /// 직전 표시 입력과의 동일 여부
        /// </summary>
        /// <param name="other">비교할 표시 입력</param>
        /// <returns>모든 표시 입력의 일치 여부</returns>
        public bool Matches(BindingState other)
        {
            return Plane.Equals(other.Plane) && AxisX.Equals(other.AxisX) &&
                AxisZ.Equals(other.AxisZ) && Bounds.Equals(other.Bounds) &&
                GlowColor.Equals(other.GlowColor) && SpillHeight == other.SpillHeight &&
                SpillStrength == other.SpillStrength && ShadeWidth == other.ShadeWidth &&
                ShadeStrength == other.ShadeStrength && GapScale == other.GapScale &&
                GapThreshold == other.GapThreshold && GapSoftness == other.GapSoftness &&
                FlowSpeed == other.FlowSpeed && SwellA.Equals(other.SwellA) &&
                SwellB.Equals(other.SwellB) && VerticalBounds.Equals(other.VerticalBounds) &&
                BoundsPadding.Equals(other.BoundsPadding);
        }
    }

    void OnEnable()
    {
#if UNITY_EDITOR
        _nextEditorValidationTime = 0d;
        UnityEditor.EditorApplication.update -= UpdateInEditor;
        UnityEditor.EditorApplication.update += UpdateInEditor;
#endif
        RefreshBindings();
    }

    void OnValidate()
    {
        _needsBindingRefresh = true;
    }

    void LateUpdate()
    {
        UpdateBindings();
    }

    void UpdateBindings()
    {
        if (ShouldDeferValidation()) return;

        if (_needsBindingRefresh || _waterRenderer == null || _waterMesh == null ||
            _waterRenderer.sharedMaterial != _sourceMaterial || _waterMesh.sharedMesh != _sourceMesh ||
            _sourceMaterial == null || _sourceMaterial.shader != _sourceShader ||
            _sourceMaterial.HasProperty(SwellProperties[0]) != _hasSwellProperties ||
            _sourceMaterial.HasProperty(IndependentWaterId) != _isIndependentWater)
        {
            RefreshBindings();
            return;
        }

        ApplyCurrentState();
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= UpdateInEditor;
#endif
        ClearBindings();
        _configurationError = null;
        _needsBindingRefresh = true;
    }

#if UNITY_EDITOR
    void UpdateInEditor()
    {
        if (!isActiveAndEnabled || Application.IsPlaying(gameObject)) return;

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now < _nextEditorValidationTime) return;
        _nextEditorValidationTime = now + 0.25d;

        // 씬 변경이 없어도 임포트 완료와 머티리얼 값 수정을 재검증
        UpdateBindings();
    }
#endif

    bool ShouldDeferValidation()
    {
#if UNITY_EDITOR
        if (UnityEditor.EditorApplication.isUpdating || UnityEditor.ShaderUtil.anythingCompiling)
        {
            // 일시적인 임포트 상태에서는 마지막 적용값을 유지하고 완료 후 연결 재검증
            _needsBindingRefresh = true;
            return true;
        }
#endif
        return false;
    }

    bool ValidateBindings(out string error)
    {
        if (!ValidateValue("너울 크기 (_swellSize)", _swellSize, _swellSize >= 0f, "0 이상", out error)) return false;

        if (_waterRenderer == null || _waterMesh == null || _waterMesh.sharedMesh == null)
        {
            error = "수면 MeshRenderer와 MeshFilter 연결이 필요합니다.";
            return false;
        }

        if (_waterRenderer.gameObject != _waterMesh.gameObject || _waterMesh.sharedMesh.bounds.size.y > 0.005f)
        {
            error = "동일 오브젝트의 평면 수면 메시를 연결하세요.";
            return false;
        }

        Material source = _waterRenderer.sharedMaterial;
        if (source == null)
        {
            error = "수면 머티리얼 연결이 필요합니다.";
            return false;
        }

        bool independentWater = source.HasProperty(IndependentWaterId);
        if (!independentWater)
        {
            foreach (string property in SurfaceSourceProperties)
            {
                if (source.HasProperty(property)) continue;
                error = $"수면 머티리얼에 {property} 설정이 없습니다.";
                return false;
            }
        }

        int swellPropertyCount = 0;
        foreach (string property in SwellProperties)
        {
            if (source.HasProperty(property)) swellPropertyCount++;
        }
        if (swellPropertyCount != 0 && swellPropertyCount != SwellProperties.Length)
        {
            error = "수면 머티리얼의 GPU 너울 설정 여섯 항목을 모두 제공하세요.";
            return false;
        }

        // 독립 수면은 저장되어 있던 수신 목록까지 읽거나 검증하지 않음
        if (independentWater)
        {
            error = null;
            return true;
        }

        if (_receivers == null)
        {
            error = "주변 표면 연결은 빈 배열 또는 명시적인 목록으로 지정하세요.";
            return false;
        }

        HashSet<Renderer> unique = new HashSet<Renderer>();
        foreach (Renderer receiver in _receivers)
        {
            if (receiver == null || !unique.Add(receiver))
            {
                error = "주변 표면 연결에 빈 항목 또는 중복 항목이 있습니다.";
                return false;
            }

            Material[] materials = receiver.sharedMaterials;
            if (materials.Length == 0)
            {
                error = $"{receiver.name}에 접촉 표면용 머티리얼을 지정하세요.";
                return false;
            }

            foreach (Material material in materials)
            {
                if (material == null || !material.HasProperty(EnabledId))
                {
                    error = $"{receiver.name}에 접촉 표면용 머티리얼을 지정하세요.";
                    return false;
                }
                if (swellPropertyCount > 0 &&
                    (!material.HasProperty(SwellAId) || !material.HasProperty(SwellBId)))
                {
                    error = $"{receiver.name}에 GPU 너울을 지원하는 접촉 표면용 머티리얼을 지정하세요.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    void RejectConfiguration(string error)
    {
        Material source = _waterRenderer != null ? _waterRenderer.sharedMaterial : null;
        string materialName = source != null ? source.name : "미지정";
        string shaderName = source != null && source.shader != null ? source.shader.name : "미지정";
        string message = $"머티리얼='{materialName}', 셰이더='{shaderName}': {error}";

        // 오류 효과를 정리하되 컴포넌트를 켜 둔 채 수정된 설정의 재검증 유지
        ClearBindings();
        _needsBindingRefresh = true;
        if (_configurationError == message) return;

        _configurationError = message;
        Debug.LogError($"{name}: {message}", this);
    }

    void AcceptConfiguration()
    {
        if (_configurationError == null) return;
        _configurationError = null;
        Debug.Log($"{name}: 설정이 정상화되어 표시 바인딩을 복구했습니다.", this);
    }

    void ClearBindings()
    {
        DisableReceivers();
        RestoreWaterProperties();
        RestoreWaterBounds();
        _boundReceivers = Array.Empty<Renderer>();
        _sourceMaterial = null;
        _sourceShader = null;
        _sourceMesh = null;
        _hasSwellProperties = false;
        _isIndependentWater = false;
        _hasState = false;
    }

    void DisableReceivers()
    {
        if (_propertyBlock == null) return;
        foreach (Renderer receiver in _boundReceivers)
        {
            // 수면보다 먼저 파괴된 선택 수신 표면 제외
            if (receiver == null) continue;
            receiver.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(EnabledId, 0f);
            receiver.SetPropertyBlock(_propertyBlock);
        }
    }

    void RestoreWaterBounds()
    {
        if (_boundsRenderer != null) _boundsRenderer.ResetLocalBounds();
        _boundsRenderer = null;
    }

    void RestoreWaterProperties()
    {
        if (_swellRenderer != null)
        {
            _swellRenderer.SetPropertyBlock(_originalWaterProperties.isEmpty ? null : _originalWaterProperties);
        }
        _swellRenderer = null;
    }

    bool ValidateValue(string property, float value, bool inRange, string requirement, out string error)
    {
        if (!float.IsNaN(value) && !float.IsInfinity(value) && inRange)
        {
            error = null;
            return true;
        }

        error = $"{property}={value:R} (조건: {requirement}, 유한한 값)";
        return false;
    }

    bool ValidateSwell(Vector4 swell, int propertyOffset, out string error)
    {
        return ValidateValue(SwellProperties[propertyOffset], swell.x, swell.x >= 0f, "0 이상", out error) &&
            ValidateValue(SwellProperties[propertyOffset + 1], swell.y, swell.y > 0f, "0 초과", out error) &&
            ValidateValue(SwellProperties[propertyOffset + 2], swell.z, swell.z > 0f, "0 초과", out error);
    }

    bool ValidateSurfaceState(BindingState state, out string error)
    {
        return ValidateValue("_SpillHeight", state.SpillHeight, state.SpillHeight > 0f, "0 초과", out error) &&
            ValidateValue("_SurfaceShadeWidth", state.ShadeWidth, state.ShadeWidth > 0f, "0 초과", out error) &&
            ValidateValue("_FragmentScale", state.GapScale, state.GapScale > 0f, "0 초과", out error) &&
            ValidateValue("_FragmentSoftness", state.GapSoftness, state.GapSoftness > 0f, "0 초과", out error) &&
            ValidateValue("_SpillStrength", state.SpillStrength, state.SpillStrength >= 0f, "0 이상", out error) &&
            ValidateValue("_SurfaceShadeStrength", state.ShadeStrength, state.ShadeStrength >= 0f && state.ShadeStrength <= 1f, "0~1", out error) &&
            ValidateValue("_FragmentThreshold", state.GapThreshold, true, "범위 제한 없음", out error) &&
            ValidateValue("_FlowSpeed", state.FlowSpeed, true, "범위 제한 없음", out error);
    }

    void ApplyCurrentState()
    {
        if (_boundReceivers.Length == 0 && !_hasSwellProperties)
        {
            AcceptConfiguration();
            return;
        }
        Matrix4x4 inverse = _waterMesh.transform.worldToLocalMatrix;
        Vector4 rawPlane = inverse.GetRow(1);
        Bounds bounds = _waterMesh.sharedMesh.bounds;
        rawPlane.w -= bounds.center.y;
        float normalLength = new Vector3(rawPlane.x, rawPlane.y, rawPlane.z).magnitude;
        if (!ValidateValue("수면 변환의 법선 길이", normalLength, normalLength >= 0.000001f, "0.000001 이상", out string transformError))
        {
            RejectConfiguration(transformError);
            return;
        }

        // 수면 머티리얼을 시각 설정의 단일 원본으로 사용
        BindingState state = new BindingState
        {
            Bounds = new Vector4(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z),
            // 평면·축을 비교하지 않는 독립 수면도 메시의 수직 경계 변경은 감지
            VerticalBounds = new Vector2(bounds.min.y, bounds.max.y),
            SwellA = new Vector4(0f, 1f, 1f, 0f),
            SwellB = new Vector4(0f, 1f, 1f, 0f)
        };
        if (_hasSwellProperties)
        {
            state.SwellA = new Vector4(_sourceMaterial.GetFloat("_SwellAmplitudeA"),
                _sourceMaterial.GetFloat("_SwellWavelengthA"), _sourceMaterial.GetFloat("_SwellPeriodA"), 0f);
            state.SwellB = new Vector4(_sourceMaterial.GetFloat("_SwellAmplitudeB"),
                _sourceMaterial.GetFloat("_SwellWavelengthB"), _sourceMaterial.GetFloat("_SwellPeriodB"), 0f);
            if (!ValidateSwell(state.SwellA, 0, out string swellError) ||
                !ValidateSwell(state.SwellB, 3, out swellError))
            {
                RejectConfiguration(swellError);
                return;
            }
            state.SwellA.x *= _swellSize;
            state.SwellB.x *= _swellSize;
            float totalAmplitude = state.SwellA.x + state.SwellB.x;
            if (!ValidateValue("_SwellAmplitudeA × _swellSize", state.SwellA.x, state.SwellA.x >= 0f, "0 이상", out swellError) ||
                !ValidateValue("_SwellAmplitudeB × _swellSize", state.SwellB.x, state.SwellB.x >= 0f, "0 이상", out swellError) ||
                !ValidateValue("너울 적용 높이 합계", totalAmplitude, totalAmplitude >= 0f, "0 이상", out swellError))
            {
                RejectConfiguration(swellError);
                return;
            }
            // 월드 수직 변위의 최대 범위를 로컬 축별 경계에 반영
            Vector3 offset = inverse.MultiplyVector(Vector3.up) * totalAmplitude;
            if (!ValidateValue("너울 경계 여유 X", offset.x, true, "범위 제한 없음", out swellError) ||
                !ValidateValue("너울 경계 여유 Y", offset.y, true, "범위 제한 없음", out swellError) ||
                !ValidateValue("너울 경계 여유 Z", offset.z, true, "범위 제한 없음", out swellError))
            {
                RejectConfiguration(swellError);
                return;
            }
            state.BoundsPadding = new Vector3(Mathf.Abs(offset.x), Mathf.Abs(offset.y), Mathf.Abs(offset.z));
        }
        if (!_isIndependentWater)
        {
            // 독립 수면에서는 사용하지 않는 평면·축을 변경 판정에서 제외
            // 단순 위치 이동 시 동일한 너울 속성과 로컬 렌더링 경계의 재적용 방지
            state.Plane = rawPlane / normalLength;
            state.AxisX = inverse.GetRow(0);
            state.AxisZ = inverse.GetRow(2);
            state.GlowColor = _sourceMaterial.GetColor("_EdgeColor");
            state.SpillHeight = _sourceMaterial.GetFloat("_SpillHeight");
            state.SpillStrength = _sourceMaterial.GetFloat("_SpillStrength");
            state.ShadeWidth = _sourceMaterial.GetFloat("_SurfaceShadeWidth");
            state.ShadeStrength = _sourceMaterial.GetFloat("_SurfaceShadeStrength");
            state.GapScale = _sourceMaterial.GetFloat("_FragmentScale");
            state.GapThreshold = _sourceMaterial.GetFloat("_FragmentThreshold");
            state.GapSoftness = _sourceMaterial.GetFloat("_FragmentSoftness");
            state.FlowSpeed = _sourceMaterial.GetFloat("_FlowSpeed");
        }
        if (!_isIndependentWater && _boundReceivers.Length > 0 && !ValidateSurfaceState(state, out string surfaceError))
        {
            RejectConfiguration(surfaceError);
            return;
        }

        if (_hasState && state.Matches(_lastState)) return;

        if (_hasSwellProperties)
        {
            // 공유 머티리얼의 기본 높이를 유지하며 이 수면에만 배율 적용
            _waterRenderer.GetPropertyBlock(_waterPropertyBlock);
            _waterPropertyBlock.SetFloat(SourceSwellAId, state.SwellA.x);
            _waterPropertyBlock.SetFloat(SourceSwellBId, state.SwellB.x);
            _waterRenderer.SetPropertyBlock(_waterPropertyBlock);
        }

        if (state.SwellA.x + state.SwellB.x > 0f)
        {
            Bounds renderBounds = bounds;
            renderBounds.extents += state.BoundsPadding;
            _waterRenderer.localBounds = renderBounds;
            _boundsRenderer = _waterRenderer;
        }
        else
        {
            RestoreWaterBounds();
        }

        foreach (Renderer receiver in _boundReceivers)
        {
            if (receiver == null) continue;
            receiver.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(EnabledId, 1f);
            _propertyBlock.SetVector(PlaneId, state.Plane);
            _propertyBlock.SetVector(AxisXId, state.AxisX);
            _propertyBlock.SetVector(AxisZId, state.AxisZ);
            _propertyBlock.SetVector(BoundsId, state.Bounds);
            _propertyBlock.SetColor(GlowColorId, state.GlowColor);
            _propertyBlock.SetFloat(SpillHeightId, state.SpillHeight);
            _propertyBlock.SetFloat(SpillStrengthId, state.SpillStrength);
            _propertyBlock.SetFloat(ShadeWidthId, state.ShadeWidth);
            _propertyBlock.SetFloat(ShadeStrengthId, state.ShadeStrength);
            _propertyBlock.SetFloat(GapScaleId, state.GapScale);
            _propertyBlock.SetFloat(GapThresholdId, state.GapThreshold);
            _propertyBlock.SetFloat(GapSoftnessId, state.GapSoftness);
            _propertyBlock.SetFloat(FlowSpeedId, state.FlowSpeed);
            _propertyBlock.SetVector(SwellAId, state.SwellA);
            _propertyBlock.SetVector(SwellBId, state.SwellB);
            receiver.SetPropertyBlock(_propertyBlock);
        }

        _lastState = state;
        _hasState = true;
        AcceptConfiguration();
    }

    /// <summary>
    /// 수면과 지원 셰이더의 선택 수신 표면 설정 재검증 및 표시 입력 갱신
    /// </summary>
    [ContextMenu("수면 설정 갱신")]
    public void RefreshBindings()
    {
        _needsBindingRefresh = true;
        // 명시적으로 비활성화한 수면은 재검증으로도 켜지지 않도록 유지
        if (!isActiveAndEnabled || ShouldDeferValidation()) return;

        ClearBindings();

        if (!ValidateBindings(out string error))
        {
            RejectConfiguration(error);
            return;
        }

        _sourceMaterial = _waterRenderer.sharedMaterial;
        _sourceShader = _sourceMaterial.shader;
        _sourceMesh = _waterMesh.sharedMesh;
        _hasSwellProperties = _sourceMaterial.HasProperty(SwellProperties[0]);
        _isIndependentWater = _sourceMaterial.HasProperty(IndependentWaterId);
        if (_hasSwellProperties)
        {
            if (_waterPropertyBlock == null) _waterPropertyBlock = new MaterialPropertyBlock();
            if (_originalWaterProperties == null) _originalWaterProperties = new MaterialPropertyBlock();
            _waterRenderer.GetPropertyBlock(_originalWaterProperties);
            _swellRenderer = _waterRenderer;
        }
        _boundReceivers = _isIndependentWater ? Array.Empty<Renderer>() : (Renderer[])_receivers.Clone();
        if (!_isIndependentWater && _propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
        _hasState = false;
        _needsBindingRefresh = false;
        ApplyCurrentState();
    }
}
