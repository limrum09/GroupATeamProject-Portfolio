using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 로비 캐릭터 교체 시 새 외형의 지연 등장과 에너지 실체화 연출
/// </summary>
[DisallowMultipleComponent]
public class LobbyCharacterSwapEffect : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] Material _materialize;
    [SerializeField] ParticleSystem _appear;
    [SerializeField] ParticleSystem _beam;

    [Header("----- 연출 시간 -----")]
    [SerializeField, Min(0f)] float _revealDelay = 0.1f;
    [SerializeField, Min(0.01f)] float _materializeDuration = 0.6f;

    [Header("----- 방출 수 -----")]
    [SerializeField, Min(0)] int _appearCount = 30;
    [SerializeField, Min(0)] int _beamCount = 12;

    enum Phase
    {
        Idle,
        Hidden,
        Materializing
    }

    static readonly int ProgressId = Shader.PropertyToID("_Progress");

    readonly List<Renderer> _hidden = new List<Renderer>();
    readonly List<SkinnedMeshRenderer> _skinned = new List<SkinnedMeshRenderer>();
    readonly List<Renderer> _overlays = new List<Renderer>();

    MaterialPropertyBlock _block;
    Mesh _emitMesh;
    Animator _target;
    Transform _ground;
    Phase _phase;
    float _revealAt;
    float _materializeStartedAt;

    void Awake()
    {
        if (_materialize == null || _appear == null || _beam == null)
            throw new MissingReferenceException($"{name}: 실체화 Material과 등장·빛기둥 ParticleSystem 연결 필요");

        ValidateSystem(_appear);

        // 캐릭터 위치로 옮긴 뒤 방출하므로 이미 방출된 빛기둥은 월드에 남아야 함
        if (_beam.main.simulationSpace != ParticleSystemSimulationSpace.World)
            throw new InvalidOperationException($"{_beam.name}: World 시뮬레이션 공간 필요");

        _block = new MaterialPropertyBlock();

        // 방출 직후 위치 샘플링이 끝나므로 렌더러마다 재사용하는 단일 굽기 메시
        _emitMesh = new Mesh { name = "LobbyCharacterSwapEmit" };
    }

    void LateUpdate()
    {
        if (_phase == Phase.Idle)
            return;

        // 연출 중 교체나 씬 정리로 더미가 파괴된 경우 즉시 종료
        if (_target == null)
        {
            Finish();
            return;
        }

        if (_phase == Phase.Hidden)
        {
            if (Time.time < _revealAt)
                return;

            Reveal();
        }

        float progress = (Time.time - _materializeStartedAt) / _materializeDuration;

        if (progress >= 1f)
        {
            Finish();
            return;
        }

        _block.SetFloat(ProgressId, progress);

        foreach (Renderer overlay in _overlays)
            overlay.SetPropertyBlock(_block);
    }

    void OnDisable()
    {
        // 비활성화 이후 숨김 상태가 남지 않도록 연출 없이 즉시 종료
        Finish();
    }

    void OnDestroy()
    {
        if (_emitMesh != null)
            Destroy(_emitMesh);
    }

    void ValidateSystem(ParticleSystem system)
    {
        // 렌더러마다 방출 위치를 옮기므로 이미 방출된 입자는 월드에 남아야 함
        if (system.main.simulationSpace != ParticleSystemSimulationSpace.World)
            throw new InvalidOperationException($"{system.name}: World 시뮬레이션 공간 필요");

        ParticleSystem.ShapeModule shape = system.shape;

        if (!shape.enabled || shape.shapeType != ParticleSystemShapeType.Mesh)
            throw new InvalidOperationException($"{system.name}: Mesh Shape 필요");

        if (system.transform.lossyScale != Vector3.one)
            throw new InvalidOperationException($"{system.name}: 굽기 메시에 월드 스케일이 포함되므로 방출기 스케일 1 필요");
    }

    void Hide(Animator character)
    {
        // GameObject 대신 렌더러만 꺼서 Animator 진행 유지
        foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>(false))
        {
            if (!renderer.enabled)
                continue;

            renderer.enabled = false;
            _hidden.Add(renderer);
        }
    }

    void Reveal()
    {
        RestoreHidden();

        // 숨김 동안 컬링으로 멈췄을 수 있는 자세 갱신
        _target.Update(0f);

        List<MeshFilter> statics = new List<MeshFilter>();

        foreach (SkinnedMeshRenderer renderer in _target.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (renderer.enabled && renderer.sharedMesh != null)
                _skinned.Add(renderer);
        }

        foreach (MeshFilter filter in _target.GetComponentsInChildren<MeshFilter>(false))
        {
            if (filter.sharedMesh != null && filter.TryGetComponent(out MeshRenderer renderer) && renderer.enabled)
                statics.Add(filter);
        }

        if (_skinned.Count == 0)
            throw new InvalidOperationException($"{_target.name}: 표시 중인 SkinnedMeshRenderer 필요");

        foreach (SkinnedMeshRenderer source in _skinned)
            _overlays.Add(CreateSkinnedOverlay(source));

        foreach (MeshFilter source in statics)
            _overlays.Add(CreateStaticOverlay(source));

        if (_appearCount > 0)
            EmitAppear();

        // 등장 순간 위에서 캐릭터까지 이어지는 세로 빛기둥
        if (_beamCount > 0)
        {
            // Animator 위치는 외형마다 달라 더미 표시 위치 기준
            _beam.transform.SetPositionAndRotation(_ground.position, _target.transform.rotation);
            _beam.Emit(_beamCount);
        }

        _phase = Phase.Materializing;
        _materializeStartedAt = Time.time;
    }

    /// <summary>
    /// 원본과 같은 본으로 스키닝되는 오버레이 렌더러 생성
    /// </summary>
    /// <param name="source">덮을 원본 SkinnedMeshRenderer</param>
    /// <returns>생성된 오버레이 렌더러</returns>
    SkinnedMeshRenderer CreateSkinnedOverlay(SkinnedMeshRenderer source)
    {
        Transform anchor = CreateOverlayAnchor(source.transform);
        SkinnedMeshRenderer overlay = anchor.gameObject.AddComponent<SkinnedMeshRenderer>();

        overlay.sharedMesh = source.sharedMesh;
        overlay.bones = source.bones;
        overlay.rootBone = source.rootBone;
        overlay.localBounds = source.localBounds;
        overlay.updateWhenOffscreen = source.updateWhenOffscreen;

        for (int i = 0; i < source.sharedMesh.blendShapeCount; i++)
            overlay.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));

        ConfigureOverlay(overlay, source.sharedMesh.subMeshCount);
        return overlay;
    }

    /// <summary>
    /// 원본 위치에 겹치는 정적 메시 오버레이 렌더러 생성
    /// </summary>
    /// <param name="source">덮을 원본 MeshFilter</param>
    /// <returns>생성된 오버레이 렌더러</returns>
    MeshRenderer CreateStaticOverlay(MeshFilter source)
    {
        Transform anchor = CreateOverlayAnchor(source.transform);
        anchor.gameObject.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;

        MeshRenderer overlay = anchor.gameObject.AddComponent<MeshRenderer>();
        ConfigureOverlay(overlay, source.sharedMesh.subMeshCount);
        return overlay;
    }

    Transform CreateOverlayAnchor(Transform source)
    {
        // 같은 부모와 로컬 변환으로 원본과 동일한 월드 크기와 자세 유지
        GameObject anchor = new GameObject($"{source.name}_MaterializeOverlay") { layer = source.gameObject.layer };
        Transform t = anchor.transform;

        t.SetParent(source.parent, false);
        t.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        t.localScale = source.localScale;
        return t;
    }

    void ConfigureOverlay(Renderer overlay, int subMeshCount)
    {
        Material[] materials = new Material[subMeshCount];

        for (int i = 0; i < materials.Length; i++)
            materials[i] = _materialize;

        overlay.sharedMaterials = materials;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;

        _block.SetFloat(ProgressId, 0f);
        overlay.SetPropertyBlock(_block);
    }

    void EmitAppear()
    {
        int totalVertices = 0;

        foreach (SkinnedMeshRenderer renderer in _skinned)
            totalVertices += renderer.sharedMesh.vertexCount;

        ParticleSystem.ShapeModule shape = _appear.shape;
        Transform emitter = _appear.transform;

        foreach (SkinnedMeshRenderer renderer in _skinned)
        {
            // 정점 수 비율로 부위별 방출량 분배
            int share = Mathf.Max(1, Mathf.RoundToInt(_appearCount * (float)renderer.sharedMesh.vertexCount / totalVertices));

            // 월드 스케일이 포함된 굽기 결과이므로 위치와 회전만 적용
            renderer.BakeMesh(_emitMesh, false);
            emitter.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            shape.mesh = _emitMesh;
            _appear.Emit(share);
        }
    }

    void RestoreHidden()
    {
        foreach (Renderer renderer in _hidden)
        {
            if (renderer != null)
                renderer.enabled = true;
        }

        _hidden.Clear();
    }

    void Finish()
    {
        RestoreHidden();

        // 더미와 함께 이미 파괴된 오버레이는 제외
        foreach (Renderer overlay in _overlays)
        {
            if (overlay != null)
                Destroy(overlay.gameObject);
        }

        _overlays.Clear();
        _skinned.Clear();
        _target = null;
        _ground = null;
        _phase = Phase.Idle;
    }

    /// <summary>
    /// 새 외형을 지연 시간 동안 숨긴 뒤 에너지 실체화로 등장
    /// </summary>
    /// <param name="incoming">방금 생성된 새 더미 Animator</param>
    /// <param name="ground">더미가 서는 표시 위치, 빛기둥 바닥 기준</param>
    public void Play(Animator incoming, Transform ground)
    {
        if (incoming == null)
            throw new ArgumentNullException(nameof(incoming));

        if (ground == null)
            throw new ArgumentNullException(nameof(ground));

        // 연속 교체 시 진행 중인 이전 연출 즉시 종료
        Finish();

        Hide(incoming);
        _target = incoming;
        _ground = ground;
        _phase = Phase.Hidden;
        _revealAt = Time.time + _revealDelay;
    }
}
