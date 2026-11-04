using System;
using UnityEngine;

/// <summary>
/// 캐릭터 선택 화면에 표시할 공격 거리 유형
/// </summary>
public enum LobbyAttackType
{
    Melee,
    Ranged
}

/// <summary>
/// 로비 선택용 캐릭터 표시 정보와 성장 설정 및 더미 프리팹
/// </summary>
[CreateAssetMenu(menuName = "Project/GameFlow/Lobby Character Definition", fileName = "_LobbyCharacterDefinition")]
public sealed class LobbyCharacterDefinition : ScriptableObject
{
    [Header("----- 표시 정보 -----")]
    [SerializeField] string _displayName;
    [SerializeField] string _characterDescription;
    [SerializeField] string _characterTypeName;
    [SerializeField] Sprite _icon;

    [Header("----- 캐릭터 성장 설정 -----")]
    [SerializeField] CharacterGrowthDefinition _growthDefinition;

    [Header("----- 로비 전용 더미 -----")]
    [SerializeField] PlayerAnimation _previewPrefab;

    [Header("----- 캐릭터 스토리 -----")]
    [SerializeField] CharacterStoryDefinition _storyDefinition;

    [Header("----- 공격 유형과 조작 난이도 -----")]
    [SerializeField] LobbyAttackType _attackType;
    [SerializeField, Range(1, MaxControlDifficulty)] int _controlDifficulty = 1;

    public const int MaxControlDifficulty = 6;

    public string CharacterId => _growthDefinition.CharacterId;
    public string DisplayName => _displayName;
    public string CharacterDescription => _characterDescription;
    public Sprite Icon => _icon;
    public string CharacterTypeName => _characterTypeName;
    public int ControlDifficulty => _controlDifficulty;
    public LobbyAttackType AttackType => _attackType;
    public CharacterGrowthDefinition GrowthDefinition => _growthDefinition;
    public PlayerAnimation PreviewPrefab => _previewPrefab;
    public CharacterStoryDefinition StoryDefinition => _storyDefinition;

    /// <summary>
    /// 선택에 필요한 성장 설정과 로비 더미 구성 검사
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(_displayName))
            throw new InvalidOperationException($"{name}: 표시 이름 필요");

        if (_growthDefinition == null)
            throw new MissingReferenceException($"{name}: CharacterGrowthDefinition 연결 필요");

        _growthDefinition.Validate();

        if (!(_growthDefinition.CharacterDefinition is PlayerDefinition))
            throw new InvalidOperationException($"{name}: 플레이어 캐릭터의 성장 설정 필요");

        if (_previewPrefab == null)
            throw new MissingReferenceException($"{name}: 로비 전용 Animator 프리팹 연결 필요");

        if (_previewPrefab.transform.parent != null)
            throw new InvalidOperationException($"{name}: 더미 프리팹 루트의 Animator 연결 필요");

        if (_previewPrefab.PrefabAnimator.runtimeAnimatorController == null)
            throw new MissingReferenceException($"{name}: 더미 Animator Controller 연결 필요");

        if (!_previewPrefab.gameObject.activeSelf || !_previewPrefab.enabled)
            throw new InvalidOperationException($"{name}: 활성 더미 프리팹과 Animator 필요");

        if (!Enum.IsDefined(typeof(LobbyAttackType), _attackType))
            throw new InvalidOperationException($"{name}: 올바른 공격 유형 설정 필요");

        if (_controlDifficulty < 1 || _controlDifficulty > MaxControlDifficulty)
            throw new InvalidOperationException($"{name}: 조작 난이도는 1부터 {MaxControlDifficulty} 사이로 설정 필요");
    }
}