using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 로비의 강화 장착 요청과 저장 성공 후 적용 조율
/// </summary>
public class MetaLoadoutService
{
    readonly MetaProgression _progression;
    readonly MetaUpgradePurchaseService _purchases;
    readonly Action<MetaSavePart> _markChanged;
    readonly Func<bool> _isLobbyReady;

    bool _isProcessing;
    bool _faulted;

    public event Action OnChanged;

    public bool IsProcessing => _isProcessing;
    public bool IsFaulted => _faulted;
    public bool BlocksRunEntry => _isProcessing || _faulted;
    public string ErrorMessage { get; private set; }

    static void Notify(Action notification)
    {
        try
        {
            notification?.Invoke();
        }
        catch (Exception exception)
        {
            // 관찰자 오류와 저장된 장착 상태의 분리
            Debug.LogException(exception);
        }
    }

    Task<MetaEquipStatus> ExecuteAsync(Func<(MetaEquipStatus Status, MetaLoadoutRecord Candidate)> prepare)
    {
        if (_faulted)
            return Task.FromResult(MetaEquipStatus.Failed);

        if (_isProcessing || _purchases.BlocksRunEntry)
            return Task.FromResult(MetaEquipStatus.Busy);

        if (!_isLobbyReady())
            return Task.FromResult(MetaEquipStatus.NotAllowed);

        ErrorMessage = null;

        (MetaEquipStatus result, MetaLoadoutRecord candidate) = prepare();

        if (result != MetaEquipStatus.Prepared)
            return Task.FromResult(result);

        _isProcessing = true;
        Notify(OnChanged);

        try
        {
            _progression.ApplyPreparedLoadout(candidate);
            _markChanged(MetaSavePart.Loadouts);

            Notify(_progression.PublishChanged);

            return Task.FromResult(MetaEquipStatus.Applied);
        }
        catch (Exception exception)
        {
            // 적용 도중 예상하지 못한 오류가 나면 추가 편집 차단
            _faulted = true;
            ErrorMessage = exception.Message;
            Debug.LogException(exception);

            return Task.FromResult(MetaEquipStatus.Failed);
        }
        finally
        {
            _isProcessing = false;
            Notify(OnChanged);
        }
    }

    /// <summary>
    /// 장착 규칙과 구매 상태 및 변경 알림 연결
    /// </summary>
    /// <param name="progression">장착 상태 소유자</param>
    /// <param name="purchases">진행 중인 구매 확인 담당</param>
    /// <param name="markChanged">필수 변경 알림</param>
    /// <param name="isLobbyReady">현재 장착 변경 허용 조건</param>
    public MetaLoadoutService(MetaProgression progression, MetaUpgradePurchaseService purchases, Action<MetaSavePart> markChanged, Func<bool> isLobbyReady)
    {
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
        _markChanged = markChanged ?? throw new ArgumentNullException(nameof(markChanged));
        _isLobbyReady = isLobbyReady ?? throw new ArgumentNullException(nameof(isLobbyReady));
    }

    /// <summary>
    /// 강화 장착 또는 해제 요청
    /// </summary>
    /// <param name="characterId">대상 캐릭터 식별자</param>
    /// <param name="slotIndex">변경할 슬롯 번호</param>
    /// <param name="upgradeId">장착할 강화, 해제는 null</param>
    /// <param name="expectedUpgradeId">화면에서 확인한 기존 강화</param>
    /// <returns>장착 처리 결과</returns>
    public Task<MetaEquipStatus> RequestEquipAsync(string characterId, int slotIndex, string upgradeId, string expectedUpgradeId)
    {
        return ExecuteAsync(() =>
        {
            MetaEquipStatus result = _progression.TryPrepareLoadout(characterId, slotIndex, upgradeId, expectedUpgradeId, out MetaLoadoutRecord candidate);

            return (result, candidate);
        });
    }

    /// <summary>
    /// 두 슬롯의 이동 또는 교환을 한 번에 저장하는 요청
    /// </summary>
    /// <param name="characterId">대상 캐릭터 식별자</param>
    /// <param name="sourceSlot">출발 슬롯 번호</param>
    /// <param name="destinationSlot">도착 슬롯 번호</param>
    /// <param name="expectedSource">드래그 시작 시 확인한 출발 강화</param>
    /// <param name="expectedDestination">드래그 시작 시 확인한 도착 강화</param>
    /// <returns>이동 또는 교환 처리 결과</returns>
    public Task<MetaEquipStatus> RequestSwapAsync(string characterId, int sourceSlot, int destinationSlot, string expectedSource, string expectedDestination)
    {
        return ExecuteAsync(() =>
        {
            MetaEquipStatus result = _progression.TryPrepareSwap(characterId, sourceSlot, destinationSlot, expectedSource, expectedDestination, out MetaLoadoutRecord candidate);

            return (result, candidate);
        });
    }
}