using UnityEngine;

/// <summary>
/// 종류별 적 행동과 공용 적 객체 연결
/// </summary>
public class EnemyModel : MonoBehaviour
{
    [Header("----- 모델 애니메이션 -----")]
    [SerializeField] Animator _animator;
    [SerializeField] bool _hasEmergence;

    [Header("----- 접촉 판정 -----")]
    [SerializeField] Collider _contactCollider;

    [Header("----- HUD 기준점 -----")]
    [SerializeField] Transform _hudAnchor;

    [Header("----- 대미지 표시 기준점 -----")]
    [SerializeField] Transform _damageAnchor;

    // 이번 외형 및 행동 구성을 소유한 공용 적 객체
    protected Enemy _enemy;

    public EnemyDefinition Definition => _enemy.Definition;
    public CharacterStatus Status => _enemy.Status;
    public Animator Animator => _animator;
    public bool HasEmergence => _hasEmergence;
    public Collider ContactCollider => _contactCollider;
    public Transform HudAnchor => _hudAnchor;
    public Transform DamageAnchor => _damageAnchor;

    /// <summary>
    /// 모델 Animator와 Controller 및 대미지 기준점의 소유 계층 검사
    /// </summary>
    public void ValidateConfiguration()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null ||
            _animator.transform.IsChildOf(transform) == false)
        {
            throw new MissingReferenceException($"{name}: 같은 Content의 모델 Animator와 Controller 연결 필요");
        }

        if (_damageAnchor == null || !_damageAnchor.IsChildOf(transform))
            throw new MissingReferenceException($"{name}: 같은 Content의 DamageAnchor 연결 필요");
    }

    /// <summary>
    /// 행동에 필요한 공용 적 객체 연결
    /// </summary>
    /// <param name="enemy">이번 구성의 소유 적</param>
    public virtual void Init(Enemy enemy)
    {
        ValidateConfiguration();
        _enemy = enemy;
    }
}
