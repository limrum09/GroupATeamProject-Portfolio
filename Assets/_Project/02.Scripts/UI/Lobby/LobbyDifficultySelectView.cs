using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 로비 난이도 버튼 입력 전달과 선택 아이콘 표시
/// </summary>
[DisallowMultipleComponent]
public class LobbyDifficultySelectView : MonoBehaviour
{
    [Serializable]
    class DifficultyButtonBinding
    {
        public Difficulty Difficulty;
        public Button Button;
        public Image Icon;
        public TextMeshProUGUI difficultyText;
        public Sprite ActiveIcon;
        public Sprite UnActiveIcon;
    }

    [Header("----- 난이도 버튼과 아이콘 -----")]
    [SerializeField]
    DifficultyButtonBinding[] _buttons = Array.Empty<DifficultyButtonBinding>();

    [Header("----- 난이도 글자 색상 -----")]
    [SerializeField] Color _activeTextColor = Color.white;
    [SerializeField] Color _unActiveTextColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    UnityAction[] _clickHandlers;
    bool _subscribed;

    public event Action<Difficulty> SelectionRequested;

    void OnEnable()
    {
        ValidateBindings();

        _clickHandlers = new UnityAction[_buttons.Length];

        for (int i = 0; i < _buttons.Length; i++)
        {
            Difficulty difficulty = _buttons[i].Difficulty;

            _clickHandlers[i] = () => SelectionRequested?.Invoke(difficulty);

            _buttons[i].Button.onClick.AddListener(_clickHandlers[i]);
        }

        _subscribed = true;
    }

    void OnDisable()
    {
        if (!_subscribed) return;

        _subscribed = false;

        for (int i = 0; i < _buttons.Length; i++)
        {
            // 씬 종료 중 이미 파괴된 버튼 제외
            if (_buttons[i].Button != null)
            {
                _buttons[i].Button.onClick.RemoveListener(_clickHandlers[i]);
            }
        }
    }

    /// <summary>
    /// 난이도별 버튼과 선택 상태 스프라이트 구성 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_buttons == null || _buttons.Length != (int)Difficulty.Count)
        {
            throw new InvalidOperationException($"{name}: 모든 난이도의 버튼을 각각 하나씩 연결 필요");
        }

        HashSet<Difficulty> difficulties = new HashSet<Difficulty>();
        HashSet<Button> buttons = new HashSet<Button>();
        HashSet<Image> icons = new HashSet<Image>();

        foreach (DifficultyButtonBinding binding in _buttons)
        {
            if (binding == null || binding.Button == null || binding.Icon == null || binding.ActiveIcon == null || binding.UnActiveIcon == null)
                throw new MissingReferenceException($"{name}: 난이도 버튼, Image, ActiveIcon, UnActiveIcon 연결 필요");

            RunStartRequest.ValidateDifficulty(binding.Difficulty);

            if (!difficulties.Add(binding.Difficulty))
                throw new InvalidOperationException($"{name}: 중복 난이도 연결: {binding.Difficulty}");

            if (!buttons.Add(binding.Button) || !icons.Add(binding.Icon))
                throw new InvalidOperationException($"{name}: 난이도마다 서로 다른 Button과 Image 연결 필요");

            if (binding.ActiveIcon == binding.UnActiveIcon)
                throw new InvalidOperationException($"{name}: {binding.Difficulty}의 Active/UnActive 스프라이트 구분 필요");

            if (binding.Button.transition == Selectable.Transition.SpriteSwap)
                throw new InvalidOperationException($"{name}: 선택 아이콘과 충돌하는 Button Sprite Swap 사용 불가");
        }
    }

    /// <summary>
    /// 확정된 난이도에 따른 Active 및 UnActive 아이콘 표시
    /// </summary>
    /// <param name="selectedDifficulty">로비에서 현재 선택한 난이도</param>
    public void Show(Difficulty selectedDifficulty)
    {
        RunStartRequest.ValidateDifficulty(selectedDifficulty);

        foreach (DifficultyButtonBinding binding in _buttons)
        {
            bool isSelected = binding.Difficulty == selectedDifficulty;

            binding.Icon.sprite = isSelected ? binding.ActiveIcon : binding.UnActiveIcon;
            binding.difficultyText.color = isSelected ? _activeTextColor : _unActiveTextColor;
        }
    }

    /// <summary>
    /// 모든 난이도 버튼의 입력 허용 변경
    /// </summary>
    /// <param name="canSelect">난이도 선택 가능 여부</param>
    public void SetInteraction(bool canSelect)
    {
        foreach (DifficultyButtonBinding binding in _buttons)
        {
            binding.Button.interactable = canSelect;
        }
    }
}