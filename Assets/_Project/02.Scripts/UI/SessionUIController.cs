using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 세션 HUD와 상점 및 지도와 월드 전환·클리어 화면 조율
/// </summary>
[DisallowMultipleComponent]
public class SessionUIController : MonoBehaviour
{
    [Header("----- 세션 화면 담당 -----")]
    [SerializeField] ShopUIController _shop;
    [SerializeField] WorldGraphUIController _worldGraph;

    [Tooltip("다음 월드 전환 화면이 없는 독립 테스트에서는 생략 가능")]
    [SerializeField] NextWorldUIController _nextWorld;

    [Tooltip("클리어 화면이 없는 독립 테스트에서는 생략 가능")]
    [SerializeField] RunClearUIController _runClear;

    [Header("----- 선택 기능: 스킬 HUD -----")]
    [Tooltip("HUD 없는 독립 테스트에서는 생략 가능, 정식 런에서는 필수")]
    [SerializeField] SkillBarUIController _skillHud;

    [Header("----- 상점 이용 중 제어 -----")]
    [Tooltip("전투 UI와 플레이어 조작이 없는 독립 테스트에서는 두 참조 모두 생략 가능")]
    [SerializeField] GameObject _combatPanel;

    [Header("----- 플레이어 프로필 -----")]
    [SerializeField] PlayerProfileUIController _playerProfile;
    [Header("----- 플레이어 체력 -----")]
    [SerializeField] PlayerHealthUIController _playerHealth;

    InputAction _openMapAction;
    bool _bound;
    bool _shopPresentationActive;
    bool _combatPanelWasActive;

    public event Action<bool> OnShopStateChanged;

    public bool IsShopOpen => _shopPresentationActive;
    public bool IsBound => _bound;
    public bool HasSkillHud => _skillHud != null;
    public bool IsSkillHudBound => _bound && _skillHud != null && _skillHud.IsBound;

    void LateUpdate()
    {
        if (!_bound || Time.timeScale <= 0f || IsShopOpen ||
            !_openMapAction.WasPressedThisFrame())
            return;

        if ((_nextWorld != null && HasVisibleCanvas(_nextWorld)) ||
            (_runClear != null && HasVisibleCanvas(_runClear)))
            return;

        _worldGraph.Open();
    }

    static bool HasVisibleCanvas(Component controller)
    {
        foreach (Canvas canvas in controller.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.isActiveAndEnabled) return true;
        }

