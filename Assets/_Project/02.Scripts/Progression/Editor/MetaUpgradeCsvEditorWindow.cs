using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 메타 강화 정의 CSV 편집과 런타임 JSON 내보내기
/// </summary>
public class MetaUpgradeCsvEditorWindow : EditorWindow
{
    // 스크립트 재컴파일 전후의 편집 내용과 외부 변경 비교 기준
    [SerializeField] string _loadedCsv;
    [SerializeField] string _draftCsv;

    const string CsvPath = "Assets/_Project/06.Data/Progression/CSV/Meta/GroupATeam_MetaUpgrades.csv";
    const string JsonFolder = "Assets/_Project/06.Data/Progression/JSON/Meta/";

    const string JsonPath = JsonFolder + "GroupATeam_MetaUpgrades.json";
    const string RankPath = JsonFolder + "GroupATeam_MetaUpgradeRanks.json";
    const string EffectPath = JsonFolder + "GroupATeam_MetaUpgradeEffects.json";
    const string IconCatalogPath = "Assets/_Project/06.Data/Progression/MetaUpgradeIconCatalog.asset";

    const string LevelColumn = "RequiredIntegratedLevel";
    const string AvailableIconColumn = "AvailableIconRef";
    const string EquippedIconColumn = "EquippedIconRef";
    const string UnavailableIconColumn = "UnavailableIconRef";

    const float CellHeight = 45f;
    const float CellGap = 4f;
    const float RowHeight = CellHeight + CellGap;
    const float HeaderHeight = 24f;

    float[] _columnX = Array.Empty<float>();

    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    CsvTable _table;
    Vector2 _scroll;
    int _activeCell = -1;
    int _pendingFocusCell = -1;
    string _message;
    bool _hasError;

    static float GetColumnWidth(string header)
    {
        return header switch
        {
            "UpgradeId" => 150f,
            "DisplayName" => 115f,
            "Description" => 205f,
            "Category" => 80f,
            "TargetCharacterId" => 110f,
            "MaxRank" => 68f,
            "SortOrder" => 72f,
            "Enabled" => 65f,
            LevelColumn => 92f,
            AvailableIconColumn => 70f,
            EquippedIconColumn => 70f,
            UnavailableIconColumn => 70f,
            _ => 110f
        };
    }

    [MenuItem("Tools/Data/메타 강화 편집기")]
    static void OpenWindow()
    {
        MetaUpgradeCsvEditorWindow window = GetWindow<MetaUpgradeCsvEditorWindow>("메타 강화 편집기");

        window.minSize = new Vector2(650f, 300f);
        window.Show();
    }

    void OnEnable()
    {
        saveChangesMessage = "수정한 메타 강화 CSV를 저장할까요?";

        Run(() =>
        {
            if (!string.IsNullOrEmpty(_draftCsv))
            {
                _table = CsvTable.Parse(_draftCsv, CsvPath);
                AddMissingColumn(_table, EquippedIconColumn, "", UnavailableIconColumn);
                hasUnsavedChanges = true;
            }
            else
            {
                LoadFromDisk();
            }
        });
    }

    void OnDisable()
    {
        _draftCsv = hasUnsavedChanges && _table != null ? _table.ToCsv() : null;
    }

    void OnProjectChange()
    {
        _spriteCache.Clear();
        Repaint();
    }

    void OnGUI()
    {
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            _activeCell = -1;
            _pendingFocusCell = -1;
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("다시 읽기", EditorStyles.toolbarButton))
            {
                if (!hasUnsavedChanges || EditorUtility.DisplayDialog("다시 읽기", "편집 중인 값을 버리고 CSV를 다시 읽을까요?", "다시 읽기", "취소"))
                {
                    GUI.FocusControl(null);
                    Run(LoadFromDisk);
                }
            }

            using (new EditorGUI.DisabledScope(_table == null))
            {
                if (GUILayout.Button("CSV 저장", EditorStyles.toolbarButton))
                    Run(SaveChanges);

                if (GUILayout.Button("JSON 내보내기", EditorStyles.toolbarButton))
                    Run(ExportJson);
            }

