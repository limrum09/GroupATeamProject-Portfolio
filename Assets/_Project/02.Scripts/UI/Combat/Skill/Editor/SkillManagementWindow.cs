using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 스킬 에셋의 검색, 편집, 생성 및 UID 검사 관리창
/// </summary>
public class SkillManagementWindow : EditorWindow
{
    enum SkillGroup
    {
        All,
        Player,
        Enemy,
        Other
    }

    const string DataFolder = SkillUIDValidator.SearchRoot + "/06.Data/Skill";
    const string EnemyFolder = DataFolder + "/Enemy";

    static readonly string[] GroupNames =
    {
        "전체",
        "플레이어",
        "Enemy",
        "기타"
    };

    static readonly string[] PlayerFolders =
    {
        DataFolder + "/KnightSkill",
        DataFolder + "/WizardSkill",
        DataFolder + "/PublicSkill",
        DataFolder + "/MoveSkill"
    };

    static readonly string[] CategoryNames =
    {
        "전체",
        "공용",
        "기사",
        "마법사"
    };

    [SerializeField] SkillDefinition _selected;
    [SerializeField] string _search = string.Empty;
    [SerializeField] SkillGroup _groupFilter;
    [SerializeField] int _categoryFilter;

    readonly List<SkillDefinition> _visibleSkills = new List<SkillDefinition>();
    List<SkillDefinition> _skills = new List<SkillDefinition>();
    List<SkillUIDValidator.Issue> _issues = new List<SkillUIDValidator.Issue>();

    Editor _editor;

    GUIStyle _panelStyle;
    GUIStyle _titleStyle;
    GUIStyle _rowStyle;
    GUIStyle _rowTitleStyle;
    GUIStyle _rowSubtitleStyle;
    GUIStyle _selectedTitleStyle;
    GUIStyle _selectedSubtitleStyle;
    bool _stylesUseDarkSkin;

    Vector2 _listScroll;
    Vector2 _detailScroll;
    Vector2 _issueScroll;

    bool _refreshRequested;
    bool _filterRequested;
    bool _hasValidation;
    bool _showIssues = true;

    string _message = "편집한 값은 즉시 반영됩니다. 선택 저장으로 에셋 파일을 저장하세요.";

    MessageType _messageType = MessageType.Info;

    // 에셋 편집을 허용하는 에디터 상태
    bool CanEdit => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

    // 편집 영역의 최소 폭을 남기는 목록 너비
    float ListWidth => Mathf.Clamp(position.width * 0.3f, 290f, 360f);

    [MenuItem("Tools/Skills/스킬 데이터 관리")]
    static void OpenFromMenu()
    {
        Open();
    }

    void OnEnable()
    {
        minSize = new Vector2(900f, 550f);

        EditorApplication.projectChanged += RequestRefresh;
        Undo.undoRedoPerformed += RequestRefresh;

        RequestRefresh();
    }

    void OnDisable()
    {
        EditorApplication.projectChanged -= RequestRefresh;
        Undo.undoRedoPerformed -= RequestRefresh;

        ReleaseEditor();
    }

    void OnFocus()
    {
        RequestRefresh();
    }

    void OnInspectorUpdate()
    {
        if (!CanEdit)
            return;

        if (_refreshRequested)
            RefreshSkills();
        else if (_filterRequested)
            ApplyFilter();

        Repaint();
    }

    void RequestRefresh()
    {
        _refreshRequested = true;
        InvalidateValidation();
    }

    void RequestFilter()
    {
        // 값 편집 시 전체 에셋 재검색 없이 표시 목록만 갱신
        _filterRequested = true;
        InvalidateValidation();
    }

    void InvalidateValidation()
    {
        _hasValidation = false;
        _issues.Clear();
    }

    void RefreshSkills()
    {
        _refreshRequested = false;
        _skills = SkillUIDValidator.FindDefinitions();

        InvalidateValidation();

        if (_selected == null || !_skills.Contains(_selected))
            SelectSkill(null);

        ApplyFilter();
    }

