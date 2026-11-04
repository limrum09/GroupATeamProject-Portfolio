using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 메타 화면 입력과 기존 구매 및 장착 기능의 연결
/// </summary>
public class MetaUpgradePresenter : IDisposable
{
    readonly LobbyManager _lobby;
    readonly IMetaUpgradeView _view;

    IReadOnlyList<MetaUpgradeData> _upgrades;
    IReadOnlyList<string> _slots;
    IReadOnlyList<string> _dragSnapshot;

    string _characterId;
    string _dragUpgradeId;

    int _selectedIndex = -1;
    int _selectedSlot = -1;
    int _shownRank;
    int _dragSourceSlot = -1;
    int _hiddenDuringSave = -1;

    bool _active;
    bool _busy;
    bool _dragging;

    bool Ready => _lobby.IsMetaReady && !_lobby.IsCharacterTestMode;
    bool CanChange => _active && Ready && !_busy && !_dragging && _lobby.CanEditMetaLoadout;
    public bool CanClose => !_busy && !_dragging && !_lobby.IsMetaProcessing;

    void HandleChanged()
    {
        // 드래그 도중 외부 상태가 바뀐 경우 미확정 입력 취소
        if (_dragging)
        {
            _view.CancelDrag();
            ClearDragState();
        }

        Refresh();
    }

    int FindCatalog(string upgradeId)
    {
        if (upgradeId == null)
            return -1;

        for (int i = 0; i < _upgrades.Count; i++)
        {
            if (_upgrades[i].UpgradeId == upgradeId)
                return i;
        }

        throw new InvalidOperationException($"화면 목록에 없는 장착 강화: {upgradeId}");
    }

