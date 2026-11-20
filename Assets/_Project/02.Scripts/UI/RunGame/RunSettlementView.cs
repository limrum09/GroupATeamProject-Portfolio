using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 씬에 배치된 결산 화면의 값 표시와 로비 복귀 입력 연결
/// </summary>
[DisallowMultipleComponent]
public class RunSettlementView : MonoBehaviour
{
    [Serializable]
    class SkillSlotBinding
    {
        public Image Icon;
        public SkillIconHover Hover;
    }

    [Header("----- 종료 시점 플레이어 -----")]
    [SerializeField] Image _playerImage;
    [SerializeField] TMP_Text _characterLabel;
    [SerializeField] LobbyCharacterDefinition[] _characters;
    [SerializeField] TMP_Text _playerLevel;
    [SerializeField] SkillSlotBinding[] _skillSlots;
    [SerializeField] SkillTooltipView _skillTooltip;
    [SerializeField] Image _difficultyIcon;
    [SerializeField] TMP_Text _difficultyMultiplier;
    [SerializeField] Sprite[] _difficultySprites;

    [Header("----- 결과 화면 -----")]
    [SerializeField] Image _resultFrame;
    [SerializeField] Image _screenTint;
    [SerializeField] TMP_Text _resultTitle;
    [SerializeField] Sprite _victoryFrame;
    [SerializeField] Sprite _defeatFrame;
    [SerializeField] Color _victoryTitleColor;
    [SerializeField] Color _defeatTitleColor;
    [SerializeField] Color _victoryScreenTint;
    [SerializeField] Color _defeatScreenTint;
    [SerializeField] TMP_Text _totalScore;
    [SerializeField] RectTransform _runHud;
    [Tooltip("미니맵이 없는 테스트 씬에서는 생략 가능")]
    [SerializeField] RectTransform _minimap;

    [Header("----- 세션 스탯 -----")]
    [SerializeField] TMP_Text _maxHealth;
    [SerializeField] TMP_Text _defense;
    [SerializeField] TMP_Text _attackPower;
    [SerializeField] TMP_Text _spellPower;
    [SerializeField] TMP_Text _attackSpeed;
    [SerializeField] TMP_Text _criticalChance;
    [SerializeField] TMP_Text _criticalDamage;

    [Header("----- 주요 기록 -----")]
    [SerializeField] TMP_Text _keyKills;
    [SerializeField] TMP_Text _keyDamageTaken;
    [SerializeField] TMP_Text _keyDamageDealt;
    [SerializeField] TMP_Text _keyMaxSingleDamage;

    [Header("----- 결산 통계 -----")]
    [SerializeField] TMP_Text _normalCount;
    [SerializeField] TMP_Text _epicCount;
    [SerializeField] TMP_Text _midBossCount;
    [SerializeField] TMP_Text _bossCount;
    [SerializeField] TMP_Text _normalScore;
    [SerializeField] TMP_Text _epicScore;
    [SerializeField] TMP_Text _midBossScore;
    [SerializeField] TMP_Text _bossScore;
    [SerializeField] TMP_Text _damageDealt;
    [SerializeField] TMP_Text _maxSingleDamage;
    [SerializeField] TMP_Text _damageTaken;
    [SerializeField] TMP_Text _sessionGold;
    [SerializeField] TMP_Text _visitedNodes;
    [SerializeField] TMP_Text _visitedWorlds;
    [SerializeField] TMP_Text _finalScore;
    [SerializeField] TMP_Text _metaGold;
    [SerializeField] Button _returnButton;

    Action _requestReturn;
    RunSettlementResult _result;

    void OnEnable()
    {
        _returnButton.onClick.AddListener(HandleReturn);
        foreach (SkillSlotBinding slot in _skillSlots)
            slot.Hover.OnHoverChanged += HandleSkillHover;
    }

    void OnDisable()
    {
        _returnButton.onClick.RemoveListener(HandleReturn);
        foreach (SkillSlotBinding slot in _skillSlots)
            slot.Hover.OnHoverChanged -= HandleSkillHover;

        _skillTooltip.Hide();
        _result = null;
        _requestReturn = null;
    }

    void HandleSkillHover(SkillIconHover source, bool hovered)
    {
        if (hovered && _result != null)
        {
            foreach (var skill in _result.Skills)
            {
                if (_skillSlots[skill.Slot].Hover != source)
                    continue;

                _skillTooltip.Show(SkillDescriptionFormatter.FormatTitle(skill.Definition, skill.Level), skill.Level,
                    SkillDescriptionFormatter.FormatDescription(skill.Definition, skill.Level));
                return;
            }
        }

        _skillTooltip.Hide();
    }

    void HandleReturn()
    {
        Action requestReturn = _requestReturn;
        if (requestReturn == null)
            return;

        _requestReturn = null;
        _returnButton.interactable = false;
        requestReturn();
    }

