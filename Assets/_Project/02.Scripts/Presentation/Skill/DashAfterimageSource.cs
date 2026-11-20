using UnityEngine;

/// <summary>
/// 외형 프리팹의 대시 잔상 대상 렌더러와 잔상 색 설정
/// </summary>
[DisallowMultipleComponent]
public class DashAfterimageSource : MonoBehaviour
{
    [Header("----- 잔상 대상 -----")]
    [SerializeField] SkinnedMeshRenderer[] _skinnedRenderers;
    [SerializeField] MeshFilter[] _staticMeshes;

    [Header("----- 잔상 색 -----")]
    [SerializeField, ColorUsage(true, true)] Color _color = new Color(0.3f, 1.6f, 1.3f, 1f);

    public SkinnedMeshRenderer[] SkinnedRenderers => _skinnedRenderers;
    public MeshFilter[] StaticMeshes => _staticMeshes;
    public Color Color => _color;

    void OnValidate()
    {
        if (_skinnedRenderers == null || _skinnedRenderers.Length == 0)
            Debug.LogError($"{name}: 잔상 대상 SkinnedMeshRenderer 연결 필요", this);
        else if (System.Array.IndexOf(_skinnedRenderers, null) >= 0)
            Debug.LogError($"{name}: 비어 있는 SkinnedMeshRenderer 항목 정리 필요", this);

        if (_staticMeshes != null && System.Array.IndexOf(_staticMeshes, null) >= 0)
            Debug.LogError($"{name}: 비어 있는 MeshFilter 항목 정리 필요", this);
    }
}
