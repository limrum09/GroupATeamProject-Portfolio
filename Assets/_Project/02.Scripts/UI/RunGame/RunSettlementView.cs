using System;
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
    [SerializeField] TMP_Text _playerLevel;
    [SerializeField] SkillSlotBinding[] _skillSlots;
    [SerializeField] SkillTooltipView _skillTooltip;
    [SerializeField] Image _difficultyIcon;
    [SerializeField] TMP_Text _difficultyMultiplier;
    [SerializeField] Sprite[] _difficultySprites;

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

                _skillTooltip.Show(skill.Definition.SkillName, skill.Level, skill.Definition.SkillDescription);
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

    /// <summary>
    /// 종료 결과 표시와 로비 복귀 요청 연결
    /// </summary>
    /// <param name="result">확정된 런 결산</param>
    /// <param name="requestReturn">로비 씬 전환 요청</param>
    public void Show(RunSettlementResult result, Action requestReturn)
    {
        _result = result;
        _playerLevel.text = result.PlayerLevel.ToString();
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
        _finalScore.text = $"총 점수 {result.FinalScore:#,0.##}점";
        _metaGold.text = $"{result.MetaGold:N0}";

        _requestReturn = requestReturn;
        _returnButton.interactable = true;
        gameObject.SetActive(true);
    }
}