    static void ShowRank(RunSettlementResult result, EnemyRankType rank, TMP_Text countLabel, TMP_Text scoreLabel)
    {
        result.KillsByRank.TryGetValue(rank, out int count);
        result.ScoreByRank.TryGetValue(rank, out decimal score);
        countLabel.text = $"{count:N0}마리";
        scoreLabel.text = $"{score:#,0.##}점";
    }

    void ShowProfile(RunSettlementResult result)
    {
        PlayerDefinition definition = result.PlayerDefinition;
        _playerImage.sprite = definition.Icon;
        _playerLevel.text = result.PlayerLevel.ToString();
        _characterLabel.text = string.Empty;

        foreach (LobbyCharacterDefinition character in _characters)
        {
            if (character.GrowthDefinition.CharacterDefinition != definition)
                continue;

            _characterLabel.text = string.IsNullOrEmpty(character.CharacterTypeName)
                ? character.DisplayName
                : $"{character.DisplayName} / {character.CharacterTypeName}";
            break;
        }
    }

    void ShowStats(CharacterStatValues stats)
    {
        _maxHealth.text = $"{stats.MaxHealth:#,0.##}";
        _defense.text = $"{stats.Defense:#,0.##}";
        _attackPower.text = $"{stats.AttackPower:#,0.##}";
        _spellPower.text = $"{stats.SpellPower:#,0.##}";
        _attackSpeed.text = $"{stats.AttackSpeed:#,0.##}";
        _criticalChance.text = $"{stats.CriticalChance:0.##%}";
        _criticalDamage.text = $"{stats.CriticalDamageMultiplier:0.##%}";
    }

    void ShowSummary(RunSettlementResult result)
    {
        bool cleared = result.EndReason == RunEndReason.Cleared;
        _resultFrame.sprite = cleared ? _victoryFrame : _defeatFrame;
        _screenTint.color = cleared ? _victoryScreenTint : _defeatScreenTint;
        _resultTitle.text = cleared ? "승리" : "실패";
        _resultTitle.color = cleared ? _victoryTitleColor : _defeatTitleColor;
        string totalScore = decimal.Truncate(result.FinalScore).ToString("N0", CultureInfo.InvariantCulture);
        _totalScore.text = totalScore;
        _finalScore.text = $"총 점수 {totalScore}점";

        int totalKills = 0;
        foreach (int count in result.KillsByRank.Values)
            totalKills += count;

        _keyKills.text = $"{totalKills:N0}";
        _keyDamageTaken.text = _damageTaken.text;
        _keyDamageDealt.text = _damageDealt.text;
        _keyMaxSingleDamage.text = _maxSingleDamage.text;
    }

    /// <summary>
    /// 종료 결과 표시와 로비 복귀 요청 연결
    /// </summary>
    /// <param name="result">확정된 런 결산</param>
    /// <param name="requestReturn">로비 씬 전환 요청</param>
    public void Show(RunSettlementResult result, Action requestReturn)
    {
        if (_runHud == null)
            throw new MissingReferenceException($"{name}: RunHUD 연결 필요");

        _result = result;
        ShowProfile(result);
        ShowStats(result.PlayerStats);
        _difficultyIcon.sprite = _difficultySprites[(int)result.SelectedDifficulty];
        _difficultyMultiplier.text = $"점수 ×{result.AppliedMultiplier:0.0}";

        foreach (SkillSlotBinding slot in _skillSlots)
        {
            slot.Icon.sprite = null;
            slot.Icon.enabled = false;
        }

        foreach (var skill in result.Skills)
        {
            Image icon = _skillSlots[skill.Slot].Icon;
            icon.sprite = skill.Definition.SkillIcon;
            icon.enabled = true;
        }

        ShowRank(result, EnemyRankType.Normal, _normalCount, _normalScore);
        ShowRank(result, EnemyRankType.Epic, _epicCount, _epicScore);
        ShowRank(result, EnemyRankType.MidBoss, _midBossCount, _midBossScore);
        ShowRank(result, EnemyRankType.Boss, _bossCount, _bossScore);
        _damageDealt.text = $"{result.DamageDealt:#,0.##}";
        _maxSingleDamage.text = $"{result.MaxSingleDamage:#,0.##}";
        _damageTaken.text = $"{result.DamageTaken:#,0.##}";
        _sessionGold.text = $"{result.SessionGoldEarned:#,0.##}";
        _visitedNodes.text = $"{result.VisitedNodeCount:N0}";
        _visitedWorlds.text = $"{result.VisitedWorldCount:N0}";
        _metaGold.text = $"{result.MetaGold:N0}";
        ShowSummary(result);

        _requestReturn = requestReturn;
        _returnButton.interactable = true;
        _runHud.gameObject.SetActive(false);
        if (_minimap != null)
            _minimap.gameObject.SetActive(false);

        gameObject.SetActive(true);
    }
}