        return false;
    }

    void OnDestroy()
    {
        Unbind();
    }

    void ValidateReferences()
    {
        if (_shop == null || _worldGraph == null)
            throw new MissingReferenceException($"{name}: 상점과 월드맵 Controller 연결 필요");
    }
    void HandleShopOpening()
    {
        _worldGraph.Close();

        if (_nextWorld != null)
            _nextWorld.Close();

        if (_runClear != null)
            _runClear.Close();

        SetShopPresentation(true);
    }

    void HandleShopClosed()
    {
        SetShopPresentation(false);
    }

    void SetShopPresentation(bool isOpen)
    {
        if (_shopPresentationActive == isOpen)
            return;

        if (isOpen && _combatPanel != null)
            _combatPanelWasActive = _combatPanel.activeSelf;

        _shopPresentationActive = isOpen;

        if (_combatPanel != null)
            _combatPanel.SetActive(isOpen ? false : _combatPanelWasActive);

        OnShopStateChanged?.Invoke(isOpen);
    }

    static void TryCleanup(Action operation, List<Exception> errors)
    {
        try
        {
            operation();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    /// <summary>
    /// 런 연결 전 화면 구성 검사와 HUD 숨김
    /// </summary>
    public void InitializeForScene()
    {
        ValidateReferences();

        if (_bound || _shopPresentationActive || (_skillHud != null && _skillHud.IsBound) || (_playerProfile != null && _playerProfile.IsBound || (_playerHealth != null && _playerHealth.IsBound)))
            throw new InvalidOperationException($"{name}: 이전 UI 연결 해제 필요");

        CloseHud();
    }

    /// <summary>
    /// 세션 기능과 플레이어 정보를 하위 화면에 연결
    /// </summary>
    /// <param name="world">월드 진행 담당자</param>
    /// <param name="shop">상점 접근 담당자</param>
    /// <param name="purchases">세션 구매 서비스</param>
    /// <param name="loadout">스킬 HUD와 조작 제어 대상, 해당 기능이 없으면 생략 가능</param>
    /// <param name="playerDefinition">프로필의 캐릭터 정의, 프로필 화면이 없으면 생략 가능</param>
    /// <param name="progression">프로필의 런 성장 상태, 프로필 화면이 없으면 생략 가능</param>
    /// <param name="playerStatus">체력 화면의 전투 상태, 해당 화면이 없으면 생략 가능</param>
    /// <param name="economy">스킬 HUD의 유료 강화에 사용할 세션 지갑</param>
    public void Bind(SessionProgressManager world, SessionShop shop, SessionShopPurchaseService purchases, SkillLoadout loadout = null, PlayerDefinition playerDefinition = null, RunLevelProgression progression = null, CharacterStatus playerStatus = null, SessionEconomyManager economy = null)
    {
        ValidateReferences();

        if (_bound || !isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 활성 상태의 미연결 UI 필요");

        if (_skillHud != null && loadout == null)
            throw new ArgumentNullException(nameof(loadout));

        if (loadout != null && loadout.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 플레이어 로드아웃 필요");

        if (_playerProfile != null)
        {
            if (playerDefinition == null)
                throw new ArgumentNullException(nameof(playerDefinition));

            if (progression == null)
                throw new ArgumentNullException(nameof(progression));
        }

        if (_playerHealth != null)
        {
            if (playerStatus == null)
                throw new ArgumentNullException(nameof(playerStatus));

            if (playerStatus.gameObject.scene != gameObject.scene)
                throw new InvalidOperationException($"{name}: 같은 씬의 플레이어 전투 상태 필요");
        }

        _openMapAction = InputSystem.actions.FindAction("Player/OpenMap", true);
        _bound = true;

        try
        {
            _worldGraph.Bind(world);
            _shop.Bind(shop, purchases);

            if (_nextWorld != null)
                _nextWorld.Bind(world);

            if (_runClear != null)
                _runClear.Bind(world);

            if (_skillHud != null)
                _skillHud.Bind(loadout, economy);

            if (_playerProfile != null)
                _playerProfile.Bind(playerDefinition, progression);

            if (_playerHealth != null)
                _playerHealth.Bind(playerStatus);


            _shop.OnOpening += HandleShopOpening;
            _shop.OnClosed += HandleShopClosed;
        }
        catch (Exception bindError)
        {
            try
            {
                Unbind();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("세션 UI 연결 및 정리 실패", bindError, cleanupError);
            }

            throw;
        }
    }

    /// <summary>
    /// 조작 재개 없이 하위 화면 연결 정리와 오류 취합
    /// </summary>
    public void Unbind()
    {
        if (!_bound)
            return;

        _bound = false;
        List<Exception> errors = new List<Exception>();

        // 상점 문맥 종료 전에 UI의 복구 콜백 해제
        if (_shop != null)
        {
            _shop.OnOpening -= HandleShopOpening;
            _shop.OnClosed -= HandleShopClosed;
            TryCleanup(_shop.Unbind, errors);
        }

        TryCleanup(() => SetShopPresentation(false), errors);

        if (_runClear != null)
            TryCleanup(_runClear.Unbind, errors);

        if (_nextWorld != null)
            TryCleanup(_nextWorld.Unbind, errors);

        if (_worldGraph != null)
            TryCleanup(_worldGraph.Unbind, errors);

        // 전투 패널 복원 이후 HUD를 최종적으로 숨기고 해제
        if (_skillHud != null)
        {
            TryCleanup(_skillHud.Close, errors);
            TryCleanup(_skillHud.Unbind, errors);
        }

        if (_playerProfile != null)
        {
            TryCleanup(_playerProfile.Close, errors);
            TryCleanup(_playerProfile.Unbind, errors);
        }


        if (_playerHealth != null)
        {
            TryCleanup(_playerHealth.Close, errors);
            TryCleanup(_playerHealth.Unbind, errors);
        }


        if (errors.Count > 0)
            throw new AggregateException("세션 UI 정리 실패", errors);
    }

    /// <summary>
    /// 세션 준비 완료 이후 연결된 HUD 표시
    /// </summary>
    public void OpenHud()
    {
        if (!IsSkillHudBound)
            throw new InvalidOperationException(
                $"{name}: 스킬 HUD 연결 완료 필요");

        if (_playerProfile != null && !_playerProfile.IsBound)
            throw new InvalidOperationException($"{name}: 플레이어 프로필 연결 완료 필요");

        if (_playerHealth != null && !_playerHealth.IsBound)
            throw new InvalidOperationException($"{name}: 플레이어 체력 연결 완료 필요");

        _skillHud.Open();

        if (_playerProfile != null)
            _playerProfile.Open();

        if (_playerHealth != null)
            _playerHealth.Open();
    }

    /// <summary>
    /// 구성된 HUD 숨김
    /// </summary>
    public void CloseHud()
    {
        if (_skillHud != null)
            _skillHud.Close();

        if (_playerProfile != null)
            _playerProfile.Close();

        if (_playerHealth != null)
            _playerHealth.Close();
    }

    /// <summary>
    /// 씬 라우터의 취소 입력 처리 여부 지정
    /// </summary>
    /// <param name="routed">라우터 연결 여부</param>
    public void SetCancelInputRouted(bool routed)
    {
        _shop.SetCancelInputRouted(routed);
        _worldGraph.SetCancelInputRouted(routed);
    }

    /// <summary>
    /// 클리어·월드 확인·상점·월드맵 순서의 취소 요청 전달
    /// </summary>
    /// <returns>세션 화면이 취소 입력을 소비했는지 여부</returns>
    public bool TryHandleCancel()
    {
        if (!_bound) throw new InvalidOperationException($"{name}: 취소 처리 전 세션 UI 연결 필요");
        if (_runClear != null && _runClear.TryHandleCancel()) return true;
        if (_nextWorld != null && _nextWorld.TryHandleCancel()) return true;
        if (_shop.TryHandleCancel()) return true;

        return _worldGraph.TryHandleCancel();
    }

    /// <summary>
    /// 상점 접근 문맥 종료 이후 월드맵 표시
    /// </summary>
    public void OpenWorldGraph()
    {
        if (!_bound)
            throw new InvalidOperationException("세션 UI 연결 필요");

        _shop.Close();

        if (_nextWorld != null)
            _nextWorld.Close();

        if (_runClear != null)
            _runClear.Close();

        _worldGraph.Open();
    }

    /// <summary>
    /// 기존 화면 종료 후 다음 월드 확인창 표시
    /// </summary>
    /// <param name="portal">상호작용한 보스 포탈</param>
    /// <param name="actor">상호작용한 플레이어</param>
    public void OpenNextWorldConfirmation(WorldPortal portal, SkillLoadout actor)
    {
        if (!_bound)
            throw new InvalidOperationException("세션 UI 연결 필요");

        if (_nextWorld == null)
            throw new MissingReferenceException("NextWorldUIController 연결 필요");

        _shop.Close();
        _worldGraph.Close();

        if (_runClear != null)
            _runClear.Close();

        _nextWorld.Open(portal, actor);
    }

    /// <summary>
    /// 기존 화면 종료 후 최종 보스 포탈의 클리어 창 표시
    /// </summary>
    /// <param name="portal">상호작용한 최종 보스 포탈</param>
    /// <param name="actor">상호작용한 플레이어</param>
    public void OpenRunClear(WorldPortal portal, SkillLoadout actor)
    {
        if (!_bound)
            throw new InvalidOperationException("세션 UI 연결 필요");

        if (_runClear == null)
            throw new MissingReferenceException("RunClearUIController 연결 필요");

        _shop.Close();
        _worldGraph.Close();

        if (_nextWorld != null)
            _nextWorld.Close();

        _runClear.Open(portal, actor);
    }
}
