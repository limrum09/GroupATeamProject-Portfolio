using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AttackRangeView : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] PlayerInputReader _input;
    [SerializeField] CombatController _combat;
    [SerializeField] SpriteRenderer _spriteRenderer;

    [Header("----- 표시 설정 -----")]
    [SerializeField] float _heightOffset = 0.05f;

    Vector3[] _points;

    void Awake()
    {
        if (_input == null || _combat == null || _spriteRenderer == null)
            throw new MissingReferenceException(
                $"{name}: 입력, 전투, SpriteRenderer 연결이 필요합니다.");

        if (_spriteRenderer.sprite == null)
            throw new MissingReferenceException($"{name}: 범위 표시용 Sprite를 지정하세요.");

        if (_spriteRenderer.drawMode != SpriteDrawMode.Simple)
            throw new InvalidOperationException($"{name}: SpriteRenderer의 Draw Mode를 Simple로 설정하세요.");

        _spriteRenderer.enabled = false;
    }

    void LateUpdate()
    {
        bool visible = _input.isActiveAndEnabled && _input.AttackModeHeld && _combat.isActiveAndEnabled && _combat.Owner.IsAlive;

        _spriteRenderer.enabled = visible;

        if (!visible)
            return;

        Vector3 center = _combat.Owner.Position;
        center.y += _heightOffset;

        Transform spriteTransform = _spriteRenderer.transform;

        // Sprite의 XY 평면을 바닥의 XZ 평면에 맞춘다.
        spriteTransform.SetPositionAndRotation(center, Quaternion.Euler(90f, 0f, 0f));

        Vector3 spriteSize = _spriteRenderer.sprite.bounds.size;
        float diameter = _combat.AttackRange * 2f;

        spriteTransform.localScale = new Vector3(diameter / spriteSize.x, diameter / spriteSize.y, 1f);
    }

    void OnDisable()
    {
        if (_spriteRenderer != null)
        {
            _spriteRenderer.enabled = false;
        }
    }
}