using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 명시적으로 지정한 UI 묶음과 개별 버튼의 클릭음 연결
/// </summary>
[DisallowMultipleComponent]
public class UIButtonSoundBindings : MonoBehaviour
{
    [Serializable]
    class ButtonSoundGroup
    {
        public GameObject Root;
        public SoundCue Cue;
    }

    [Serializable]
    class ButtonSoundBinding
    {
        public Button Button;
        public SoundCue Cue;
    }

    [Header("----- 씬 재생기 -----")]
    [SerializeField] SoundPlayer _player;

    [Header("----- 지정한 UI 하위 버튼 전체 -----")]
    [Tooltip("활성화 시 지정 루트의 비활성 버튼까지 연결, 이후 생성한 버튼은 재연결 필요")]
    [SerializeField] ButtonSoundGroup[] _groups = Array.Empty<ButtonSoundGroup>();

    [Header("----- 개별 버튼 추가와 소리 재정의 -----")]
    [SerializeField] ButtonSoundBinding[] _bindings = Array.Empty<ButtonSoundBinding>();

    Button[] _subscribedButtons;
    UnityAction[] _handlers;

    void ValidateCue(SoundCue cue)
    {
        if (cue == null) throw new MissingReferenceException($"{name}: UI 소리 설정 연결 필요");
        cue.ValidateConfiguration();
        if (cue.Loop || cue.SpatialBlend != 0f)
            throw new InvalidOperationException($"{cue.name}: UI에는 2D 단발음 설정 필요");
    }

    void OnEnable()
    {
        if (_player == null || _groups == null || _bindings == null)
            throw new MissingReferenceException($"{name}: 씬 재생기와 버튼 연결 목록 필요");

        Dictionary<Button, SoundCue> resolved = new Dictionary<Button, SoundCue>();
        foreach (ButtonSoundGroup group in _groups)
        {
            if (group == null || group.Root == null)
                throw new MissingReferenceException($"{name}: 버튼 묶음의 UI 루트 연결 필요");
            ValidateCue(group.Cue);

            // 전역 탐색 없이 지정된 하위 UI만 열거하는 묶음 계약
            Button[] children = group.Root.GetComponentsInChildren<Button>(true);
            if (children.Length == 0)
                throw new InvalidOperationException($"{group.Root.name}: 연결할 Button이 없는 UI 루트");
            foreach (Button button in children)
            {
                if (resolved.ContainsKey(button))
                    throw new InvalidOperationException($"{name}: 서로 겹치는 UI 버튼 묶음");
                resolved.Add(button, group.Cue);
            }
        }

        HashSet<Button> overrides = new HashSet<Button>();
        foreach (ButtonSoundBinding binding in _bindings)
        {
            if (binding == null || binding.Button == null)
                throw new MissingReferenceException($"{name}: 개별 버튼 연결 필요");
            if (!overrides.Add(binding.Button))
                throw new InvalidOperationException($"{name}: 동일 버튼의 클릭음 중복 연결");
            ValidateCue(binding.Cue);
            resolved[binding.Button] = binding.Cue;
        }

        if (resolved.Count == 0)
            throw new InvalidOperationException($"{name}: UI 버튼 묶음 또는 개별 버튼 연결 필요");

        // 모든 설정의 검증 완료 이후 구독
        _subscribedButtons = new Button[resolved.Count];
        _handlers = new UnityAction[resolved.Count];
        int index = 0;
        foreach (KeyValuePair<Button, SoundCue> binding in resolved)
        {
            SoundCue cue = binding.Value;
            _subscribedButtons[index] = binding.Key;
            // 창이 닫혀도 씬 재생기에서 클릭음 유지
            _handlers[index] = () => _player.Play(cue, transform.position);
            binding.Key.onClick.AddListener(_handlers[index]);
            index++;
        }
    }

    void OnDisable()
    {
        if (_subscribedButtons == null) return;
        for (int index = 0; index < _subscribedButtons.Length; index++)
        {
            if (_subscribedButtons[index] != null)
                _subscribedButtons[index].onClick.RemoveListener(_handlers[index]);
        }
        _subscribedButtons = null;
        _handlers = null;
    }
}
