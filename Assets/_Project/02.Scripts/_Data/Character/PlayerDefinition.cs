using System;
using UnityEngine;

/// <summary>
/// 플레이어 캐릭터의 직업
/// </summary>
public enum PlayerClass
{
    Knight = 0,
    Wizard = 1
}

/// <summary>
/// 플레이어의 공통 능력치와 고정 이동 및 시작 슬롯 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Player Definition", fileName = "_PlayerDefinition")]
public sealed class PlayerDefinition : CharacterDefinition
{
    [Header("----- 런 정보 -----")]
    [SerializeField] PlayerClass _playerClass;
    [SerializeField] PlayerAnimation _runVisualPrefab;
    [SerializeField] Sprite _icon;

    [Header("----- 고정 이동 스킬 -----")]
    [SerializeField] MovementSkillDefinition _movementSkill;

    [Header("----- 시작 QWER 스킬 -----")]
    [SerializeField] SkillDefinition _startingQSkill;
    [SerializeField] SkillDefinition _startingWSkill;
    [SerializeField] SkillDefinition _startingESkill;
    [SerializeField] SkillDefinition _startingRSkill;

    public PlayerClass Class => _playerClass;
    public MovementSkillDefinition MovementSkill => _movementSkill;
    public Sprite Icon => _icon;    

    /// <summary>
    /// 런에 생성할 캐릭터 외형 프리팹 검사와 조회
    /// </summary>
    /// <returns>루트에 PlayerAnimation이 연결된 외형 프리팹</returns>
    public PlayerAnimation GetRunVisualPrefab()
    {
        if (_runVisualPrefab == null)
            throw new MissingReferenceException($"{name}: 런 외형 프리팹 연결 필요");

        if (_runVisualPrefab.gameObject.scene.IsValid() || _runVisualPrefab.transform.parent != null)
            throw new InvalidOperationException($"{name}: 씬 인스턴스가 아닌 외형 프리팹 루트 연결 필요");

        if (!_runVisualPrefab.gameObject.activeSelf || !_runVisualPrefab.enabled)
            throw new InvalidOperationException($"{name}: 외형 프리팹과 PlayerAnimation의 활성 설정 필요");

        return _runVisualPrefab;
    }

    /// <summary>
    /// 지정 슬롯의 시작 스킬 조회
    /// </summary>
    /// <param name="slot">Q부터 R까지의 슬롯 번호, 0부터 3</param>
    /// <returns>시작 스킬 정의, 미지정 슬롯은 null</returns>
    public SkillDefinition GetStartingSkill(int slot)
    {
        switch (slot)
        {
            case 0:
                return _startingQSkill;
            case 1:
                return _startingWSkill;
            case 2:
                return _startingESkill;
            case 3:
                return _startingRSkill;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), "0부터 3 사이의 슬롯 번호 필요");
        }
    }
}