    int FindEquipped(string upgradeId)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] == upgradeId)
                return i;
        }

        return -1;
    }

    void Refresh()
    {
        if (!_active) return;

        if (!Ready)
        {
            _view.Clear();

            string label = _lobby.IsCharacterTestMode? "사용 불가" : _lobby.HasMetaLoadFailed ? "불러오기 실패" : "불러오는 중";

            _view.SetInteraction(false, false, CanClose, label);
            return;
        }

        string characterId = _lobby.SelectedCharacter.CharacterId;

        if (_characterId != characterId)
        {
            _characterId = characterId;
            _upgrades = _lobby.GetUpgrades();

            // 로딩된 데이터와 고정 UI 목록의 최초 연결 확인
            if (_upgrades.Count != _view.CatalogCount)
                throw new InvalidOperationException("활성 메타 강화 수와 오른쪽 UI 항목 수 일치 필요");

            _selectedIndex = -1;
            _selectedSlot = -1;
        }

        _slots = _lobby.GetEquippedUpgradeIds();

        _view.ShowHeader(_lobby.SelectedCharacter.DisplayName, _lobby.MetaBalance);
        _lobby.GetSelectedCharacterMasteryProgress(out int characterLevel, out long currentExperience, out long requiredExperience);
        _view.ShowMastery(_lobby.IntegratedMasteryLevel, characterLevel, currentExperience, requiredExperience);

        for (int i = 0; i < _upgrades.Count; i++)
        {
            MetaUpgradeData upgrade = _upgrades[i];
            int rank = _lobby.GetPurchasedRank(upgrade.UpgradeId);
            bool equipped = FindEquipped(upgrade.UpgradeId) >= 0;

            if (_dragging && _dragSourceSlot >= 0 && upgrade.UpgradeId == _dragUpgradeId)
                equipped = false;

            _view.ShowCatalog(i, $"{rank}/{upgrade.MaxRank}", equipped, i == _selectedIndex);
        }

        for (int i = 0; i < _slots.Count; i++)
        {
            bool hidden = (_dragging && i == _dragSourceSlot) || (_busy && i == _hiddenDuringSave);

            _view.ShowSlot(i, hidden ? -1 : FindCatalog(_slots[i]), i == _selectedSlot);
        }

        bool pendingSave = _lobby.HasPendingMetaSave;
        bool canPurchase = false;
        string purchaseLabel = _lobby.HasMetaOperationFault ? "사용 불가" : _busy || _lobby.IsMetaProcessing ? "처리 중" : pendingSave ? "저장 재시도" : "강화";

        if (_selectedIndex < 0)
        {
            _view.ShowDetails(-1, string.Empty, string.Empty, "오른쪽에서 강화를 선택하세요.", "—");
        }
        else
        {
            MetaUpgradeData selected = _upgrades[_selectedIndex];
            _shownRank = _lobby.GetPurchasedRank(selected.UpgradeId);

            bool hasNext = _lobby.TryGetNextRank(selected.UpgradeId, out MetaUpgradeRankData nextRank);

            string description = FormatDescription(selected, _shownRank);
            string price = pendingSave ? "—" : hasNext ? nextRank.Cost.ToString("N0") : "최대 단계";

            _view.ShowDetails(_selectedIndex, selected.DisplayName, $"{_shownRank}/{selected.MaxRank}", description, price);

            canPurchase = CanChange && hasNext && _lobby.MetaBalance >= nextRank.Cost;
        }

        if (pendingSave)
        {
            canPurchase = !_busy && !_dragging && _lobby.CanRetryMetaSave;
        }

        bool canInspect = !_busy && !_dragging && !_lobby.IsMetaProcessing;

        _view.SetInteraction(canInspect, canPurchase, CanClose, purchaseLabel);

        // 현재 적용 중인 레벨 또는 해금 제한 없음
        _view.ShowRestriction(_lobby.MetaSaveError ?? string.Empty);
    }

    void HandleClicked(bool fromLoadout, int index, bool doubleClick)
    {
        if (!_active || !Ready || _busy || _dragging)
            return;

        string upgradeId = fromLoadout ? _slots[index] : _upgrades[index].UpgradeId;

        _selectedIndex = FindCatalog(upgradeId);
        _selectedSlot = fromLoadout ? index : -1;

        Refresh();

        if (!doubleClick || !CanChange || upgradeId == null)
            return;

        if (fromLoadout)
        {
            Execute(async () => EquipMessage(await _lobby.RequestEquipAsync(index, null, upgradeId)), index);

            return;
        }

        if (_lobby.GetPurchasedRank(upgradeId) == 0 || FindEquipped(upgradeId) >= 0)
            return;

        for (int slot = 0; slot < _slots.Count; slot++)
        {
            if (_slots[slot] != null) continue;

            int destination = slot;

            Execute(async () => EquipMessage(await _lobby.RequestEquipAsync( destination, upgradeId, null)));

            return;
        }

        Refresh();
    }

    void HandleDragStarted(bool fromLoadout, int index)
    {
        if (!CanChange)
        {
            _view.CancelDrag();
            return;
        }

        string upgradeId = fromLoadout ? _slots[index] : _upgrades[index].UpgradeId;

        if (upgradeId == null)
        {
            _view.CancelDrag();
            return;
        }

        _selectedIndex = FindCatalog(upgradeId);
        _selectedSlot = fromLoadout ? index : -1;

        if (_lobby.GetPurchasedRank(upgradeId) == 0 || (!fromLoadout && FindEquipped(upgradeId) >= 0))
        {
            _view.CancelDrag();
            Refresh();
            return;
        }

        _dragging = true;
        _dragUpgradeId = upgradeId;
        _dragSourceSlot = fromLoadout ? index : -1;
        _dragSnapshot = _slots;

        Refresh();
        _view.ShowDragIcon(_selectedIndex);
    }

    void HandleDropped(int destination)
    {
        if (!_active || !_dragging)
            return;

        int source = _dragSourceSlot;
        string upgradeId = _dragUpgradeId;
        IReadOnlyList<string> expected = _dragSnapshot;

        ClearDragState();

        if (source >= 0)
        {
            if (destination == source)
            {
                // 원위치 드롭은 저장 없이 기존 표시 복원
                Refresh();
                return;
            }

            if (destination < 0)
            {
                Execute(async () => EquipMessage(await _lobby.RequestEquipAsync( source, null, expected[source])), source);
            }
            else
            {
                Execute(async () => EquipMessage(await _lobby.RequestSwapAsync( source, destination, expected[source], expected[destination])), source);
            }

            return;
        }

        if (destination >= 0)
        {
            Execute(async () => EquipMessage(await _lobby.RequestEquipAsync( destination, upgradeId, expected[destination])));
        }
        else
        {
            // 오른쪽 목록에서 허공으로 드래그한 경우 변경 없음
            Refresh();
        }
    }

    void HandleDragCanceled()
    {
        if (!_active) return;

        ClearDragState();
        Refresh();
    }

    void ClearDragState()
    {
        _dragging = false;
        _dragSourceSlot = -1;
        _dragUpgradeId = null;
        _dragSnapshot = null;
    }

    void HandlePurchase()
    {
        if (!_active || !Ready || _busy || _dragging)
            return;

        if (_lobby.HasPendingMetaSave)
        {
            if (_lobby.CanRetryMetaSave)
            {
                Execute(async () => await _lobby.SaveMetaChangesAsync() ? null : _lobby.MetaSaveError ?? "저장을 완료하지 못했습니다.");
            }

            return;
        }

        if (!CanChange || _selectedIndex < 0)
            return;

        Guid requestId = Guid.NewGuid();
        string upgradeId = _upgrades[_selectedIndex].UpgradeId;
        int expectedRank = _shownRank;

        Execute(async () => PurchaseMessage(await _lobby.RequestMetaPurchaseAsync( requestId, upgradeId, expectedRank)));
    }

    async void Execute(Func<Task<string>> request, int hiddenSource = -1)
    {
        if (_busy) return;

        _busy = true;
        _hiddenDuringSave = hiddenSource;

        try
        {
            Refresh();

            string error = await request();

            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning(error);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            _busy = false;
            _hiddenDuringSave = -1;

            if (_active)
                Refresh();
        }
    }

    static string FormatDescription(MetaUpgradeData upgrade, int purchasedRank)
    {
        StringBuilder text = new StringBuilder();

        // 단계별 효과를 스탯과 적용 시점 기준으로 묶어 표시
        var groups = upgrade.Ranks.SelectMany(rank => rank.Effects, (rank, effect) => (Rank: rank.Rank, Effect: effect)).GroupBy(item => (item.Effect.StatId, item.Effect.ApplyTiming, item.Effect.ModifierType));

        foreach (var group in groups)
        {
            string stat = group.Key.StatId switch
            {
                "MaxHealth" => "최대 체력을",
                "Attack" => "공격력을",
                "Defense" => "방어력을",
                "SpellPower" => "주문력을",
                "MoveSpeed" => "이동 속도를",
                "AttackSpeed" => "공격 속도를",
                "CritChance" => "치명타 확률을",
                "CritDamageMultiplier" => "치명타 피해 배율을",
                _ => throw new InvalidOperationException($"표시 이름이 없는 스탯: {group.Key.StatId}")
            };

            string timing = group.Key.ApplyTiming switch
            {
                "RunStart" => "시작",
                "LevelUp" => "레벨업마다",
                _ => throw new InvalidOperationException($"지원하지 않는 적용 시점: {group.Key.ApplyTiming}")
            };

            string unit = group.Key.StatId switch
            {
                "CritChance" => "%p",
                "CritDamageMultiplier" => "배",
                _ => string.Empty
            };

            Dictionary<int, float> values = group.ToDictionary(item => item.Rank, item => item.Effect.Value);

            if (text.Length > 0)
                text.AppendLine();

            text.Append(timing).Append(' ').Append(stat).Append(' ');

            for (int i = 0; i < upgrade.Ranks.Count; i++)
            {
                MetaUpgradeRankData rank = upgrade.Ranks[i];

                if (i > 0)
                    text.Append(" / ");

                // 해당 단계에 이 효과가 없으면 기여 값 0으로 표시
                values.TryGetValue(rank.Rank, out float value);

                if (group.Key.StatId == "CritChance")
                    value *= 100f;

                string color = rank.Rank == purchasedRank ? "#66FF66" : rank.Rank < purchasedRank ? "#FFFFFF" : "#808080";

                text.Append("<color=").Append(color).Append('>').Append(value.ToString("0.###")).Append(unit).Append("</color>");
            }

            text.Append(" 증가시킵니다.");
        }

        return text.ToString();
    }

    static string EquipMessage(MetaEquipStatus result)
    {
        return result switch
        {
            MetaEquipStatus.Applied => string.Empty,
            MetaEquipStatus.Unchanged => string.Empty,
            MetaEquipStatus.NotPurchased => "먼저 해당 강화를 구매해 주세요.",
            MetaEquipStatus.AlreadyEquipped => "같은 강화를 중복 장착할 수 없습니다.",
            MetaEquipStatus.Unavailable => "장착할 수 없는 항목입니다.",
            MetaEquipStatus.StaleSelection => "슬롯 정보가 변경되었습니다. 다시 시도해 주세요.",
            MetaEquipStatus.Busy => "진행 중인 작업이 끝난 뒤 다시 시도해 주세요.",
            MetaEquipStatus.NotAllowed => "현재는 장착 정보를 변경할 수 없습니다.",
            _ => "장착 정보를 처리하지 못했습니다."
        };
    }

    static string PurchaseMessage(MetaPurchaseResult result)
    {
        return result.Status switch
        {
            MetaPurchaseStatus.Applied => string.Empty,
            MetaPurchaseStatus.InsufficientFunds => "메타 재화가 부족합니다.",
            MetaPurchaseStatus.StaleSelection => "구매 단계가 변경되었습니다. 다시 확인해 주세요.",
            MetaPurchaseStatus.NotPurchasable => "구매 가능한 다음 단계가 없습니다.",
            MetaPurchaseStatus.Busy => "진행 중인 작업이 끝난 뒤 다시 시도해 주세요.",
            MetaPurchaseStatus.Processing => string.Empty,
            MetaPurchaseStatus.NotAllowed => "현재는 구매할 수 없습니다.",
            MetaPurchaseStatus.Faulted => "메타 구매 상태 확인이 필요합니다.",
            _ => "구매를 완료하지 못했습니다."
        };
    }

    /// <summary>
    /// Inspector에서 연결한 로비와 화면 계약 보관
    /// </summary>
    /// <param name="lobby">메타 조회와 요청 연결 담당</param>
    /// <param name="view">메타 화면 표시 계약</param>
    public MetaUpgradePresenter(LobbyManager lobby, IMetaUpgradeView view)
    {
        _lobby = lobby;
        _view = view;
    }

    /// <summary>
    /// 화면 입력과 모델 변경 알림 구독
    /// </summary>
    public void Activate()
    {
        if (_active) return;

        _active = true;

        _lobby.OnChanged += HandleChanged;
        _view.Clicked += HandleClicked;
        _view.DragStarted += HandleDragStarted;
        _view.Dropped += HandleDropped;
        _view.DragCanceled += HandleDragCanceled;
        _view.PurchaseRequested += HandlePurchase;

        Refresh();
    }

    /// <summary>
    /// 구독 해제와 미확정 드래그 취소
    /// </summary>
    public void Deactivate()
    {
        if (!_active) return;

        _active = false;

        if (_lobby != null)
            _lobby.OnChanged -= HandleChanged;

        _view.Clicked -= HandleClicked;
        _view.DragStarted -= HandleDragStarted;
        _view.Dropped -= HandleDropped;
        _view.DragCanceled -= HandleDragCanceled;
        _view.PurchaseRequested -= HandlePurchase;

        _view.CancelDrag();
        ClearDragState();
    }

    /// <summary>
    /// Presenter 사용 종료
    /// </summary>
    public void Dispose()
    {
        Deactivate();
    }
}