    void ApplyFilter()
    {
        _filterRequested = false;
        _visibleSkills.Clear();

        string query = (_search ?? string.Empty).Trim();

        foreach (SkillDefinition skill in _skills)
        {
            if (skill == null)
                continue;

            if (_groupFilter != SkillGroup.All && GetSkillGroup(skill) != _groupFilter)
                continue;

            if (_groupFilter != SkillGroup.Enemy && _categoryFilter > 0)
            {
                using (SerializedObject data = new SerializedObject(skill))
                {
                    int category = data.FindProperty("_category").intValue;

                    if (category != _categoryFilter - 1)
                        continue;
                }
            }

            if (query.Length > 0 && !Contains(skill.name, query) && !Contains(skill.SkillName, query) && !Contains(skill.SkillUID, query))
                continue;

            _visibleSkills.Add(skill);
        }

        Repaint();
    }

    static SkillGroup GetSkillGroup(SkillDefinition skill)
    {
        // 사용 자격이나 스킬 타입과 독립적인 관리창의 폴더 분류
        string path = AssetDatabase.GetAssetPath(skill);

        if (path.StartsWith(EnemyFolder + "/", StringComparison.Ordinal))
            return SkillGroup.Enemy;

        foreach (string folder in PlayerFolders)
        {
            if (path.StartsWith(folder + "/", StringComparison.Ordinal))
                return SkillGroup.Player;
        }

        return SkillGroup.Other;
    }

    static bool Contains(string value, string query)
    {
        return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsManagedSkill(SkillDefinition skill)
    {
        if (skill == null)
            return false;

        string path = AssetDatabase.GetAssetPath(skill);

        return path.StartsWith(SkillUIDValidator.SearchRoot + "/", StringComparison.Ordinal);
    }

    void SelectSkill(SkillDefinition skill)
    {
        ReleaseEditor();

        _selected = skill;
        _detailScroll = Vector2.zero;

        Repaint();
    }

    void RevealSkill(SkillDefinition skill)
    {
        // 오류 항목이나 Inspector에서 연 스킬을 해당 분류에 표시
        if (_groupFilter != SkillGroup.All)
            _groupFilter = GetSkillGroup(skill);

        _search = string.Empty;
        _categoryFilter = 0;
        _listScroll = Vector2.zero;

        ApplyFilter();
        SelectSkill(skill);

        EditorGUIUtility.PingObject(skill);
    }

    void ReleaseEditor()
    {
        if (_editor != null)
            DestroyImmediate(_editor);

        _editor = null;
    }

    void SetMessage(string message, MessageType type = MessageType.Info)
    {
        _message = message;
        _messageType = type;

        Repaint();
    }

    void EnsureStyles()
    {
        if (_panelStyle != null && _stylesUseDarkSkin == EditorGUIUtility.isProSkin)
            return;

        _stylesUseDarkSkin = EditorGUIUtility.isProSkin;

        _panelStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(12, 12, 10, 10),
            margin = new RectOffset(0, 0, 0, 0)
        };

        _titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
            fixedHeight = 0f,
            wordWrap = false,
            clipping = TextClipping.Clip
        };

