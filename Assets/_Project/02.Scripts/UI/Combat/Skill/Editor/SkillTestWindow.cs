using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 테스트 씬의 실제 시전과 피격 결과를 관찰하는 스킬 실험 창
/// </summary>
public class SkillTestWindow : EditorWindow
{
    const string TestScene = "Assets/_Project/01.Scene/TestScenes/Temp_Skill_Test_Scene.unity";
    const string StartOverrideKey = "SkillTestWindow.StartOverride";
    const string PreviousSceneKey = "SkillTestWindow.PreviousStartScene";
    const string RestartKey = "SkillTestWindow.Restart";
    static readonly string[] ClickModes = { "클릭 · 조준점", "클릭 · 더미 배치" };
    static readonly Color AimColor = new Color(1f, .82f, .18f);
    static readonly Color RangeColor = new Color(1f, .58f, .16f);
    static readonly Color PlayerColor = new Color(.32f, .85f, 1f);
    static readonly Color TargetColor = new Color(.46f, 1f, .62f);

    [SerializeField] SkillDefinition _skill;
    [SerializeField] Vector3 _origin;
    [SerializeField] float _yaw;
    [SerializeField] Vector3 _aim;
    [SerializeField] Vector3 _dummyPosition;
    [SerializeField] bool _hasAim;
    [SerializeField] bool _hasLayout;
    [SerializeField] bool _restoreLayout;
    [SerializeField] string _playerId;
    [SerializeField] string _dummyId;
    [SerializeField] float _zoom = 1f;

    PlayerActor _actor;
    PlayerController _controller;
    PlayerInputReader _reader;
    CombatController _combat;
    CombatTarget _dummy;
    PlayerActor[] _players = Array.Empty<PlayerActor>();
    CombatTarget[] _enemies = Array.Empty<CombatTarget>();
    string[] _playerLabels = { "선택" };
    string[] _enemyLabels = { "선택" };
    bool _ownsInput;
    [NonSerialized] Camera _camera;
    [NonSerialized] RenderTexture _texture;
    Vector3 _cursor;
    bool _hasCursor;
    bool _placeDummy;
    double _nextRepaint;
    double _nextListRefresh;
    Vector2 _scroll;
    readonly List<Rect> _captions = new List<Rect>(10);
    [NonSerialized] GUIStyle _captionStyle;

    bool _requesting;
    long _actionId;
    SkillExecutionContext _execution;
    CombatTarget _actualTarget;
    Vector3 _actualDirection;
    Vector3 _areaOrigin;
    Quaternion _areaRotation;
    Transform _followAnchor;
    bool _hasArea;
    bool? _overlapped;
    bool _hasDamage;
    DamageRequest _lastDamage;
    float _lastApplied;
    int _damageFrame;
    int _hits;
    float _damage;
    float _hpAtCast;
    float _hpAfterHit;
    bool _hasCast;
    bool _showCastRange;
    bool _hasLaunch;
    Vector3 _launchOrigin;
    Vector3 _launchDirection;
    Vector3 _impactPosition;
    string _message;

    Vector3 Direction => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
    static bool IsTestPlay => EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.GetSceneByPath(TestScene).isLoaded;

    [MenuItem("Tools/Skills/스킬 실험")]
    static void Open()
    {
        SkillTestWindow window = GetWindow<SkillTestWindow>("스킬 실험");
        window.ReadSelection();
        window.Show();
    }

    [InitializeOnLoadMethod]
    static void RegisterStartSceneRestore()
    {
        EditorApplication.playModeStateChanged -= RestoreStartScene;
        EditorApplication.playModeStateChanged += RestoreStartScene;
    }

