using System;
using System.Collections.Generic;
using UnityEngine;
using Image = UnityEngine.UI.Image;

/// <summary>
/// 메타 장착 슬롯의 직업별 배치와 엠블럼, 연결선 표시
/// </summary>
[DisallowMultipleComponent]
public class MetaLoadoutLayoutView : MonoBehaviour
{
    [Serializable]
    class LoadoutLayout
    {
        public PlayerClass Class;
        public Sprite Emblem;
        public Vector2 EmblemPosition;
        public Vector2 EmblemSize;
        public Vector2[] SlotPositions = new Vector2[MetaProgression.EquipSlotCount];
        public Vector2Int[] Connections = Array.Empty<Vector2Int>();
    }

    [Header("----- 왼쪽 슬롯 순서 -----")]
    [SerializeField] MetaUpgradeSlotView[] _slots = Array.Empty<MetaUpgradeSlotView>();

    [Header("----- 직업 엠블럼 -----")]
    [SerializeField] UnityEngine.UI.Image _emblem;

    [Header("----- 직업별 슬롯과 엠블럼 배치 -----")]
    [SerializeField] LoadoutLayout[] _loadoutLayouts = Array.Empty<LoadoutLayout>();

    [Header("----- 연결선 표시 -----")]
    [SerializeField] Image[] _lineImages = Array.Empty<Image>();
    [SerializeField] Sprite _normalLineSprite;
    [SerializeField] Sprite _glowingLineSprite;

    [Header("----- 공용 해금 안내 -----")]
    [SerializeField] MetaUnlockHintView _unlockHint;

    readonly bool[] _slotUnlocked = new bool[MetaProgression.EquipSlotCount];
    readonly int[] _lockedUntilLevel = new int[MetaProgression.EquipSlotCount];
    LoadoutLayout _activeLayout;

    public IReadOnlyList<MetaUpgradeSlotView> Slots => _slots;

    void OnEnable()
    {
        foreach (MetaUpgradeSlotView slot in _slots)
            slot.LoadoutHover.OnHoverChanged += HandleHoverChanged;

        RefreshUnlockHint();
    }

    void OnDisable()
    {
        foreach (MetaUpgradeSlotView slot in _slots)
        {
            if (slot != null && slot.LoadoutHover != null)
                slot.LoadoutHover.OnHoverChanged -= HandleHoverChanged;
        }

        if (_unlockHint != null && _unlockHint.isActiveAndEnabled)
            _unlockHint.Hide();
    }

    void HandleHoverChanged(SkillIconHover source, bool hovered)
    {
        RefreshUnlockHint();
    }

