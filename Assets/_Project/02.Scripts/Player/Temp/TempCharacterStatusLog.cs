using UnityEngine;

public class TempCharacterStatusLog : MonoBehaviour
{
    [SerializeField]
    private CharacterStatus status;

    float _currentHp;

    private void Start()
    {
        status.OnHealthChanged += OnHealChanged;
        status.OnDied += OnDead;

        _currentHp = status.MaxHealth;
    }

    private void OnDisable()
    {
        status.OnHealthChanged -= OnHealChanged;
        status.OnDied -= OnDead;
    }

    public void OnHealChanged(float currentHp, float maxHp)
    {
        Debug.Log($"{gameObject.name} - 피해량 : {_currentHp - currentHp}, 현재 체력 : {currentHp}, 최대 체력 : {maxHp}");
        _currentHp = currentHp;
    }

    public void OnDead()
    {
        Debug.Log("죽음!");
    }
}