    static void RestoreStartScene(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode && state != PlayModeStateChange.EnteredEditMode)
            return;
        if (!SessionState.GetBool(StartOverrideKey, false))
            return;
        if (AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == TestScene)
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousSceneKey, ""));
        SessionState.EraseBool(StartOverrideKey);
        SessionState.EraseString(PreviousSceneKey);
    }

    void OnEnable()
    {
        minSize = new Vector2(590f, 465f);
        wantsMouseMove = true;
        Selection.selectionChanged += ReadSelection;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayStateChanged;
        if (_skill == null) ReadSelection();
    }

    void OnDisable()
    {
        Selection.selectionChanged -= ReadSelection;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayStateChanged;
        ReleaseSession();
    }

    void OnDestroy()
    {
        SessionState.EraseBool(RestartKey);
    }

    void OnLostFocus()
    {
        ReleaseInput();
    }

    void ReadSelection()
    {
        if (!_restoreLayout && Selection.activeObject is SkillDefinition definition)
            SelectSkill(definition);
    }

    void SelectSkill(SkillDefinition definition)
    {
        if (_skill == definition)
            return;
        ClearObservation();
        _skill = definition;
        _message = "";
        Repaint();
    }

    void PlayStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
            ReleaseSession();
        if (state == PlayModeStateChange.EnteredPlayMode)
            RefreshActors();
        Repaint();
    }

    void StartTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;
        SceneAsset test = AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScene);
        if (test == null)
        {
            _message = "스킬 테스트 씬을 찾을 수 없습니다.";
            return;
        }
        SessionState.SetString(PreviousSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(StartOverrideKey, true);
        EditorSceneManager.playModeStartScene = test;
        EditorApplication.EnterPlaymode();
    }

    void RestartTest()
    {
        if (!IsTestPlay) return;
        _restoreLayout = _actor != null && !string.IsNullOrEmpty(_dummyId);
        SessionState.SetBool(RestartKey, true);
        EditorApplication.ExitPlaymode();
    }

    void ReleaseSession()
    {
        BindPlayer(null);
        BindDummy(null);
        ClearObservation();
        _players = Array.Empty<PlayerActor>();
        _enemies = Array.Empty<CombatTarget>();
        _playerLabels = new[] { "선택" };
        _enemyLabels = new[] { "선택" };
        _hasCursor = false;
        if (_camera != null)
        {
            _camera.targetTexture = null;
            DestroyImmediate(_camera.gameObject);
        }
        ReleaseTexture();
    }

    void ClearObservation()
    {
        if (_execution != null)
        {
            _execution.OnImpact -= Impact;
            _execution.OnExecuted -= Executed;
            _execution.OnAreaStarted -= AreaStarted;
            _execution.OnAreaEnded -= AreaEnded;
            _execution = null;
        }
        _requesting = false;
        _actionId = 0;
        _actualDirection = Vector3.zero;
        _actualTarget = null;
        _followAnchor = null;
        _hasArea = _hasDamage = _hasCast = _hasLaunch = _showCastRange = false;
        _overlapped = null;
        _hits = 0;
        _damage = 0f;
    }

    void BindPlayer(PlayerActor actor)
    {
        ReleaseInput();
        if (_combat != null)
        {
            _combat.OnActionStarted -= ActionStarted;
            _combat.OnExecutionCreated -= ExecutionCreated;
            _combat.OnActionEnded -= ActionEnded;
        }
        ClearObservation();
        _actor = actor;
        _combat = actor != null ? actor.Loadout.Combat : null;
        _reader = actor != null ? actor.GetComponentInChildren<PlayerInputReader>(true) : null;
        _controller = _reader != null ? _reader.Controller : null;
        if (_combat != null)
        {
            _combat.OnActionStarted += ActionStarted;
            _combat.OnExecutionCreated += ExecutionCreated;
            _combat.OnActionEnded += ActionEnded;
            if (!_hasLayout)
            {
                _origin = actor.CurrentPose.position;
                _yaw = actor.CurrentPose.rotation.eulerAngles.y;
                _hasLayout = true;
            }
        }
    }

    void BindDummy(CombatTarget target)
    {
        if (_dummy != null)
            _dummy.OnDamaged -= Damaged;
        ClearObservation();
        _dummy = target;
        if (_dummy != null)
            _dummy.OnDamaged += Damaged;
    }

    static string Id(Component item) => GlobalObjectId.GetGlobalObjectIdSlow(item).ToString();

    static string[] Labels<T>(T[] items) where T : Component
    {
        return new[] { "선택" }.Concat(items.Select(item => AnimationUtility.CalculateTransformPath(item.transform, null))).ToArray();
    }

    void RefreshActors()
    {
        if (!IsTestPlay)
            return;
        GameObject[] roots = SceneManager.GetSceneByPath(TestScene).GetRootGameObjects();
        _players = roots.SelectMany(root => root.GetComponentsInChildren<PlayerActor>(true)).Where(p => p.IsPrepared && p.isActiveAndEnabled).ToArray();
        _enemies = roots.SelectMany(root => root.GetComponentsInChildren<CombatTarget>(true)).Where(t => t.Team == CombatTeam.Enemy && t.isActiveAndEnabled).ToArray();
        _playerLabels = Labels(_players);
        _enemyLabels = Labels(_enemies);

        PlayerActor player = _players.FirstOrDefault(p => Id(p) == _playerId);
        if (player == null && string.IsNullOrEmpty(_playerId) && _players.Length == 1)
        {
            player = _players[0];
            _playerId = Id(player);
        }
        if (!ReferenceEquals(_actor, player))
            BindPlayer(player);
        CombatTarget dummy = _enemies.FirstOrDefault(t => Id(t) == _dummyId);
        if (!ReferenceEquals(_dummy, dummy))
        {
            // 쓰러진 대상이 씬에서 사라져도 마지막 타격 기록은 보존
            if (dummy == null && _hasCast)
            {
                if (_dummy != null) _dummy.OnDamaged -= Damaged;
                _dummy = null;
            }
            else BindDummy(dummy);
        }
    }

    void Tick()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying && !EditorApplication.isCompiling && SessionState.GetBool(RestartKey, false))
        {
            SessionState.EraseBool(RestartKey);
            StartTest();
            return;
        }
        if (!IsTestPlay)
        {
            if (_texture != null || _camera != null || _combat != null || _execution != null)
                ReleaseSession();
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        if (now >= _nextListRefresh)
        {
            _nextListRefresh = now + 1.0;
            RefreshActors();
        }
        if (_restoreLayout && _dummy != null && _controller != null && _controller.CanAcceptCommands)
        {
            _restoreLayout = false;
            if (PlacePlayer()) PlaceDummy(_dummyPosition);
            else _message = "이전 플레이어 배치를 복원할 수 없습니다.";
        }
        if (focusedWindow == this && _reader != null && _reader.isActiveAndEnabled && _controller != null && _controller.CanAcceptCommands)
        {
            _reader.enabled = false;
            _ownsInput = true;
        }
        if (now >= _nextRepaint)
        {
            _nextRepaint = now + 1.0 / 24;
            Repaint();
        }
    }

    void ReleaseInput()
    {
        if (_ownsInput && _reader != null && _controller != null && _controller.isActiveAndEnabled && _reader.HasSceneBindings)
            _reader.enabled = true;
        _ownsInput = false;
    }

    static bool Finite(float value) => !float.IsInfinity(value) && !float.IsNaN(value);
    static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

    int EquippedSlot()
    {
        if (_actor != null && _skill != null)
            for (int i = 0; i < _actor.Loadout.SlotCount; i++)
                if (_actor.Loadout.GetSkill(i)?.Definition == _skill)
                    return i;
        return -1;
    }

    string CastBlock()
    {
        if (_skill == null) return "관리 창에서 ‘프로젝트에서 선택’을 누르거나 스킬을 지정하세요.";
        if (_actor == null || _controller == null || _combat == null) return "준비된 플레이어를 선택하세요.";
        if (_dummy == null || !_dummy.IsAlive) return "살아 있는 실험 대상을 선택하세요.";
        if (!_controller.CanAcceptCommands || !_combat.CanMove || _combat.IsBusy) return "플레이어가 다음 요청을 받을 때까지 기다리세요.";
        if (!Finite(_origin) || !Finite(_yaw) || (_hasAim && !Finite(_aim))) return "배치와 조준값을 확인하세요.";
        if (_skill is MovementSkillDefinition)
        {
            if (_skill != _combat.MovementDefinition) return "이 플레이어의 고정 이동 스킬을 선택하세요.";
            return _combat.MovementSkillRemainingCooldown > 0f ? "이동 스킬 재사용 대기 중입니다." : null;
        }
        if (_combat.Owner.Status.Definition.IsBasicAttackSkill(_skill)) return null;
        int slot = EquippedSlot();
        if (slot < 0) return "테스트 씬의 스킬 패널에서 선택한 스킬을 Q/W/E/R 슬롯에 장착하세요.";
        if (!_actor.Loadout.GetSkill(slot).IsReady) return "스킬 재사용 대기 중입니다.";
        if (_skill is AreaDamageSkillDefinition area && area.PlacementMode == AreaPlacementMode.RequestedWorldPosition && !_hasAim)
            return "화면에서 지면을 클릭해 조준점을 지정하세요.";
        return null;
    }

    bool PlacePlayer()
    {
        if (_actor == null || _controller == null || !_controller.CanAcceptCommands ||
            _combat.IsBusy || !_combat.CanMove || !Finite(_origin.x) ||
            !Finite(_origin.z) || !Finite(_yaw))
            return false;

        const float probeHeight = 3f;
        const float probeDepth = 6f;

        // 입력한 X/Z에서 현재 플레이어 높이를 기준으로 바닥 탐색
        Vector3 rayOrigin = new Vector3(
            _origin.x,
            _actor.CurrentPose.position.y + probeHeight,
            _origin.z);

        int groundMask = LayerMask.GetMask("Ground");
        Physics.SyncTransforms();

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit groundHit,
            probeHeight + probeDepth, groundMask, QueryTriggerInteraction.Ignore))
            return false;

        Pose destination = new Pose(
            groundHit.point,
            Quaternion.Euler(0f, _yaw, 0f));

        _actor.SetPlacementBlocked(true);
        try
        {
            if (!_actor.TryPlace(destination))
                return false;

            // NavMesh 보정까지 완료된 실제 위치를 실험 창에 반영
            _origin = _actor.CurrentPose.position;
            _showCastRange = false;
            return true;
        }
        finally
        {
            _actor.SetPlacementBlocked(false);

            if (focusedWindow == this && _reader.isActiveAndEnabled &&
                _controller.CanAcceptCommands)
            {
                _reader.enabled = false;
                _ownsInput = true;
            }
        }
    }

    void PlaceDummy(Vector3 position)
    {
        if (!IsTestPlay || _dummy == null || !_dummy.IsAlive || !Finite(position))
            return;
        Transform body = _dummy.ActorTransform;
        if (body.TryGetComponent(out NavMeshAgent agent) && agent.isActiveAndEnabled)
        {
            if (!agent.isOnNavMesh || !agent.Warp(position))
            {
                _message = "더미를 이 위치에 배치할 수 없습니다.";
                return;
            }
            agent.ResetPath();
        }
        else body.position = position;
        Physics.SyncTransforms();
        _dummyPosition = body.position;
        ClearObservation();
        _message = "더미 배치 완료";
    }

    void Cast()
    {
        string blocked = CastBlock();
        if (!IsTestPlay || blocked != null)
        {
            _message = blocked;
            return;
        }
        if (!PlacePlayer())
        {
            _message = "입력 위치 근처의 NavMesh에 플레이어를 배치할 수 없습니다.";
            return;
        }
        ClearObservation();
        _hpAtCast = _dummy.Status.CurrentHealth;
        _hpAfterHit = _hpAtCast;
        _showCastRange = true;
        _requesting = true;
        try
        {
            bool accepted = _skill is MovementSkillDefinition ? _controller.RequestMovementSkill(Direction)
                : _combat.Owner.Status.Definition.IsBasicAttackSkill(_skill) ? _combat.TryAttack(_dummy)
                : _controller.RequestUseSkill(EquippedSlot(), Direction, _aim, _hasAim);
            _hasCast = accepted;
            if (!accepted) _message = "시전 요청 거절";
            else if (_execution == null) _message = "시전 중";
        }
        finally
        {
            _requesting = false;
        }
    }

    void ActionStarted(CombatActionInfo action)
    {
        if (!_requesting || action.Skill != _skill)
            return;
        _actionId = action.Id;
        _actualDirection = action.Direction;
        _actualTarget = action.Target;
    }

    void ExecutionCreated(SkillExecutionContext execution)
    {
        if (execution.Action.Id != _actionId || execution.Action.Skill != _skill || execution.SourceScene.path != TestScene)
            return;
        _execution = execution;
        // 즉시 피해로 더미가 사망하기 전의 범위 겹침 보관
        if (_skill is AreaDamageSkillDefinition area)
        {
            Vector3 predicted = area.PlacementMode == AreaPlacementMode.RequestedWorldPosition
                ? execution.Action.WorldPosition : execution.SourcePosition;
            _overlapped = DummyOverlaps(predicted, Quaternion.LookRotation(execution.Action.Direction, Vector3.up));
        }
        execution.OnExecuted += Executed;
        execution.OnAreaStarted += AreaStarted;
        execution.OnAreaEnded += AreaEnded;
        execution.OnImpact += Impact;
        _message = "발동";
    }

    void ActionEnded(long id, CombatActionResult result)
    {
        if (id == _actionId && result != CombatActionResult.Completed)
            _message = "시전 취소";
    }

    void AreaStarted(Vector3 origin, Quaternion rotation, bool hasRuntime, Transform anchor)
    {
        _areaOrigin = origin;
        _areaRotation = rotation;
        _hasArea = true;
        _followAnchor = anchor;
        _overlapped = _dummy != null && _dummy.IsAlive ? DummyOverlaps(origin, rotation) : _overlapped;
    }

    void AreaEnded()
    {
        if (_followAnchor != null)
            _areaOrigin = _followAnchor.position;
        _followAnchor = null;
    }

    void Executed(CombatActionInfo action, Vector3 position)
    {
        if (!(_skill is TargetDamageSkillDefinition projectile) || !projectile.UseProjectile) return;
        _hasLaunch = true;
        _launchOrigin = position;
        _launchDirection = action.Direction;
        if (projectile.FlightMode != ProjectileFlightMode.Straight || _dummy == null) return;
        // 발사 순간의 기하학적 예상 경로와 실제 물리 프레임의 피격 결과 비교
        float distance = projectile.ProjectileSpeed * projectile.ProjectileLifetime;
        _overlapped = Physics.OverlapSphere(position, projectile.ProjectileRadius, projectile.TargetMask, QueryTriggerInteraction.Collide)
            .Any(c => c.GetComponentInParent<CombatTarget>() == _dummy) ||
            Physics.SphereCastAll(position, projectile.ProjectileRadius, action.Direction, distance, projectile.TargetMask, QueryTriggerInteraction.Collide)
                .Any(hit => hit.collider.GetComponentInParent<CombatTarget>() == _dummy);
    }

    void Damaged(DamageRequest request, float applied)
    {
        if (_execution == null || request.Source.Id != _execution.Source.Id)
            return;
        // ReceiveDamage 직후 같은 호출 흐름에서 전달되는 OnImpact와 짝을 이루는 적용량
        _lastDamage = request;
        _lastApplied = applied;
        _hpAfterHit = _dummy.Status.CurrentHealth;
        _damageFrame = Time.frameCount;
        _hasDamage = true;
    }

    void Impact(SkillImpactInfo impact)
    {
        if (impact.ActionId != _actionId || impact.Target != _dummy)
            return;
        _hits++;
        _impactPosition = impact.Position;
        if (_hasDamage && _damageFrame == Time.frameCount && _lastDamage.Equals(impact.Damage))
            _damage += _lastApplied;
        _hasDamage = false;
        _message = "더미 적중";
    }

    AreaEffectDefinition Area => _skill is AreaDamageSkillDefinition area ? area.AreaEffect : (_skill as TargetDamageSkillDefinition)?.OnHitAreaEffect;

    bool? DummyOverlaps(Vector3 origin, Quaternion rotation)
    {
        AreaEffectDefinition area = Area;
        if (area == null || area.Hitbox == null || _dummy == null)
            return null;
        CombatHitbox hitbox = area.Hitbox;
        Vector3 center = origin + rotation * hitbox.Offset;
        Collider[] colliders = hitbox.HitShape == CombatHitShape.Box
            ? Physics.OverlapBox(center, hitbox.BoxSize * .5f, rotation, area.TargetMask, QueryTriggerInteraction.Collide)
            : Physics.OverlapSphere(center, hitbox.Radius, area.TargetMask, QueryTriggerInteraction.Collide);
        return colliders.Any(c => c.GetComponentInParent<CombatTarget>() == _dummy);
    }

    void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("스킬 실험", EditorStyles.boldLabel, GUILayout.Width(70));
            SelectSkill((SkillDefinition)EditorGUILayout.ObjectField(_skill, typeof(SkillDefinition), false));
            if (!EditorApplication.isPlaying)
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                    if (GUILayout.Button("테스트 씬 Play", EditorStyles.toolbarButton, GUILayout.Width(100))) StartTest();
            }
            else if (IsTestPlay && GUILayout.Button(new GUIContent("새 실험", "테스트 Play를 다시 시작하고 입력한 배치를 복원"), EditorStyles.toolbarButton, GUILayout.Width(66)))
                RestartTest();
        }
        if (!IsTestPlay)
        {
            EditorGUILayout.HelpBox(EditorApplication.isPlaying ? "현재 Play를 종료한 뒤 테스트 씬에서 시작하세요." : "스킬을 선택하고 테스트 씬 Play를 시작하세요.", MessageType.Info);
            return;
        }
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 86f;
        try { DrawInputs(); }
        finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
        Rect rect = GUILayoutUtility.GetRect(100, Mathf.Max(230f, position.height - 210f), GUILayout.ExpandWidth(true));
        DrawViewport(rect);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            string overlap = !_overlapped.HasValue ? "—" : _overlapped.Value ? "겹침" : "밖";
            string hp = _dummy != null ? _dummy.Status.CurrentHealth.ToString("0.##") : _hasCast ? _hpAfterHit.ToString("0.##") : "—";
            GUILayout.Label($"발동 시 예상 범위   {overlap}       실제 적중   {_hits}회       적용 피해   {_damage:0.##}       더미 HP   {(_hasCast ? _hpAtCast.ToString("0.##") + " → " : "")}{hp}", EditorStyles.boldLabel);
            GUILayout.Label(string.Join("  ·  ", new[] { _message, CastBlock() }.Where(s => !string.IsNullOrEmpty(s))), EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    void DrawInputs()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            int playerIndex = EditorGUILayout.Popup("플레이어", Array.IndexOf(_players, _actor) + 1, _playerLabels);
            if (playerIndex != Array.IndexOf(_players, _actor) + 1)
            {
                PlayerActor chosen = playerIndex == 0 ? null : _players[playerIndex - 1];
                _playerId = chosen != null ? Id(chosen) : "-";
                BindPlayer(chosen);
            }
            int targetIndex = EditorGUILayout.Popup("피격 더미", Array.IndexOf(_enemies, _dummy) + 1, _enemyLabels);
            if (targetIndex != Array.IndexOf(_enemies, _dummy) + 1)
            {
                CombatTarget chosen = targetIndex == 0 ? null : _enemies[targetIndex - 1];
                _dummyId = chosen != null ? Id(chosen) : "";
                BindDummy(chosen);
                if (chosen != null) _dummyPosition = chosen.Position;
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            _origin = EditorGUILayout.Vector3Field("플레이어 위치", _origin);
            _yaw = EditorGUILayout.FloatField("공격 방향 Y°", _yaw, GUILayout.Width(164));
            if (EditorGUI.EndChangeCheck()) _showCastRange = false;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _dummyPosition = EditorGUILayout.Vector3Field("더미 위치", _dummyPosition);
            using (new EditorGUI.DisabledScope(_dummy == null))
                if (GUILayout.Button("더미 배치", GUILayout.Width(85))) PlaceDummy(_dummyPosition);
            using (new EditorGUI.DisabledScope(_actor == null))
                if (GUILayout.Button("플레이어 배치", GUILayout.Width(100))) _message = PlacePlayer() ? "플레이어 배치 완료" : "배치할 수 없는 위치 또는 행동 상태입니다.";
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _placeDummy = GUILayout.Toolbar(_placeDummy ? 1 : 0, ClickModes, GUILayout.Width(205)) == 1;
            if (_actor != null && GUILayout.Button("장착 슬롯 ▾", GUILayout.Width(90)))
            {
                GenericMenu slots = new GenericMenu();
                for (int i = 0; i < _actor.Loadout.SlotCount; i++)
                {
                    SkillDefinition skill = _actor.Loadout.GetSkill(i)?.Definition;
                    GUIContent label = new GUIContent($"{"QWER"[i]}   {(skill != null ? skill.SkillName : "빈 슬롯")}");
                    if (skill != null) slots.AddItem(label, skill == _skill, () => SelectSkill(skill));
                    else slots.AddDisabledItem(label);
                }
                slots.ShowAsContext();
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("보기 배율", GUILayout.Width(55));
            _zoom = GUILayout.HorizontalSlider(_zoom, .45f, 2f, GUILayout.Width(85));
            using (new EditorGUI.DisabledScope(CastBlock() != null))
                if (GUILayout.Button("시전", GUILayout.Width(90))) Cast();
        }
    }

    void ReleaseTexture()
    {
        if (_texture == null) return;
        if (_camera != null) _camera.targetTexture = null;
        _texture.Release();
        DestroyImmediate(_texture);
    }

    void PrepareCamera(Rect rect)
    {
        if (_camera == null)
        {
            GameObject go = new GameObject("SkillTestWindow Camera", typeof(Camera));
            go.hideFlags = HideFlags.HideAndDontSave;
            _camera = go.GetComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(.09f, .11f, .14f);
            _camera.cullingMask &= ~(1 << 5);
        }
        int width = Mathf.Clamp(Mathf.RoundToInt(rect.width), 64, 1600);
        int height = Mathf.Max(32, Mathf.RoundToInt(width * rect.height / rect.width));
        if (_texture == null || _texture.width != width || _texture.height != height)
        {
            ReleaseTexture();
            _texture = new RenderTexture(width, height, 24) { name = "SkillTestWindow View", hideFlags = HideFlags.HideAndDontSave };
            _texture.Create();
            _camera.targetTexture = _texture;
        }
        Vector3 player = _actor != null ? _actor.CurrentPose.position : Finite(_origin) ? _origin : Vector3.zero;
        Vector3 dummy = _dummy != null ? _dummy.Position : player;
        Vector3 center = (player + dummy) * .5f;
        float span = Mathf.Max(6f, Vector3.Distance(player, dummy) * .72f + 2f) / _zoom;
        _camera.aspect = rect.width / rect.height;
        _camera.orthographicSize = span / Mathf.Min(1f, _camera.aspect);
        Quaternion angle = Quaternion.Euler(43f, 35f, 0f);
        float distance = span * 3f + 40f;
        _camera.transform.SetPositionAndRotation(center - angle * Vector3.forward * distance, angle);
        _camera.farClipPlane = distance * 2f;
    }

    void DrawViewport(Rect rect)
    {
        if (rect.width < 2 || rect.height < 2) return;
        Event evt = Event.current;
        if (evt.type == EventType.Repaint)
        {
            PrepareCamera(rect);
            _camera.Render();
            GUI.DrawTexture(rect, _texture);
        }
        if (_camera == null) return;
        if (rect.Contains(evt.mousePosition) && (evt.type == EventType.MouseMove || evt.type == EventType.MouseDown))
        {
            Vector2 uv = Rect.PointToNormalized(rect, evt.mousePosition);
            Ray ray = _camera.ViewportPointToRay(new Vector3(uv.x, 1f - uv.y));
            float y = Finite(_origin.y) ? _origin.y : 0f;
            Plane floor = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            if (_skill is AreaDamageSkillDefinition area && area.PlacementMode == AreaPlacementMode.RequestedWorldPosition
                && Physics.Raycast(ray, out RaycastHit ground, _camera.farClipPlane, area.GroundMask, QueryTriggerInteraction.Ignore))
            {
                _cursor = ground.point;
                _hasCursor = true;
            }
            else
            {
                _hasCursor = floor.Raycast(ray, out float depth);
                if (_hasCursor) _cursor = ray.GetPoint(depth);
            }
            if (_hasCursor && evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (_placeDummy) PlaceDummy(_cursor);
                else { _aim = _cursor; _hasAim = true; _showCastRange = false; _message = "조준점 고정"; }
                GUI.FocusControl(null);
                evt.Use();
            }
            Repaint();
        }
        if (!rect.Contains(evt.mousePosition)) _hasCursor = false;
        if (evt.type != EventType.Repaint) return;
        GUI.BeginGroup(rect);
        try { DrawMarkers(rect.size); }
        finally { GUI.EndGroup(); }
    }

    Vector2 Project(Vector3 world, Vector2 size)
    {
        Vector3 uv = _camera.WorldToViewportPoint(world);
        return new Vector2(uv.x * size.x, (1f - uv.y) * size.y);
    }

    void Line(Vector3 from, Vector3 to, Color color, Vector2 size)
    {
        using (new Handles.DrawingScope(color))
            Handles.DrawAAPolyLine(2f, Project(from, size), Project(to, size));
    }

    void Label(Vector3 point, string caption, Color color, Vector2 size)
    {
        Vector2 at = Project(point, size);
        if (at.x < 0 || at.x > size.x || at.y < 0 || at.y > size.y) return;
        _captionStyle ??= new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter };
        GUIContent content = new GUIContent(caption);
        float width = Mathf.Ceil(_captionStyle.CalcSize(content).x) + 10f;
        Rect label = new Rect(Mathf.Clamp(at.x + 7f, 2f, size.x - width - 2f), 0f, width, 19f);
        for (int i = 0; i <= _captions.Count * 2 + 2; i++)
        {
            int row = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
            label.y = Mathf.Clamp(at.y - 20f + row * 21f, 2f, size.y - 21f);
            if (!_captions.Any(label.Overlaps)) break;
        }
        _captions.Add(label);
        Vector2 edge = new Vector2(Mathf.Clamp(at.x, label.xMin, label.xMax), Mathf.Clamp(at.y, label.yMin, label.yMax));
        using (new Handles.DrawingScope(color)) Handles.DrawAAPolyLine(1.5f, at, edge);
        EditorGUI.DrawRect(label, new Color(.035f, .045f, .055f, .85f));
        _captionStyle.normal.textColor = color;
        GUI.Label(label, content, _captionStyle);
    }

    void Cross(Vector3 at, float radius, Color color, Vector2 size)
    {
        Line(at - Vector3.right * radius, at + Vector3.right * radius, color, size);
        Line(at - Vector3.forward * radius, at + Vector3.forward * radius, color, size);
    }

    void Circle(Vector3 at, Vector3 x, Vector3 y, Color color, Vector2 size)
    {
        Vector3 last = at + x;
        for (int i = 1; i <= 48; i++)
        {
            float angle = i * (Mathf.PI * 2f / 48f);
            Vector3 next = at + x * Mathf.Cos(angle) + y * Mathf.Sin(angle);
            Line(last, next, color, size);
            last = next;
        }
    }

    void DrawMarkers(Vector2 size)
    {
        _captions.Clear();
        if (!Finite(_origin) || !Finite(_yaw)) return;
        if (_actor != null) Label(_actor.Target.AimPosition, "플레이어", PlayerColor, size);
        Cross(_origin, .35f, PlayerColor, size);
        Line(_origin, _origin + Direction * 2, PlayerColor, size);
        Vector3 tip = _origin + Direction * 2;
        Vector3 side = Vector3.Cross(Vector3.up, Direction) * .18f;
        Line(tip, tip - Direction * .44f + side, PlayerColor, size);
        Line(tip, tip - Direction * .44f - side, PlayerColor, size);
        Label(_origin + Direction * 2, "입력 방향", PlayerColor, size);
        if (_showCastRange && _actualDirection != Vector3.zero)
        {
            Line(_origin, _origin + _actualDirection * 2.6f, Color.white, size);
            if (Vector3.Angle(Direction, _actualDirection) > 2f)
                Label(_origin + _actualDirection * 2.6f, "확정 방향", Color.white, size);
        }
        if (_dummy != null)
        {
            Vector3 point = _dummy.Position;
            Circle(point, Vector3.right * .6f, Vector3.forward * .6f, TargetColor, size);
            Label(_dummy.AimPosition, $"더미  HP {_dummy.Status.CurrentHealth:0.##}", TargetColor, size);
        }
        if (_hasCursor)
        {
            Cross(_cursor, .28f, Color.white, size);
            Label(_cursor + Vector3.down * .45f, $"마우스  {_cursor.x:0.0}, {_cursor.z:0.0}", Color.white, size);
        }
        if (_hasAim) { Cross(_aim, .55f, AimColor, size); Label(_aim, "지정점", AimColor, size); }
        if (_hits > 0)
        {
            Cross(_impactPosition, .4f, new Color(1f, .38f, .32f), size);
            Label(_impactPosition + Vector3.up * .8f, $"실제 적중 ×{_hits}", new Color(1f, .64f, .55f), size);
        }
        if (_skill == null) return;
        bool displayArea = _hasArea && _showCastRange;
        Quaternion rotation = displayArea ? _areaRotation : Quaternion.Euler(0, _yaw, 0);
        Vector3 origin = _followAnchor != null ? _followAnchor.position : _areaOrigin;
        AreaEffectDefinition area = Area;
        if (!displayArea)
        {
            if (_skill is AreaDamageSkillDefinition placement && placement.PlacementMode == AreaPlacementMode.RequestedWorldPosition && !_hasAim) return;
            if (_skill is TargetDamageSkillDefinition && _dummy == null) return;
            origin = _skill is TargetDamageSkillDefinition ? (_actualTarget != null ? _actualTarget.Position : _dummy.Position)
                : _skill is AreaDamageSkillDefinition ground && ground.PlacementMode == AreaPlacementMode.RequestedWorldPosition ? _aim : _origin;
        }
        if (_skill is TargetDamageSkillDefinition shot && shot.UseProjectile)
        {
            Vector3 start = _hasLaunch && _showCastRange ? _launchOrigin : _origin + rotation * shot.ProjectileSpawnOffset;
            Vector3 direction = _hasLaunch && _showCastRange ? _launchDirection : Direction;
            bool tracked = shot.FlightMode == ProjectileFlightMode.Homing && (_actualTarget != null || _dummy != null);
            Vector3 end = tracked ? (_actualTarget != null ? _actualTarget.AimPosition : _dummy.AimPosition) : start + direction * (shot.ProjectileSpeed * shot.ProjectileLifetime);
            Vector3 lateral = Vector3.Cross(Vector3.up, (end - start).normalized) * shot.ProjectileRadius;
            Line(start - lateral, end - lateral, RangeColor, size);
            Line(start + lateral, end + lateral, RangeColor, size);
            Circle(start, Vector3.right * shot.ProjectileRadius, Vector3.forward * shot.ProjectileRadius, RangeColor, size);
            Label(start, tracked ? "추적 대상 방향" : "예상 탄환 경로", RangeColor, size);
        }
        if (area != null && area.Hitbox != null)
        {
            CombatHitbox box = area.Hitbox;
            Vector3 center = origin + rotation * box.Offset;
            if (box.HitShape == CombatHitShape.Sphere)
            {
                Circle(center, Vector3.right * box.Radius, Vector3.forward * box.Radius, RangeColor, size);
                Circle(center, Vector3.up * box.Radius, Vector3.right * box.Radius, RangeColor, size);
                Circle(center, Vector3.up * box.Radius, Vector3.forward * box.Radius, RangeColor, size);
            }
            else
            {
                Vector3[] corners = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    corners[i] = center + rotation * Vector3.Scale(box.BoxSize * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                for (int i = 0; i < 8; i++)
                    for (int axis = 1; axis <= 4; axis *= 2)
                        if ((i & axis) == 0) Line(corners[i], corners[i | axis], RangeColor, size);
            }
            Label(center, "예상 공격 범위", RangeColor, size);
        }
        else if (_skill is TargetDamageSkillDefinition targeted && !targeted.UseProjectile && _dummy != null)
        {
            Vector3 target = _actualTarget != null ? _actualTarget.AimPosition : _dummy.AimPosition;
            Line(_origin, target, RangeColor, size);
            Label(target, "대상 지정", RangeColor, size);
        }
        else if (_skill is MovementSkillDefinition movement)
        {
            Vector3 end = _origin + Direction * movement.Distance;
            Line(_origin, end, RangeColor, size);
            Label(end, "예상 도착", RangeColor, size);
        }
        else if (_skill is DashStrikeSkillDefinition dashStrike)
        {
            // 조준점까지의 수평 거리를 최소·최대 돌진 거리로 제한
            float distance = dashStrike.Distance;

            if (_hasAim)
            {
                Vector3 offset = _aim - _origin;
                offset.y = 0f;
                distance = Mathf.Clamp(offset.magnitude, dashStrike.MinDistance, dashStrike.Distance);
            }

            Vector3 end = _origin + Direction * distance;
            Line(_origin, end, RangeColor, size);
            Label(end, "예상 도착 공격", RangeColor, size);
        }
    }
}
