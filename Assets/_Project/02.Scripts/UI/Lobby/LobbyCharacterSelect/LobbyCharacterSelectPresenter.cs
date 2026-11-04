using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 선택 입력과 정적 정보 및 숙련도 표시 조율
/// </summary>
public class LobbyCharacterSelectPresenter : IDisposable
{
    readonly LobbyManager _lobby;
    readonly LobbyCharacterSelectView _view;

    bool _active;
    bool _interactionEnabled;
    bool _disposed;

    static float GetStatRatio(float value, float maximum)
    {
        // 모든 등록 캐릭터의 해당 능력치가 0이면 채움도 0
        return maximum > 0f ? value / maximum : 0f;
    }

    void RefreshStatChart(CharacterDefinition selected, IReadOnlyList<LobbyCharacterDefinition> characters)
    {
        float maxAttack = 0f;
        float maxSpell = 0f;
        float maxAttackSpeed = 0f;
        float maxHealth = 0f;
        float maxDefense = 0f;

        foreach (LobbyCharacterDefinition character in characters)
        {
            CharacterDefinition definition = character.GrowthDefinition.CharacterDefinition;

            maxAttack = Mathf.Max(maxAttack, definition.AttackPower);
            maxSpell = Mathf.Max(maxSpell, definition.SpellPower);
            maxAttackSpeed = Mathf.Max(maxAttackSpeed, definition.BaseAttackSpeed);
            maxHealth = Mathf.Max(maxHealth, definition.MaxHealth);
            maxDefense = Mathf.Max(maxDefense, definition.Defense);
        }

        _view.ShowStatChart(GetStatRatio(selected.AttackPower, maxAttack), GetStatRatio(selected.SpellPower, maxSpell), GetStatRatio(selected.BaseAttackSpeed, maxAttackSpeed), GetStatRatio(selected.MaxHealth, maxHealth), GetStatRatio(selected.Defense, maxDefense));
    }

    void RefreshInteraction()
    {
        if (!_active)
            return;

        bool canSelect = _interactionEnabled && _lobby.CanChangeSelection && _lobby.Characters.Count > 1;

        _view.SetInteraction(canSelect, _interactionEnabled);
    }

    void Refresh()
    {
        if (!_active)
            return;

        LobbyCharacterDefinition character = _lobby.SelectedCharacter;

        PlayerDefinition player = (PlayerDefinition)character.GrowthDefinition.CharacterDefinition;

        _view.ShowCharacter(character.Icon, character.DisplayName, character.CharacterDescription, character.CharacterTypeName, character.AttackType, character.ControlDifficulty);

        for (int slot = 0; slot < _view.StartingSkillSlotCount; slot++)
        {
            SkillDefinition skill = player.GetStartingSkill(slot);

            if (skill != null && skill.SkillIcon == null)
                throw new MissingReferenceException($"{skill.name}: 선택 화면에 표시할 스킬 아이콘 필요");

            _view.ShowStartingSkill(slot, skill == null ? null : skill.SkillIcon);
        }

        RefreshStatChart(player, _lobby.Characters);

        if (_lobby.IsMetaReady && !_lobby.IsCharacterTestMode)
        {
            _lobby.GetSelectedCharacterMasteryProgress(out int level, out long currentExperience, out long requiredExperience);

            _view.ShowMastery(level, currentExperience, requiredExperience);
        }
        else
        {
            _view.ShowMasteryUnavailable();
        }

        RefreshInteraction();
    }

    void HandleSelectionRequested(int direction)
    {
        if (!_active || !_interactionEnabled || !_lobby.CanChangeSelection)
            return;

        if (direction != -1 && direction != 1)
            throw new ArgumentOutOfRangeException(nameof(direction), "이전은 -1, 다음은 1 필요");

        IReadOnlyList<LobbyCharacterDefinition> characters = _lobby.Characters;

        if (characters.Count <= 1)
            return;

        int currentIndex = -1;

        for (int i = 0; i < characters.Count; i++)
        {
            if (characters[i] == _lobby.SelectedCharacter)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
            throw new InvalidOperationException("선택 캐릭터가 로비 목록에 포함되어야 함");

        int nextIndex = (currentIndex + direction + characters.Count) % characters.Count;

        // 성공 시 LobbyManager의 변경 알림으로 화면 갱신
        if (!_lobby.TrySelectCharacter(characters[nextIndex].CharacterId))
            Refresh();
    }

    /// <summary>
    /// 캐릭터 상태 조회 대상과 표시 화면 연결
    /// </summary>
    /// <param name="lobby">캐릭터 선택과 숙련도 조회 담당</param>
    /// <param name="view">캐릭터 정보 표시와 버튼 입력 담당</param>
    public LobbyCharacterSelectPresenter(LobbyManager lobby, LobbyCharacterSelectView view)
    {
        if (lobby == null)
            throw new ArgumentNullException(nameof(lobby));

        if (view == null)
            throw new ArgumentNullException(nameof(view));

        _lobby = lobby;
        _view = view;
    }

    /// <summary>
    /// 화면 사용 시작과 상태 변경 및 선택 입력 구독
    /// 카메라 이동 완료 전에는 입력 차단 유지
    /// </summary>
    public void Activate()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LobbyCharacterSelectPresenter));

        if (_active)
            return;

        _active = true;
        _interactionEnabled = false;

        _lobby.OnChanged += Refresh;
        _view.SelectionRequested += HandleSelectionRequested;

        try
        {
            Refresh();
        }
        catch
        {
            // 초기 표시 실패 시 구독만 정리하고 구성 오류 전달
            Deactivate();
            throw;
        }
    }

    /// <summary>
    /// 화면 전환 담당자가 결정한 입력 허용 상태 반영
    /// </summary>
    /// <param name="enabled">현재 화면에서 선택과 닫기를 허용할지 여부</param>
    public void SetInteractionEnabled(bool enabled)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LobbyCharacterSelectPresenter));

        _interactionEnabled = enabled;
        RefreshInteraction();
    }

    /// <summary>
    /// 표시값을 유지한 채 화면 입력과 상태 변경 구독 해제
    /// </summary>
    public void Deactivate()
    {
        if (!_active)
            return;

        _active = false;
        _interactionEnabled = false;

        if (_lobby != null)
            _lobby.OnChanged -= Refresh;

        if (_view != null)
        {
            _view.SelectionRequested -= HandleSelectionRequested;
            _view.SetInteraction(false, false);
        }
    }

    /// <summary>
    /// Presenter 수명 종료와 구독 정리
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Deactivate();
        _disposed = true;
    }
}