    void RefreshUnlockHint()
    {
        if (!isActiveAndEnabled) return;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_lockedUntilLevel[i] > 0 && _slots[i].LoadoutHover.IsHovered)
            {
                _unlockHint.Show((RectTransform)_slots[i].transform, _lockedUntilLevel[i]);
                return;
            }
        }

        _unlockHint.Hide();
    }

    /// <summary>
    /// 선택한 직업의 엠블럼, 슬롯 위치와 연결선 배치
    /// </summary>
    /// <param name="playerClass">선택한 캐릭터 직업</param>
    public void ShowLayout(PlayerClass playerClass)
    {
        LoadoutLayout layout = Array.Find(_loadoutLayouts, item => item.Class == playerClass);

        if (layout == null)
            throw new InvalidOperationException($"{name}: {playerClass} 슬롯 배치 설정 필요");

        if (layout.SlotPositions.Length != _slots.Length)
            throw new InvalidOperationException($"{name}: {playerClass} 슬롯 위치 {_slots.Length}개 필요");

        if (layout.Connections.Length > _lineImages.Length)
            throw new InvalidOperationException($"{name}: {playerClass} 연결선 이미지 수 부족");

        _emblem.sprite = layout.Emblem;
        RectTransform emblemRect = _emblem.rectTransform;
        emblemRect.anchoredPosition = layout.EmblemPosition;
        emblemRect.sizeDelta = layout.EmblemSize;

        _activeLayout = layout;
        Array.Clear(_slotUnlocked, 0, _slotUnlocked.Length);
        Array.Clear(_lockedUntilLevel, 0, _lockedUntilLevel.Length);
        _unlockHint.Hide();

        for (int i = 0; i < _slots.Length; i++)
        {
            RectTransform slotRect = (RectTransform)_slots[i].transform;
            slotRect.anchoredPosition = layout.SlotPositions[i];
        }

        for (int i = 0; i < _lineImages.Length; i++)
        {
            Image line = _lineImages[i];
            line.gameObject.SetActive(false);

            if (i >= layout.Connections.Length)
                continue;

            Vector2Int pair = layout.Connections[i];
            if (pair.x < 0 || pair.x >= _slots.Length || pair.y < 0 || pair.y >= _slots.Length || pair.x == pair.y)
                throw new InvalidOperationException($"{name}: 잘못된 슬롯 연결 {pair}");

            Vector2 start = layout.SlotPositions[pair.x];
            Vector2 end = layout.SlotPositions[pair.y];
            Vector2 direction = end - start;

            RectTransform lineRect = line.rectTransform;
            lineRect.anchoredPosition = (start + end) * 0.5f;
            lineRect.sizeDelta = new Vector2(lineRect.sizeDelta.x, direction.magnitude);
            lineRect.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, direction));
        }
    }

    /// <summary>
    /// 장착 슬롯 한 칸과 해당 슬롯에 연결된 선 표시
    /// </summary>
    /// <param name="slot">왼쪽 슬롯 번호</param>
    /// <param name="visibleIcon">표시할 아이콘, 빈칸이나 드래그 중 숨김이면 null</param>
    /// <param name="unlocked">슬롯 해금 여부</param>
    /// <param name="hasUpgrade">아이콘 표시와 별개인 실제 장착 여부</param>
    public void ShowSlot(int slot, Sprite visibleIcon, bool unlocked, bool hasUpgrade, int unlockLevel)
    {
        _slots[slot].ShowLoadout(visibleIcon, unlocked, hasUpgrade);
        _slotUnlocked[slot] = unlocked;

        _lockedUntilLevel[slot] = unlocked ? 0 : unlockLevel;

        if (_slots[slot].LoadoutHover.IsHovered)
            RefreshUnlockHint();

        if (_activeLayout == null) return;

        for (int i = 0; i < _activeLayout.Connections.Length; i++)
        {
            Vector2Int pair = _activeLayout.Connections[i];
            if (pair.x != slot && pair.y != slot) continue;

            bool bothUnlocked = _slotUnlocked[pair.x] && _slotUnlocked[pair.y];
            Image line = _lineImages[i];
            line.sprite = bothUnlocked ? _glowingLineSprite : _normalLineSprite;
            line.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 현재 직업의 배치를 보관한 채 슬롯 표시와 연결선 초기화
    /// </summary>
    public void Clear()
    {
        Array.Clear(_slotUnlocked, 0, _slotUnlocked.Length);
        Array.Clear(_lockedUntilLevel, 0, _lockedUntilLevel.Length);
        _unlockHint.Hide();

        foreach (MetaUpgradeSlotView slot in _slots)
            slot.ShowLoadout(null, false, false);

        foreach (Image line in _lineImages)
            line.gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 장착 슬롯 수와 엠블럼, 연결선 표시 참조 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_slots.Length != MetaProgression.EquipSlotCount)
            throw new InvalidOperationException($"{name}: 왼쪽 장착 슬롯 6개 연결 필요");

        if (_loadoutLayouts.Length == 0)
            throw new InvalidOperationException($"{name}: 직업별 슬롯 배치 설정 필요");

        if (_emblem == null)
            throw new MissingReferenceException($"{name}: 직업 엠블럼 Image 연결 필요");

        foreach (LoadoutLayout layout in _loadoutLayouts)
        {
            if (layout.Emblem == null)
                throw new MissingReferenceException($"{name}: {layout.Class} 엠블럼 Sprite 연결 필요");

            if (layout.EmblemSize.x <= 0f || layout.EmblemSize.y <= 0f)
                throw new InvalidOperationException($"{name}: {layout.Class} 엠블럼 크기는 양수 필요");
        }

        if (_normalLineSprite == null || _glowingLineSprite == null)
            throw new MissingReferenceException($"{name}: 기본 선과 발광 선 스프라이트 연결 필요");

        foreach (Image line in _lineImages)
        {
            if (line == null)
                throw new MissingReferenceException($"{name}: 연결선 Image 참조 누락");
        }
    }
#endif
}