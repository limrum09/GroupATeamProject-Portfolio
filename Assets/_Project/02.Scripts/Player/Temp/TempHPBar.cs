using UnityEngine;
using UnityEngine.Serialization;

/// <summary>체력 비율에 따른 스프라이트 체력바 표시</summary>
public class TempHPBar : MonoBehaviour
{
    [FormerlySerializedAs("status")]
    [SerializeField] CharacterStatus _status;
    [SerializeField] SpriteRenderer _currentHPBar;

    Vector3 _fullScale;
    bool _started;

    void Awake()
    {
        if (_status == null || _currentHPBar == null)
            throw new MissingReferenceException($"{name}: CharacterStatus와 체력바 SpriteRenderer 연결이 필요합니다.");

        // 체력에 따른 크기 변경 전에 원래 크기 보관
        _fullScale = _currentHPBar.transform.localScale;
    }

    void OnEnable()
    {
        _status.OnHealthChanged += OnHealthChanged;

        // 재활성화 시 비활성 기간의 체력 변화 반영
        if (_started)
            OnHealthChanged(_status.CurrentHealth, _status.MaxHealth);
    }

    void Start()
    {
        // CharacterStatus의 Awake 초기화 이후 첫 표시
        _started = true;
        OnHealthChanged(_status.CurrentHealth, _status.MaxHealth);
    }

    void OnDisable()
    {
        if (_status != null)
            _status.OnHealthChanged -= OnHealthChanged;
    }

    /// <summary>현재 체력 비율을 체력바 크기에 반영</summary>
    /// <param name="currentHP">현재 체력</param>
    /// <param name="maxHP">최대 체력</param>
    public void OnHealthChanged(float currentHP, float maxHP)
    {
        float ratio = Mathf.Clamp01(currentHP / maxHP);

        Vector3 scale = _fullScale;
        scale.x *= ratio;

        _currentHPBar.transform.localScale = scale;
    }
}