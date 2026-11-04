using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

static class SettingsBindings
{
    internal static readonly string[] Names =
    {
        "MoveClick", "MovementSkill", "Interact", "OpenMap", "AttackMode",
        "Skill1", "Skill2", "Skill3", "Skill4"
    };

    internal static readonly string[] Defaults =
    {
        "<Mouse>/rightButton", "<Keyboard>/space", "<Keyboard>/f", "<Keyboard>/m",
        "<Keyboard>/a", "<Keyboard>/q", "<Keyboard>/w", "<Keyboard>/e", "<Keyboard>/r"
    };

    internal const string Unassigned = "<Unassigned>";

    internal static string EffectivePath(string[] paths, int row)
    {
        return string.IsNullOrEmpty(paths[row]) ? Defaults[row] : paths[row];
    }

    internal static int FindConflict(string[] paths, int row, string path)
    {
        for (int index = 0; index < paths.Length; index++)
        {
            if (index != row && string.Equals(EffectivePath(paths, index), path,
                StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    internal static string Validate(string[] paths)
    {
        if (paths == null || paths.Length != Names.Length)
            return "키 설정 형식이 올바르지 않습니다.";

        HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int row = 0; row < paths.Length; row++)
        {
            string path = EffectivePath(paths, row);
            if (path == Unassigned)
                return "키가 지정되지 않은 동작이 있습니다.";
            if (!IsAllowed(path) || !used.Add(path))
                return "지원하지 않거나 중복된 키가 있습니다.";

            Resolve(row, out _);
        }

        return null;
    }

    internal static bool IsAllowed(string path)
    {
        if (path == "<Mouse>/rightButton" || path == "<Mouse>/middleButton" ||
            path == "<Mouse>/backButton" || path == "<Mouse>/forwardButton")
            return true;

        const string prefix = "<Keyboard>/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string name = path.Substring(prefix.Length);
        if (string.Equals(name, "escape", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var control in InputSystem.LoadLayout("Keyboard").controls)
        {
            if (string.Equals(control.name.ToString(), name, StringComparison.Ordinal) &&
                control.layout.ToString() == "Key")
                return true;
        }

        return false;
    }

    static InputAction Resolve(int row, out int bindingIndex)
    {
        InputAction action = InputSystem.actions.FindAction("Player/" + Names[row], true);
        bindingIndex = -1;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            if (action.bindings[i].path != Defaults[row]) continue;

            bindingIndex = i;
            break;
        }
        if (bindingIndex < 0)
            throw new InvalidOperationException($"{action.name}: 기본 바인딩 연결 필요");

        return action;
    }

    internal static void Apply(string[] paths)
    {
        for (int row = 0; row < paths.Length; row++)
        {
            InputAction action = Resolve(row, out int index);
            if (string.IsNullOrEmpty(paths[row]))
                action.RemoveBindingOverride(index);
            else
                action.ApplyBindingOverride(index, paths[row]);
        }
    }
}