            GUILayout.FlexibleSpace();
        }

        if (!string.IsNullOrEmpty(_message))
            EditorGUILayout.HelpBox(_message, _hasError ? MessageType.Error : MessageType.Info);

        if (_table == null) return;

        Rect viewport = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        DrawGrid(viewport);
    }

    void Run(Action action)
    {
        try
        {
            _hasError = false;
            _message = null;
            action();
        }
        catch (Exception error)
        {
            _hasError = true;
            _message = error.Message;
        }
    }

    void LoadFromDisk()
    {
        string source = File.ReadAllText(CsvPath, Utf8);
        CsvTable table = CsvTable.Parse(source, CsvPath);

        if (!table.Headers.Contains("UpgradeId"))
            throw new FormatException("메타 강화 CSV에 UpgradeId 헤더 필요");

        bool added = AddMissingColumn(table, LevelColumn, "1");
        added |= AddMissingColumn(table, AvailableIconColumn, "");
        added |= AddMissingColumn(table, EquippedIconColumn, "", UnavailableIconColumn);
        added |= AddMissingColumn(table, UnavailableIconColumn, "");

        _table = table;
        _loadedCsv = source;
        _draftCsv = null;
        _spriteCache.Clear();
        _scroll = Vector2.zero;
        hasUnsavedChanges = added;

        _message = added? "새 열을 준비했습니다. CSV 저장을 누르면 원본에 반영됩니다." : $"{table.Rows.Count}개 강화 불러옴";
    }

    static bool AddMissingColumn(CsvTable table, string header, string initialValue, string beforeHeader = null)
    {
        if (table.Headers.Contains(header)) return false;

        int index = beforeHeader == null ? -1 : table.Headers.IndexOf(beforeHeader);

        if (index < 0)
            index = table.Headers.Count;

        table.Headers.Insert(index, header);

        foreach (List<string> row in table.Rows)
            row.Insert(index, initialValue);

        return true;
    }

    static bool IsIconColumn(string header)
    {
        return header == AvailableIconColumn || header == EquippedIconColumn || header == UnavailableIconColumn;
    }

    static string HeaderLabel(string header)
    {
        switch (header)
        {
            case "UpgradeId": 
                return "강화 ID";
            case "DisplayName": 
                return "표시 이름";
            case "Description": 
                return "설명";
            case LevelColumn: 
                return "요구 통합 레벨";
            case AvailableIconColumn:
                return "기본";
            case EquippedIconColumn:
                return "장착중";
            case UnavailableIconColumn:
                return "강화불가";
            default: 
                return header;
        }
    }

    void DrawGrid(Rect viewport)
    {
        int columns = _table.Headers.Count;
        int rowCount = _table.Rows.Count;

        if (_columnX.Length != columns + 1)
            _columnX = new float[columns + 1];

        _columnX[0] = 0f;

        for (int col = 0; col < columns; col++)
            _columnX[col + 1] = _columnX[col] + GetColumnWidth(_table.Headers[col]);

        Rect content = new Rect(0f, 0f, _columnX[columns], HeaderHeight + rowCount * RowHeight);

        _scroll = GUI.BeginScrollView(viewport, _scroll, content);

        int focusIndex = ReadNavigation(columns, rowCount);

        for (int col = 0; col < columns; col++)
        {
            float width = _columnX[col + 1] - _columnX[col];
            Rect rect = new Rect(_columnX[col], 0f, width - CellGap, HeaderHeight);
            GUI.Label(rect, HeaderLabel(_table.Headers[col]), EditorStyles.boldLabel);
        }

        for (int row = 0; row < rowCount; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                float width = _columnX[col + 1] - _columnX[col];
                float y = HeaderHeight + row * RowHeight + CellGap * 0.5f;

                Rect rect = new Rect(_columnX[col], y, width - CellGap, CellHeight);

                DrawCell(rect, row, col, columns);
            }
        }

        if (focusIndex >= 0)
        {
            _activeCell = focusIndex;
            _pendingFocusCell = focusIndex;
            Repaint();
        }

        // 모든 셀을 그린 Repaint에서 대상 컨트롤에 포커스 적용
        int target = -1;

        if (Event.current.type == EventType.Repaint && _pendingFocusCell >= 0)
        {
            target = _pendingFocusCell;
            _pendingFocusCell = -1;

            EditorGUIUtility.editingTextField = false;
            GUI.FocusControl("cell" + target);
        }

        GUI.EndScrollView();

        if (target >= 0)
        {
            int col = target % columns;
            int row = target / columns;
            float left = _columnX[col];
            float right = _columnX[col + 1];
            float top = HeaderHeight + row * RowHeight;

            _scroll.x = Mathf.Clamp(_scroll.x, Mathf.Max(0f, right - viewport.width + 18f), left);
            _scroll.y = Mathf.Clamp(_scroll.y, Mathf.Max(0f, top + RowHeight - viewport.height + 18f), top);

            Repaint();
        }
    }

    int ReadNavigation(int columns, int rows)
    {
        Event input = Event.current;

        if (input.type != EventType.KeyDown || rows == 0) 
            return -1;

        string name = GUI.GetNameOfFocusedControl();

        int current = _activeCell;

        if (current < 0 || current >= rows * columns)
            return -1;

        bool enter = input.keyCode == KeyCode.Return || input.keyCode == KeyCode.KeypadEnter;
        int next;

        if (input.keyCode == KeyCode.Tab)
        {
            next = Mathf.Clamp(current + (input.shift ? -1 : 1), 0, rows * columns - 1);
        }
        else if (enter)
        {
            if (input.alt && _table.Headers[current % columns] == "Description")
            {
                // 설명 TextArea에서 현재 커서 위치의 일반 줄바꿈으로 처리
                input.alt = false;
                input.character = '\n';
                return -1;
            }

            int lastInColumn = (rows - 1) * columns + current % columns;
            next = Mathf.Min(current + columns, lastInColumn);
        }
        else
        {
            return -1;
        }

        input.Use();
        return next;
    }

    void DrawCell(Rect rect, int row, int col, int columns)
    {
        string header = _table.Headers[col];
        string previous = _table.Rows[row][col];
        string value;

        int cellIndex = row * columns + col;

        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            _activeCell = cellIndex;

        GUI.SetNextControlName("cell" + cellIndex);

        if (IsIconColumn(header))
        {
            Sprite original = ResolveSprite(previous);
            bool missingSprite = !string.IsNullOrEmpty(previous) && original == null;

            float side = rect.height;
            Rect fieldRect = new Rect(rect.x, rect.y, side, side);
            Rect clearRect = new Rect(fieldRect.xMax + 2f, rect.center.y - 9f, 20f, 18f);

            Sprite selected = (Sprite)EditorGUI.ObjectField(fieldRect, original, typeof(Sprite), false);

            if (missingSprite && GUI.Button(clearRect, new GUIContent("×", "깨진 아이콘 참조 지우기"), EditorStyles.miniButton))
            {
                value = string.Empty;
            }
            else if (selected != original)
            {
                value = selected == null ? string.Empty : GlobalObjectId.GetGlobalObjectIdSlow(selected).ToString();
            }
            else
            {
                return;
            }
        }
        else if (header == "Description")
        {
            value = EditorGUI.TextArea(rect, previous);
        }
        else
        {
            value = EditorGUI.TextField(rect, previous);
        }

        if (value == previous) return;

        _table.Rows[row][col] = value;
        hasUnsavedChanges = true;
    }

    Sprite ResolveSprite(string reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;

        if (_spriteCache.TryGetValue(reference, out Sprite cached))
            return cached;

        Sprite sprite = null;

        if (GlobalObjectId.TryParse(reference, out GlobalObjectId id))
            sprite = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Sprite;

        _spriteCache[reference] = sprite;
        return sprite;
    }

    void ValidateRows(bool exporting)
    {
        int idColumn = _table.Headers.IndexOf("UpgradeId");
        int levelColumn = _table.Headers.IndexOf(LevelColumn);
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < _table.Rows.Count; i++)
        {
            List<string> row = _table.Rows[i];
            string id = row[idColumn];
            string label = $"데이터 {i + 1}행";

            if (string.IsNullOrWhiteSpace(id))
                throw new FormatException($"{label}: 강화 ID 필요");

            foreach (char ch in id)
            {
                if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                    throw new FormatException($"{label}: 강화 ID에 공백이나 제어문자 사용 불가");
            }

            if (!ids.Add(id))
                throw new FormatException($"{label}: 중복 강화 ID '{id}'");

            if (!int.TryParse(row[levelColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level < 1)
                throw new FormatException($"{label} ({id}): 요구 통합 레벨은 1 이상의 정수");

            if (!exporting) continue;

            for (int col = 0; col < _table.Headers.Count; col++)
            {
                string header = _table.Headers[col];

                if (IsIconColumn(header) && row[col].Length > 0 && ResolveSprite(row[col]) == null)
                    throw new FormatException($"{label} ({id}): {HeaderLabel(header)} 참조를 복원할 수 없습니다. Sprite를 다시 선택하세요.");
            }
        }
    }

    void CheckExternalChanges()
    {
        string current = File.ReadAllText(CsvPath, Utf8);

        if (!string.Equals(current, _loadedCsv, StringComparison.Ordinal))
            throw new IOException("창을 연 뒤 원본 CSV가 변경됐습니다. 필요한 편집값을 확인한 뒤 다시 읽기를 눌러 주세요.");
    }

    static void WriteText(string path, string text)
    {
        // 같은 디렉터리에서 완성된 파일로 교체하고 기존 Unity .meta 유지
        string tempPath = path + "." + Guid.NewGuid().ToString("N") + "~";

        try
        {
            File.WriteAllText(tempPath, text, Utf8);

            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    void SaveCsv(string csv)
    {
        CheckExternalChanges();

        if (!string.Equals(csv, _loadedCsv, StringComparison.Ordinal))
        {
            WriteText(CsvPath, csv);
            _loadedCsv = csv;
            AssetDatabase.ImportAsset(CsvPath);
        }

        _draftCsv = null;
        base.SaveChanges();
    }

    MetaUpgradeIconEntry[] BuildIconEntries(IReadOnlyDictionary<string, MetaUpgradeData> definitions)
    {
        int idColumn = _table.Headers.IndexOf("UpgradeId");
        int availableColumn = _table.Headers.IndexOf(AvailableIconColumn);
        int equippedColumn = _table.Headers.IndexOf(EquippedIconColumn);
        int unavailableColumn = _table.Headers.IndexOf(UnavailableIconColumn);

        MetaUpgradeIconEntry[] entries = new MetaUpgradeIconEntry[_table.Rows.Count];

        for (int i = 0; i < _table.Rows.Count; i++)
        {
            List<string> row = _table.Rows[i];
            string id = row[idColumn];

            Sprite available = ResolveSprite(row[availableColumn]);
            Sprite equipped = ResolveSprite(row[equippedColumn]);
            Sprite unavailable = ResolveSprite(row[unavailableColumn]);

            if (definitions[id].Enabled)
            {
                if (available == null)
                    throw new FormatException($"{id}: 해금 아이콘을 지정하세요.");

                if (equipped == null)
                    throw new FormatException($"{id}: 장착 아이콘을 지정하세요.");

                if (unavailable == null)
                    throw new FormatException($"{id}: 잠금 아이콘을 지정하세요.");
            }

            entries[i] = new MetaUpgradeIconEntry(id, available, equipped, unavailable);
        }

        return entries;
    }

    void SaveRuntimeAssets(string json, MetaUpgradeIconEntry[] entries)
    {
        bool create = !File.Exists(IconCatalogPath);
        MetaUpgradeIconCatalog catalog = create ? ScriptableObject.CreateInstance<MetaUpgradeIconCatalog>() : AssetDatabase.LoadAssetAtPath<MetaUpgradeIconCatalog>(IconCatalogPath);

        if (catalog == null)
            throw new IOException($"아이콘 카탈로그 파일을 열 수 없습니다: {IconCatalogPath}");

        MetaUpgradeIconEntry[] previous = catalog.ReplaceEntries(entries);

        try
        {
            if (create)
            {
                AssetDatabase.CreateAsset(catalog, IconCatalogPath);

                if (!AssetDatabase.Contains(catalog))
                    throw new IOException($"아이콘 카탈로그 생성 실패: {IconCatalogPath}");
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);

            // 기존 WriteText는 완성된 임시 파일로 JSON을 교체
            WriteText(JsonPath, json);
        }
        catch
        {
            // JSON과 아이콘 카탈로그가 서로 다른 내보내기 버전으로 남는 상황 방지
            if (create)
            {
                if (AssetDatabase.Contains(catalog))
                    AssetDatabase.DeleteAsset(IconCatalogPath);
                else
                    DestroyImmediate(catalog);
            }
            else
            {
                catalog.ReplaceEntries(previous);
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }

            throw;
        }

        AssetDatabase.ImportAsset(JsonPath);
    }

    void ExportJson()
    {
        ValidateRows(true);

        string csv = _table.ToCsv();
        string json = CsvToJsonConverter.Convert(csv, CsvPath);
        MetaUpgradeIconEntry[] entries;

        TextAsset preview = new TextAsset(json);
        preview.name = "메타 강화 내보내기 검사";

        try
        {
            TextAsset ranks = AssetDatabase.LoadAssetAtPath<TextAsset>(RankPath);
            TextAsset effects = AssetDatabase.LoadAssetAtPath<TextAsset>(EffectPath);

            IReadOnlyDictionary<string, MetaUpgradeData> definitions = MetaUpgradeJsonLoader.Load(preview, ranks, effects);

            entries = BuildIconEntries(definitions);
        }
        finally
        {
            DestroyImmediate(preview);
        }

        SaveCsv(csv);
        SaveRuntimeAssets(json, entries);

        _message = $"CSV 저장 및 JSON·아이콘 카탈로그 내보내기 완료: {entries.Length}개 강화";
    }

    /// <summary>
    /// 창을 닫을 때의 저장 요청과 CSV 저장 버튼 처리
    /// </summary>
    public override void SaveChanges()
    {
        ValidateRows(false);
        SaveCsv(_table.ToCsv());
        _message = $"CSV 저장 완료: {_table.Rows.Count}개 강화";
    }
}