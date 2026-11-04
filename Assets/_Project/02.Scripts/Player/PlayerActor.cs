using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 공통 플레이어의 외형 구성과 런 준비 및 조작 허용과 해제
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerActor : MonoBehaviour
{
    [Header("----- 플레이어 구성 -----")]
    [SerializeField] CharacterStatus _status;
    [SerializeField] PlayerController _controller;
    [SerializeField] PlayerInputReader _input;
    [SerializeField] PlayerMovement _movement;
    [SerializeField] CombatController _combat;
    [SerializeField] CombatEffectPresenter _effectPresenter;

    [Header("----- 외형 생성 위치 -----")]
    [SerializeField] Transform _visualRoot;

    Camera _inputCamera;
    EventSystem _eventSystem;
    CombatEffect _effectPlayer;

    PlayerAnimation _visual;
    Guid _runId;

    bool _prepared;
    bool _preparing;
    bool _releasing;
    bool _controlRequested;
    bool _inputBlocked;
    bool _placementBlocked;

    public CharacterStatus Status => _status;
    public CombatTarget Target => _combat.Owner;
    public Guid RunId => _runId;
    public bool IsPrepared => _prepared;
    public SkillLoadout Loadout => _combat.Skills;
    public Pose CurrentPose => new Pose(transform.position, transform.rotation);

    void OnDisable()
    {
        // 일시적인 비활성화에서는 외형과 런 성장 및 스킬 유지
        DisableControl();
    }

    void OnDestroy()
    {
        _prepared = false;
        _runId = Guid.Empty;

        DisableControl();

        if (_effectPresenter != null)
            _effectPresenter.enabled = false;

        try
        {
            ClearVisual();
        }
        finally
        {
            ClearInjectedReferences();
        }
    }

    void ValidateComposition()
    {
        if (_status == null || _controller == null || _input == null || _movement == null || _combat == null || _effectPresenter == null || _visualRoot == null)
            throw new MissingReferenceException($"{name}: 플레이어 구성 요소와 VisualRoot 연결 필요");

        if (_inputCamera == null || _eventSystem == null || _effectPlayer == null)
            throw new MissingReferenceException($"{name}: Camera, EventSystem, CombatEffect 연결 필요");

        CombatTarget owner = _combat.Owner;

        if (owner == null || owner.Status != _status || owner.ActorTransform != transform || owner.Team != CombatTeam.Player)
            throw new InvalidOperationException($"{name}: PlayerActor 루트와 플레이어 전투 소유자 연결 확인 필요");

        if (_visualRoot == transform || !_visualRoot.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: VisualRoot는 PlayerRoot의 별도 자식으로 배치 필요");

        if (_effectPlayer.transform.IsChildOf(transform) || _effectPlayer.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 이펙트 재생기는 같은 씬의 PlayerRoot 외부에 배치 필요");
    }

    void ValidateRuntimeReady()
    {
        if (!_status.IsInitialized || _status.RunId != _runId || !_status.IsAlive)
            throw new InvalidOperationException($"{name}: 같은 런으로 초기화한 생존 플레이어 필요");

        if (!_inputCamera.isActiveAndEnabled || !_eventSystem.isActiveAndEnabled || !_effectPlayer.isActiveAndEnabled || !_input.HasSceneBindings)
            throw new InvalidOperationException($"{name}: 활성 씬 의존성과 입력 연결 완료 필요");

        if (!_movement.IsReadyForControl)
            throw new InvalidOperationException($"{name}: NavMesh 위에 활성 이동 구성 배치 필요");
    }

    void RequirePreparation(Guid runId)
    {
        if (!_preparing || _releasing || _runId != runId || !enabled || _visual == null)
            throw new OperationCanceledException($"{name}: 플레이어 준비 중 연결 해제 또는 준비 취소");

        if (_input.enabled || _controller.enabled || _combat.enabled)
            throw new InvalidOperationException($"{name}: 준비 완료 전 입력, 조작, 전투 활성화 불가");
    }

    void RequirePrepared(Guid runId)
    {
        if (_preparing || _releasing || !_prepared || runId == Guid.Empty || _runId != runId || _status == null || _visual == null || !_visual.IsPrepared || !_controller.HasPreparedAnimation)
            throw new InvalidOperationException($"{name}: 요청한 런의 플레이어 준비 완료 필요");
    }

    void StopControlComponents(bool stopCombat)
    {
        if (_input != null)
            _input.enabled = false;

        if (_controller != null)
            _controller.enabled = false;

        if (stopCombat && _combat != null)
            _combat.enabled = false;

        if (_movement != null)
            _movement.Stop();
    }

    void ApplyControlState()
    {
        if (!_controlRequested || _placementBlocked || !_prepared || _preparing || _releasing || !isActiveAndEnabled)
        {
            StopControlComponents(true);
            return;
        }

        try
        {
            RequirePrepared(_runId);

            if (!_status.IsAlive)
            {
                DisableControl();
                return;
            }

            ValidateRuntimeReady();

            _combat.enabled = true;

            if (!_combat.isActiveAndEnabled)
                throw new InvalidOperationException($"{name}: 전투 컴포넌트 활성화 실패");

            if (_inputBlocked)
            {
                // 상점에서는 전투 처리를 유지하며 조작만 중단
                StopControlComponents(false);
                return;
            }

            _controller.enabled = true;

            if (!_controller.isActiveAndEnabled)
                throw new InvalidOperationException($"{name}: 조작 컴포넌트 활성화 실패");

            _input.enabled = true;

            if (!_input.isActiveAndEnabled)
                throw new InvalidOperationException($"{name}: 입력 컴포넌트 활성화 실패");
        }
        catch
        {
            DisableControl();
            throw;
        }
    }


    void ClearInjectedReferences()
    {
        try
        {
            if (_input != null)
                _input.UnbindScene();
        }
        finally
        {
            try
            {
                if (_effectPresenter != null)
                    _effectPresenter.UnbindEffectPlayer();
            }
            finally
            {
                _inputCamera = null;
                _eventSystem = null;
                _effectPlayer = null;
            }
        }
    }

    void ClearVisual()
    {
        PlayerAnimation visual = _visual;
        _visual = null;

        try
        {
            if (_controller != null)
                _controller.UnbindAnimation();
        }
        finally
        {
            if (visual != null)
            {
                visual.gameObject.SetActive(false);
                Destroy(visual.gameObject);
            }
        }
    }

    static void RequirePlayMode()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Play 중에 플레이어 준비 필요");
    }

    /// <summary>
    /// 준비된 플레이어의 배치 차단과 생존 상태 확인 후 이동 담당자에 배치 위임
    /// </summary>
    /// <param name="destination">목적 위치와 회전</param>
    /// <returns>배치 성공 여부</returns>
    public bool TryPlace(Pose destination)
    {
        RequirePrepared(_runId);

        if (!isActiveAndEnabled || !_placementBlocked)
            throw new InvalidOperationException($"{name}: 활성 플레이어의 배치 차단 설정 필요");

        if (!_status.IsAlive)
            return false;

        return _movement.TryPlace(destination);
    }

    /// <summary>
    /// 비활성 플레이어의 런 준비에 사용할 씬 의존성 지정
    /// </summary>
    /// <param name="inputCamera">월드 입력 좌표를 계산할 카메라</param>
    /// <param name="eventSystem">UI 입력 소비를 판정할 EventSystem</param>
    /// <param name="effectPlayer">같은 씬의 전투 이펙트 재생기</param>
    public void BindScene(Camera inputCamera, EventSystem eventSystem, CombatEffect effectPlayer)
    {
        RequirePlayMode();

        if (_prepared || _preparing || _releasing || gameObject.activeSelf)
            throw new InvalidOperationException($"{name}: 런 준비 전 비활성 플레이어에 씬 참조 연결 필요");

        if (inputCamera == null)
            throw new ArgumentNullException(nameof(inputCamera));

        if (eventSystem == null)
            throw new ArgumentNullException(nameof(eventSystem));

        if (effectPlayer == null)
            throw new ArgumentNullException(nameof(effectPlayer));

        _inputCamera = inputCamera;
        _eventSystem = eventSystem;
        _effectPlayer = effectPlayer;
    }

    /// <summary>
    /// 비활성 공통 루트에 선택 외형과 런 상태를 구성하고 조작 대기 상태로 준비
    /// </summary>
    /// <param name="definition">선택한 플레이어의 기본 정의</param>
    /// <param name="runId">GameFlow가 생성한 이번 런의 식별자</param>
    /// <param name="initialGrowth">Progression이 계산한 시작 성장 기여분</param>
    public void PrepareForRun(PlayerDefinition definition, Guid runId, CharacterStatValues initialGrowth)
    {
        RequirePlayMode();

        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (runId == Guid.Empty)
            throw new ArgumentException("유효한 런 식별자 필요", nameof(runId));

        if (_preparing || _releasing)
            throw new InvalidOperationException($"{name}: 준비 또는 해제 중 재진입 불가");

        ValidateComposition();

        if (_prepared)
        {
            RequirePrepared(runId);

            if (_status.Definition != definition)
                throw new InvalidOperationException($"{name}: 준비한 런의 캐릭터 변경 불가");

            // 같은 런에서는 체력, 성장, 스킬, 외형을 다시 초기화하지 않음
            return;
        }

        if (!enabled || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
            throw new InvalidOperationException($"{name}: 로드된 씬의 PlayerActor 활성 설정 필요");

        if (gameObject.activeSelf)
            throw new InvalidOperationException($"{name}: 비활성 PlayerRoot에서 새 런 준비 필요");

        if (transform.parent != null && !transform.parent.gameObject.activeInHierarchy)
            throw new InvalidOperationException($"{name}: PlayerRoot의 부모 계층 활성화 필요");

        if (_status.RunId == runId)
            throw new InvalidOperationException($"{name}: 이미 적용했거나 해제한 런 ID로 재준비 불가");

        PlayerAnimation visualPrefab = definition.GetRunVisualPrefab();

        _runId = runId;
        _preparing = true;

        try
        {
            DisableControl();

            // 비활성 부모 아래에서 외형 생성과 참조 연결
            _visual = Instantiate(visualPrefab, _visualRoot, false);
            _visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            _controller.BindAnimation(_visual);
            _input.BindScene(_inputCamera, _eventSystem);
            _effectPresenter.BindEffectPlayer(_effectPlayer);

            // 기본 상태와 런 성장 적용은 기존 Combat 메서드에 위임
            _status.InitializeForRun(definition, runId, initialGrowth);

            RequirePreparation(runId);

            _combat.InitializeFixedSkillsForRun(definition, runId);
            _combat.Skills.InitializeStartingSkills(definition, runId);

            RequirePreparation(runId);

            // CharacterStatus.Awake는 미리 준비한 런 상태 보존
            gameObject.SetActive(true);

            RequirePreparation(runId);

            _visual.Prepare();

            RequirePreparation(runId);
            ValidateRuntimeReady();

            // 입력과 전투는 비활성 상태로 유지
            _prepared = true;
        }
        catch
        {
            Release();
            throw;
        }
        finally
        {
            _preparing = false;
        }
    }

    /// <summary>
    /// 준비된 런의 조작 허용 요청과 현재 차단 상태 반영
    /// </summary>
    /// <param name="runId">조작을 허용할 준비된 런의 식별자</param>
    public void EnableControl(Guid runId)
    {
        RequirePrepared(runId);

        _controlRequested = true;
        ApplyControlState();
    }

    /// <summary>
    /// 런 조작 허용 해제와 입력 및 진행 중 행동 중단
    /// </summary>
    public void DisableControl()
    {
        _controlRequested = false;
        StopControlComponents(true);
    }

    /// <summary>
    /// GameFlow가 요청한 추가 입력 차단 설정
    /// </summary>
    /// <param name="blocked">추가 입력 차단 여부</param>
    public void SetInputBlocked(bool blocked)
    {
        if (_inputBlocked == blocked)
            return;

        _inputBlocked = blocked;
        ApplyControlState();
    }

    /// <summary>
    /// 월드 배치 중 입력과 전투 차단 설정
    /// </summary>
    /// <param name="blocked">배치 중 차단 여부</param>
    public void SetPlacementBlocked(bool blocked)
    {
        if (_placementBlocked == blocked)
            return;

        _placementBlocked = blocked;
        ApplyControlState();
    }

    /// <summary>
    /// 런 종료 또는 준비 실패 시 외형과 주입한 연결 정리
    /// </summary>
    public void Release()
    {
        if (_releasing)
            return;

        _releasing = true;
        _prepared = false;
        _runId = Guid.Empty;

        _controlRequested = false;
        _inputBlocked = false;
        _placementBlocked = false;

        try
        {
            DisableControl();
            ClearVisual();
        }
        finally
        {
            try
            {
                // Presenter 구독 해제 이후 외부 참조 제거
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);

                ClearInjectedReferences();
            }
            finally
            {
                _releasing = false;
            }
        }
    }
}
