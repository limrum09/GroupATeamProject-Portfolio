using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 이동기 대시 동안 외형 자세를 주기적으로 구워 서서히 사라지는 잔상으로 그리는 연출
/// </summary>
[DisallowMultipleComponent]
public class DashAfterimagePresenter : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] CombatController _combat;
    [SerializeField] Material _material;

    [Header("----- 잔상 설정 -----")]
    [SerializeField, Min(0.005f)] float _spawnInterval = 0.025f;
    [SerializeField, Min(0.01f)] float _fadeDuration = 0.3f;
    [SerializeField, Min(1)] int _maxGhosts = 16;

    [Header("----- 본체 발광 -----")]
    [SerializeField, Range(0f, 1f)] float _bodyGlowAlpha = 0.6f;
    [SerializeField, Min(0.01f)] float _bodyGlowFadeOut = 0.1f;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    readonly List<Ghost> _ghosts = new List<Ghost>();
    MaterialPropertyBlock _block;
    DashAfterimageSource _source;
    Mesh[] _glowMeshes;

    long _dashActionId;
    float _nextSpawnAt;
    float _glowEndedAt = float.NegativeInfinity;
    bool _subscribed;

    /// <summary>
    /// 한 순간의 외형 자세 스냅샷
    /// </summary>
    class Ghost
    {
        public Mesh[] BakedMeshes;
        public Matrix4x4[] BakedMatrices;
        public Matrix4x4[] StaticMatrices;
        public float SpawnedAt;
        public bool Active;
    }

    void Awake()
    {
        if (_combat == null || _material == null)
            throw new MissingReferenceException($"{name}: CombatController와 잔상 Material 연결 필요");

        _block = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        _combat.OnExecutionCreated += HandleExecutionCreated;
        _combat.OnActionEnded += HandleActionEnded;
        _subscribed = true;
    }

    void OnDisable()
    {
        if (_subscribed)
        {
            _combat.OnExecutionCreated -= HandleExecutionCreated;
            _combat.OnActionEnded -= HandleActionEnded;
            _subscribed = false;
        }

        StopDash(false);
        DeactivateGhosts();
    }

    void OnDestroy()
    {
        ReleaseMeshes();
    }

    void LateUpdate()
    {
        if (_source == null)
            return;

        // 애니메이션 갱신 이후의 자세 기준 스냅샷
        if (_dashActionId != 0 && Time.time >= _nextSpawnAt)
        {
            SpawnGhost();
            _nextSpawnAt = Time.time + _spawnInterval;
        }

        DrawGhosts();
        DrawBodyGlow();
    }

    void HandleExecutionCreated(SkillExecutionContext execution)
    {
        // 잔상 대상이 없는 외형은 선택 구성으로 연출 생략
        if (_source == null)
            return;

        if (!(execution.Action.Skill is MovementSkillDefinition movement) || movement.Mode != MovementSkillMode.Dash)
            return;

        _dashActionId = execution.Action.Id;
        _nextSpawnAt = Time.time;
    }

    void HandleActionEnded(long actionId, CombatActionResult result)
    {
        // 완료·취소·교체 모두 생성 중단, 이미 남은 잔상은 계속 소멸
        if (actionId == _dashActionId)
            StopDash(true);
    }

    void StopDash(bool fadeGlow)
    {
        // 정상 종료는 본체 발광을 서서히 끄고, 해제·비활성화는 즉시 끔
        _glowEndedAt = fadeGlow && _dashActionId != 0 ? Time.time : float.NegativeInfinity;
        _dashActionId = 0;
    }

    void SpawnGhost()
    {
        Ghost ghost = TakeGhost();
        SkinnedMeshRenderer[] skinned = _source.SkinnedRenderers;
        MeshFilter[] statics = _source.StaticMeshes;

        for (int i = 0; i < skinned.Length; i++)
        {
            Transform t = skinned[i].transform;
            // 월드 스케일이 포함된 굽기 결과이므로 위치와 회전만 적용
            skinned[i].BakeMesh(ghost.BakedMeshes[i], false);
            ghost.BakedMatrices[i] = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        }

        for (int i = 0; i < statics.Length; i++)
            ghost.StaticMatrices[i] = statics[i].transform.localToWorldMatrix;

        ghost.SpawnedAt = Time.time;
        ghost.Active = true;
    }

    Ghost TakeGhost()
    {
        Ghost oldest = _ghosts[0];

        foreach (Ghost ghost in _ghosts)
        {
            if (!ghost.Active)
                return ghost;

            if (ghost.SpawnedAt < oldest.SpawnedAt)
                oldest = ghost;
        }

        // 풀 초과 시 가장 오래된 잔상 재사용
        return oldest;
    }

    void DrawGhosts()
    {
        SkinnedMeshRenderer[] skinned = _source.SkinnedRenderers;
        MeshFilter[] statics = _source.StaticMeshes;
        int layer = gameObject.layer;

        foreach (Ghost ghost in _ghosts)
        {
            if (!ghost.Active)
                continue;

            float t = (Time.time - ghost.SpawnedAt) / _fadeDuration;

            if (t >= 1f)
            {
                ghost.Active = false;
                continue;
            }

            // DrawMesh 호출 시점에 PropertyBlock 값 복사
            _block.SetColor(ColorId, _source.Color);
            _block.SetFloat(AlphaId, 1f - t);

            for (int i = 0; i < skinned.Length; i++)
                DrawAllSubMeshes(ghost.BakedMeshes[i], ghost.BakedMatrices[i], layer);

            for (int i = 0; i < statics.Length; i++)
                DrawAllSubMeshes(statics[i].sharedMesh, ghost.StaticMatrices[i], layer);
        }
    }

    void DrawBodyGlow()
    {
        float alpha = _bodyGlowAlpha;

        if (_dashActionId == 0)
        {
            float t = (Time.time - _glowEndedAt) / _bodyGlowFadeOut;

            if (t >= 1f)
                return;

            alpha *= 1f - t;
        }

        SkinnedMeshRenderer[] skinned = _source.SkinnedRenderers;
        MeshFilter[] statics = _source.StaticMeshes;
        int layer = gameObject.layer;

        _block.SetColor(ColorId, _source.Color);
        _block.SetFloat(AlphaId, alpha);

        // 현재 자세를 매 프레임 다시 구워 본체 위에 덧그림
        for (int i = 0; i < skinned.Length; i++)
        {
            Transform t = skinned[i].transform;
            skinned[i].BakeMesh(_glowMeshes[i], false);
            DrawAllSubMeshes(_glowMeshes[i], Matrix4x4.TRS(t.position, t.rotation, Vector3.one), layer);
        }

        for (int i = 0; i < statics.Length; i++)
            DrawAllSubMeshes(statics[i].sharedMesh, statics[i].transform.localToWorldMatrix, layer);
    }

    void DrawAllSubMeshes(Mesh mesh, Matrix4x4 matrix, int layer)
    {
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
            Graphics.DrawMesh(mesh, matrix, _material, layer, null, sub, _block, ShadowCastingMode.Off, false);
    }

    void DeactivateGhosts()
    {
        foreach (Ghost ghost in _ghosts)
            ghost.Active = false;
    }

    void ReleaseMeshes()
    {
        foreach (Ghost ghost in _ghosts)
        {
            foreach (Mesh mesh in ghost.BakedMeshes)
                Destroy(mesh);
        }

        _ghosts.Clear();

        if (_glowMeshes != null)
        {
            foreach (Mesh mesh in _glowMeshes)
                Destroy(mesh);

            _glowMeshes = null;
        }
    }

    /// <summary>
    /// 생성된 외형의 잔상 대상 연결과 잔상 풀 준비
    /// </summary>
    /// <param name="source">외형 프리팹의 잔상 설정</param>
    public void BindVisual(DashAfterimageSource source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        if (_source != null)
            throw new InvalidOperationException($"{name}: 기존 외형 연결 해제 후 다시 연결 필요");

        _source = source;
        _glowMeshes = new Mesh[source.SkinnedRenderers.Length];

        for (int i = 0; i < _glowMeshes.Length; i++)
            _glowMeshes[i] = new Mesh { name = "DashBodyGlow" };

        for (int i = 0; i < _maxGhosts; i++)
        {
            Ghost ghost = new Ghost
            {
                BakedMeshes = new Mesh[source.SkinnedRenderers.Length],
                BakedMatrices = new Matrix4x4[source.SkinnedRenderers.Length],
                StaticMatrices = new Matrix4x4[source.StaticMeshes.Length]
            };

            for (int m = 0; m < ghost.BakedMeshes.Length; m++)
                ghost.BakedMeshes[m] = new Mesh { name = "DashAfterimage" };

            _ghosts.Add(ghost);
        }
    }

    /// <summary>
    /// 외형 제거 전 잔상 중단과 구운 메쉬 해제
    /// </summary>
    public void UnbindVisual()
    {
        StopDash(false);
        ReleaseMeshes();
        _source = null;
    }
}
