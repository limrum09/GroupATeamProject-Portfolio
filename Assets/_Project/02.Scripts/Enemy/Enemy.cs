using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 공용 뼈대(적 객체의 데이터와 외형 및 행동 구성)
/// </summary>
public class Enemy : MonoBehaviour
{
    [Header("----- AI -----")]
    [SerializeField] EnemyAI _ai;

    [Header("----- 전투 상태 -----")]
    [SerializeField] CharacterStatus _status;	// 체력과 전투 상태 및 사망 판정을 소유한 Combat 구성 요소

    [Header("----- 전투 이펙트 -----")]
    [SerializeField] CombatEffectPresenter _effectPresenter;

    [Header("----- 전투 대상 -----")]
    [SerializeField] CombatTarget _combatTarget;

    // 이번 소환에 연결한 정적 적 데이터
    EnemyDefinition _definition;
    EnemyDifficultyDefinition _difficultyDefinition;

    // 이번 소환에서 생성한 외형 및 행동 객체
    GameObject _content;
    Transform _hudAnchor;
    Transform _damageAnchor;

    public event Action OnDeathAnimationEnded
    {
        add => _ai.OnDeathAnimationEnded += value;
        remove => _ai.OnDeathAnimationEnded -= value;
    }

    public CombatTeam Team => CombatTeam.Enemy;
    public CharacterStatus Status => _status;
    public EnemyDefinition Definition => _definition;
    public EnemyDifficultyDefinition DifficultyDefinition => _difficultyDefinition;
    public Transform HudAnchor => _hudAnchor;
    public CombatTarget CombatTarget => _combatTarget;
    public Transform DamageAnchor => _damageAnchor;

    void Awake()
    {
        if (_status == null || _ai == null || _effectPresenter == null)
        {
            throw new MissingReferenceException(
                $"{name}: CharacterStatus, EnemyAI, CombatEffectPresenter가 필요합니다.");
        }

        ValidateCombatTarget();
    }

    void ValidateCombatTarget()
    {
        if (_combatTarget == null)
            throw new MissingReferenceException($"{name}: CombatTarget 연결 필요");

        if (_combatTarget.Status != _status || _combatTarget.ActorTransform != transform ||
            _combatTarget.Team != CombatTeam.Enemy || !_combatTarget.transform.IsChildOf(transform))
        {
            throw new InvalidOperationException(
                $"{name}: 같은 적의 Status와 루트 및 Enemy 진영을 사용하는 CombatTarget 필요");
        }
    }

    /// <summary>
    /// 비활성 적 객체의 데이터와 외형 및 행동 구성
    /// </summary>
    /// <param name="definition">이번 소환에 사용할 적 데이터</param>
    /// <param name="difficultyDefinition">이번 런의 최종 적 설정</param>
    /// <param name="playerTarget">감지하고 공격할 플레이어</param>
    /// <param name="effectPlayer">같은 씬의 전투 이펙트 재생기</param>
    public void Init(
        EnemyDefinition definition,
        EnemyDifficultyDefinition difficultyDefinition,
        CombatTarget playerTarget,
        CombatEffect effectPlayer)
    {
        if (gameObject.activeInHierarchy)
        {
            throw new InvalidOperationException(
                $"{name}: 비활성 상태에서 적 초기화가 필요합니다.");
        }

        if (definition == null || definition.Prefab == null || playerTarget == null ||
            effectPlayer == null || _ai == null || _status == null || _effectPresenter == null)
        {
            throw new MissingReferenceException(
                $"{name}: 적 데이터와 프리팹, 플레이어 CombatTarget, CombatEffect, EnemyAI, CharacterStatus, CombatEffectPresenter가 필요합니다.");
        }

        ValidateCombatTarget();

        if (difficultyDefinition == null)
            throw new ArgumentNullException(nameof(difficultyDefinition));

        if (definition.EnemyId != difficultyDefinition.EnemyId)
            throw new InvalidOperationException("적 정의와 난이도 설정의 ID 불일치");

        // 최초 활성화의 Presenter.Awake 이전에 씬 재생기 연결
        _effectPresenter.BindEffectPlayer(effectPlayer);

        _definition = definition;
        // 풀 재대여 시 기존 체력과 종류별 기본 스탯 초기화
        _difficultyDefinition = difficultyDefinition;
        _status.InitializeActor(definition, difficultyDefinition.Stats);
        _content = Instantiate(definition.Prefab, transform);

        // 종류별 프리팹 루트의 필수 행동 구성 요소
        EnemyModel model = _content.GetComponent<EnemyModel>();

        if (model == null)
        {
            throw new MissingReferenceException(
                $"{definition.name}: 프리팹 루트에 EnemyModel이 필요합니다.");
        }

        model.Init(this);
        _hudAnchor = model.HudAnchor;
        _damageAnchor = model.DamageAnchor;

        _ai.Init(playerTarget, model, effectPlayer);
    }

    /// <summary>
    /// 초기화 완료 여부와 관계없는 소환 구성 해제
    /// </summary>
    public void Clear()
    {
        List<Exception> errors = new List<Exception>();
        GameObject content = _content;

        // 반복 정리와 정리 중 콜백에 대비한 소유 참조 선해제
        _content = null;
        _definition = null;
        _difficultyDefinition = null;
        _hudAnchor = null;
        _damageAnchor = null;

        try
        {
            _ai.Clear();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        try
        {
            _effectPresenter.UnbindEffectPlayer();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        // 콘텐츠 생성 이전의 초기화 실패도 허용
        if (content != null)
        {
            try
            {
                content.SetActive(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            finally
            {
                Destroy(content);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException(
                $"{name}: 적 구성 해제 실패",
                errors);
        }
    }
}