        // 행 배경과 두 줄 텍스트를 분리해 기본 버튼의 높이 제한 제거
        _rowStyle = new GUIStyle(GUIStyle.none);
        _rowTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(0, 0, 0, 0),
            fixedHeight = 0f,
            wordWrap = false,
            clipping = TextClipping.Clip
        };

        _rowSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(0, 0, 0, 0),
            fixedHeight = 0f,
            wordWrap = false,
            clipping = TextClipping.Clip
        };

        _selectedTitleStyle = new GUIStyle(_rowTitleStyle);
        _selectedTitleStyle.normal.textColor = Color.white;

        _selectedSubtitleStyle = new GUIStyle(_rowSubtitleStyle);
        _selectedSubtitleStyle.normal.textColor = new Color(0.9f, 0.94f, 1f);
    }

    void OnGUI()
    {
        if (!CanEdit)
        {
            EditorGUILayout.HelpBox("Play 모드와 컴파일이 끝난 뒤 사용하세요.", MessageType.Info);

            return;
        }

        EnsureStyles();
        DrawToolbar();
        GUILayout.Space(8f);

        using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
        {
            GUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(_panelStyle, GUILayout.Width(ListWidth), GUILayout.ExpandHeight(true)))
            {
                DrawListToolbar();
                DrawSkillList();
            }

            GUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                DrawDetails();
                GUILayout.Space(6f);
                DrawIssues();
            }

            GUILayout.Space(8f);
        }

        GUILayout.Space(8f);
        DrawStatusBar();
    }

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("스킬 데이터 관리", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("전체 UID 검사", EditorStyles.toolbarButton, GUILayout.Width(100f)))
            {
                RunValidation();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                RefreshSkills();
                GUIUtility.ExitGUI();
            }
        }
    }

    void DrawListToolbar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("스킬 목록", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ 새 스킬", GUILayout.Width(80f), GUILayout.Height(24f)))
            {
                ShowCreateMenu();
                GUIUtility.ExitGUI();
            }
        }

        GUILayout.Space(8f);
        DrawGroupTabs();
        GUILayout.Space(8f);
        EditorGUI.BeginChangeCheck();

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("검색", GUILayout.Width(32f));
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(0f));
        }

        if (_groupFilter != SkillGroup.Enemy)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("직업", GUILayout.Width(32f));
                _categoryFilter = EditorGUILayout.Popup(_categoryFilter, CategoryNames);
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            _listScroll = Vector2.zero;
            ApplyFilter();
            GUIUtility.ExitGUI();
        }

        GUILayout.Space(6f);
        GUILayout.Label($"표시 {_visibleSkills.Count} / 전체 {_skills.Count}", EditorStyles.miniLabel);
        GUILayout.Space(4f);
    }

    void DrawGroupTabs()
    {
        SkillGroup selectedGroup = (SkillGroup)GUILayout.Toolbar((int)_groupFilter, GroupNames, GUILayout.Height(24f));

        if (selectedGroup == _groupFilter)
            return;

        _groupFilter = selectedGroup;
        _categoryFilter = 0;
        _listScroll = Vector2.zero;

        ApplyFilter();

        // 다른 분류의 스킬이 편집 영역에 남지 않도록 선택 해제
        if (_selected != null && !_visibleSkills.Contains(_selected))
            SelectSkill(null);

        GUI.FocusControl(null);
        GUIUtility.ExitGUI();
    }

    void DrawSkillList()
    {
        using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(_listScroll, GUILayout.ExpandHeight(true)))
        {
            _listScroll = scroll.scrollPosition;

            foreach (SkillDefinition skill in _visibleSkills)
            {
                if (skill == null)
                    continue;

                DrawSkillRow(skill);
            }

            if (_visibleSkills.Count == 0)
            {
                EditorGUILayout.HelpBox("검색 조건에 맞는 스킬이 없습니다.", MessageType.Info);
            }
        }
    }

    void DrawSkillRow(SkillDefinition skill)
    {
        float lineHeight = EditorGUIUtility.singleLineHeight;
        Rect row = EditorGUILayout.GetControlRect(false, lineHeight * 2f + 18f);
        bool isSelected = _selected == skill;
        bool isHovered = row.Contains(Event.current.mousePosition);

        Color background = _stylesUseDarkSkin
            ? new Color(0.22f, 0.22f, 0.22f)
            : new Color(0.89f, 0.89f, 0.89f);

        if (isSelected)
            background = new Color(0.2f, 0.42f, 0.68f);
        else if (isHovered)
            background = _stylesUseDarkSkin ? new Color(0.29f, 0.29f, 0.29f) : new Color(0.82f, 0.87f, 0.93f);

        EditorGUI.DrawRect(row, background);

        string displayName = string.IsNullOrWhiteSpace(skill.SkillName) ? skill.name : skill.SkillName;
        string dirty = EditorUtility.IsDirty(skill) ? " *" : string.Empty;
        string uid = string.IsNullOrWhiteSpace(skill.SkillUID) ? "UID 미지정" : $"UID: {skill.SkillUID}";
        string tooltip = $"{displayName}{dirty}\n{skill.name}\n{uid}\n{AssetDatabase.GetAssetPath(skill)}";

        bool selected = GUI.Toggle(row, isSelected, new GUIContent(string.Empty, tooltip), _rowStyle);

        DrawSkillIcon(new Rect(row.x + 8f, row.y + (row.height - 32f) * 0.5f, 32f, 32f), skill);

        Rect title = new Rect(row.x + 50f, row.y + 6f, Mathf.Max(0f, row.width - 58f), lineHeight);
        Rect subtitle = new Rect(title.x, title.yMax + 2f, title.width, lineHeight);

        GUI.Label(title, new GUIContent(displayName + dirty, tooltip), isSelected ? _selectedTitleStyle : _rowTitleStyle);
        GUI.Label(subtitle, new GUIContent(uid, tooltip), isSelected ? _selectedSubtitleStyle : _rowSubtitleStyle);

        GUILayout.Space(2f);

        if (selected && !isSelected)
        {
            GUI.FocusControl(null);
            SelectSkill(skill);
            GUIUtility.ExitGUI();
        }
    }

    void DrawSkillIcon(Rect rect, SkillDefinition skill)
    {
        UnityEngine.Object source = skill.SkillIcon != null ? (UnityEngine.Object)skill.SkillIcon : skill;
        Texture icon = AssetPreview.GetMiniThumbnail(source);

        if (icon != null)
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
    }

    void DrawDetails()
    {
        if (!IsManagedSkill(_selected))
        {
            using (new EditorGUILayout.VerticalScope(_panelStyle, GUILayout.ExpandHeight(true)))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("편집할 스킬을 선택하세요", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            }

            return;
        }

        DrawSelectedHeader();
        GUILayout.Space(8f);

        using (new EditorGUILayout.VerticalScope(_panelStyle, GUILayout.ExpandHeight(true)))
        {
            GUILayout.Label("스킬 설정", EditorStyles.boldLabel);
            GUILayout.Space(6f);

            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(_detailScroll, GUILayout.ExpandHeight(true)))
            {
                _detailScroll = scroll.scrollPosition;
                Editor.CreateCachedEditor(_selected, null, ref _editor);

                if (_editor == null)
                {
                    EditorGUILayout.HelpBox("선택한 스킬의 Inspector를 생성하지 못했습니다.", MessageType.Error);
                    return;
                }

                if (_editor is SkillDefinitionEditor skillEditor)
                    skillEditor.IsEmbedded = true;

                float previousLabelWidth = EditorGUIUtility.labelWidth;
                float previousFieldWidth = EditorGUIUtility.fieldWidth;
                bool previousWideMode = EditorGUIUtility.wideMode;

                try
                {
                    EditorGUIUtility.wideMode = true;
                    EditorGUIUtility.labelWidth = Mathf.Clamp((position.width - ListWidth - 72f) * 0.44f, 200f, 270f);
                    EditorGUIUtility.fieldWidth = 60f;

                    EditorGUI.BeginChangeCheck();
                    _editor.OnInspectorGUI();

                    if (EditorGUI.EndChangeCheck())
                        RequestFilter();
                }
                finally
                {
                    // 관리창의 필드 너비가 다른 Inspector에 남지 않도록 복원
                    EditorGUIUtility.labelWidth = previousLabelWidth;
                    EditorGUIUtility.fieldWidth = previousFieldWidth;
                    EditorGUIUtility.wideMode = previousWideMode;
                }
            }
        }
    }

    void DrawSelectedHeader()
    {
        using (new EditorGUILayout.VerticalScope(_panelStyle))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect icon = GUILayoutUtility.GetRect(40f, 40f, GUILayout.Width(40f), GUILayout.Height(40f));
                DrawSkillIcon(icon, _selected);
                GUILayout.Space(8f);

                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true)))
                {
                    string displayName = string.IsNullOrWhiteSpace(_selected.SkillName) ? _selected.name : _selected.SkillName;
                    GUILayout.Label(new GUIContent(displayName, displayName), _titleStyle, GUILayout.MinWidth(0f), GUILayout.Height(25f));
                    GUILayout.Label(EditorUtility.IsDirty(_selected) ? "저장하지 않은 변경 사항 있음" : "선택한 스킬", EditorStyles.miniLabel);
                }

                if (GUILayout.Button("복제", GUILayout.Width(64f), GUILayout.Height(28f)))
                {
                    CreateSkill(_selected.GetType(), _selected);
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("선택 저장", GUILayout.Width(88f), GUILayout.Height(28f)))
                {
                    SaveSelected();
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.Space(6f);
            EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(_selected), EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            DrawSelectionActions();
        }
    }

    void DrawSelectionActions()
    {
        ParticleSystem effect = _selected.UseEffectPrefab;

        BakedSpellPlayer player = effect != null ? effect.GetComponent<BakedSpellPlayer>() : null;

        bool canPreview = player != null && player.Definition != null;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("프로젝트에서 선택", GUILayout.Width(130f), GUILayout.Height(22f)))
            {
                Selection.activeObject = _selected;
                EditorGUIUtility.PingObject(_selected);

                GUIUtility.ExitGUI();
            }

            using (new EditorGUI.DisabledScope(!canPreview))
            {
                GUIContent previewLabel = new GUIContent("이펙트 미리보기", canPreview ? "연결된 발동 이펙트 재생" : "BakedSpellPlayer와 재생 데이터가 연결된 발동 이펙트 필요");

                if (GUILayout.Button(previewLabel, GUILayout.Width(120f), GUILayout.Height(22f)))
                {
                    FantasySpellPreviewWindow.Open(effect.gameObject);
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.FlexibleSpace();

            if (!canPreview)
                GUILayout.Label("미리보기 미지원", EditorStyles.miniLabel);
        }
    }

    void RunValidation()
    {
        // 분류 탭, 검색, 직업 필터에 관계없이 전체 관리 대상 검사
        RefreshSkills();

        _issues = SkillUIDValidator.Validate(_skills);
        _hasValidation = true;
        _showIssues = true;
        _issueScroll = Vector2.zero;
    }

    void DrawIssues()
    {
        if (!_hasValidation)
        {
            EditorGUILayout.LabelField("UID 검사 전 · 상단의 전체 UID 검사로 확인", EditorStyles.miniLabel);

            return;
        }

        if (_issues.Count == 0)
        {
            EditorGUILayout.LabelField($"UID 검사 완료 · {_skills.Count}개 / 검사 시점 오류 없음", EditorStyles.miniLabel);

            return;
        }

        using (new EditorGUILayout.VerticalScope(_panelStyle))
        {
            _showIssues = EditorGUILayout.Foldout(_showIssues, $"UID 검사 · {_skills.Count}개 / 오류 {_issues.Count}건", true);

            if (!_showIssues)
                return;

            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(_issueScroll, GUILayout.Height(Mathf.Clamp(position.height * 0.2f, 90f, 150f))))
            {
                _issueScroll = scroll.scrollPosition;

                foreach (SkillUIDValidator.Issue issue in _issues)
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUI.DisabledScope(!IsManagedSkill(issue.Definition)))
                        {
                            if (GUILayout.Button("선택", GUILayout.Width(45f)))
                            {
                                RevealSkill(issue.Definition);
                                GUIUtility.ExitGUI();
                            }
                        }

                        EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedLabel);
                    }
                }
            }
        }
    }

    void DrawStatusBar()
    {
        if (_messageType == MessageType.Error || _messageType == MessageType.Warning)
        {
            EditorGUILayout.HelpBox(_message, _messageType);
            return;
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(new GUIContent(_message, _message), EditorStyles.miniLabel, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
        }
    }

    void ShowCreateMenu()
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent("대상 공격"), false, () => CreateSkill(typeof(TargetDamageSkillDefinition), null));
        menu.AddItem(new GUIContent("범위 공격"), false, () => CreateSkill(typeof(AreaDamageSkillDefinition), null));
        menu.AddItem(new GUIContent("이동"), false, () => CreateSkill(typeof(MovementSkillDefinition), null));
        menu.AddItem(new GUIContent("돌진 공격"), false, () => CreateSkill(typeof(DashStrikeSkillDefinition), null));
        menu.AddItem(new GUIContent("적 돌진"), false, () => CreateSkill(typeof(ChargeAttackSkillDefinition), null));
        menu.ShowAsContext();
    }

    void CreateSkill(Type type, SkillDefinition template)
    {
        if (!CanEdit)
            return;

        string defaultFolder = _groupFilter == SkillGroup.Enemy ? EnemyFolder : DataFolder;

        if (!AssetDatabase.IsValidFolder(defaultFolder))
        {
            SetMessage($"스킬 저장 폴더가 필요합니다: {defaultFolder}", MessageType.Error);

            return;
        }

        string defaultName = template != null? template.name + "_Copy" : type.Name;
        string path = EditorUtility.SaveFilePanelInProject("스킬 에셋 생성", defaultName, "asset", $"{DataFolder} 아래에 저장하세요.", defaultFolder);

        if (string.IsNullOrEmpty(path))
            return;

        path = path.Replace('\\', '/');

        if (!path.StartsWith(DataFolder + "/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
        {
            SetMessage($"{DataFolder} 아래의 .asset 파일로 저장하세요.", MessageType.Error);

            return;
        }

        // 기존 파일을 덮어쓰지 않는 새 에셋 경로
        path = AssetDatabase.GenerateUniqueAssetPath(path);

        SkillDefinition created = null;

        try
        {
            created = template != null ? Instantiate(template): (SkillDefinition)ScriptableObject.CreateInstance(type);

            created.name = Path.GetFileNameWithoutExtension(path);

            // 복제 원본의 UID 재사용 방지
            using (SerializedObject data = new SerializedObject(created))
            {
                data.FindProperty("_skillUID").stringValue = string.Empty;
                data.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.CreateAsset(created, path);

            if (!AssetDatabase.Contains(created))
                throw new IOException($"스킬 에셋 파일을 생성하지 못했습니다: {path}");

            AssetDatabase.SaveAssetIfDirty(created);

            if (EditorUtility.IsDirty(created))
                throw new IOException($"생성한 스킬의 변경 사항이 저장되지 않았습니다: {path}");

            RefreshSkills();
            RevealSkill(created);

            SetMessage($"{path} 생성 완료. 새 UID를 입력하고 전체 UID 검사를 실행하세요.");
        }
        catch (ExitGUIException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);

            SetMessage($"스킬 생성 처리 실패: {path}\n{exception.Message}", MessageType.Error);

            RequestRefresh();
        }
        finally
        {
            // 저장에 실패한 임시 객체만 정리, 생성된 에셋 파일은 유지
            if (created != null && !AssetDatabase.Contains(created))
                DestroyImmediate(created);
        }
    }

    void SaveSelected()
    {
        if (!CanEdit || !IsManagedSkill(_selected))
            return;

        string path = AssetDatabase.GetAssetPath(_selected);

        try
        {
            AssetDatabase.SaveAssetIfDirty(_selected);

            if (EditorUtility.IsDirty(_selected))
                throw new IOException("변경 사항이 남아 있습니다. 파일 쓰기 권한과 Console을 확인하세요.");

            SetMessage($"{path} 저장 완료");
        }
        catch (ExitGUIException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);

            SetMessage($"스킬 저장 실패: {path}\n{exception.Message}", MessageType.Error);
        }
    }

    /// <summary>
    /// 스킬 관리창 열기와 지정 스킬 표시
    /// </summary>
    /// <param name="skill">바로 표시할 스킬, 미지정 시 기존 선택 유지</param>
    public static void Open(SkillDefinition skill = null)
    {
        if (skill != null && !IsManagedSkill(skill))
        {
            EditorUtility.DisplayDialog("관리 대상 확인", "Assets/_Project 아래에 저장된 스킬 에셋을 선택하세요.", "확인");

            return;
        }

        SkillManagementWindow window = GetWindow<SkillManagementWindow>("스킬 데이터 관리");

        window.RequestRefresh();

        if (skill != null)
            window.RevealSkill(skill);

        window.Show();
    }
}
