using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>
/// 기존 Fill Image의 스프라이트를 유지하는 오각형 능력치 표시와 전환
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class PentagonStatGraphic : BaseMeshEffect
{
    const int AxisCount = 5;

    [Header("----- 값 변경 연출 -----")]
    [SerializeField, Min(0f)] float _duration = 0.5f;

    // 초기 표시 전에는 기존 스프라이트의 전체 크기 유지
    readonly float[] _values = { 1f, 1f, 1f, 1f, 1f };
    readonly float[] _targetValues = { 1f, 1f, 1f, 1f, 1f };
    readonly float[] _startValues = new float[AxisCount];

    float _elapsed;
    bool _isAnimating;

    void Update()
    {
        if (!Application.isPlaying || !_isAnimating)
            return;

        _elapsed += Time.unscaledDeltaTime;

        float progress = _duration == 0f ? 1f : Mathf.Clamp01(_elapsed / _duration);

        if (progress >= 1f)
        {
            ApplyTargetImmediately();
            return;
        }

        float weight = Mathf.SmoothStep(0f, 1f, progress);

        for (int i = 0; i < AxisCount; i++)
        {
            _values[i] = Mathf.Lerp(_startValues[i], _targetValues[i], weight);
        }

        graphic.SetVerticesDirty();
    }

    void ValidateDuration()
    {
        if (float.IsNaN(_duration) || float.IsInfinity(_duration) || _duration < 0f)
            throw new InvalidOperationException($"{name}: 전환 시간은 유한한 0 이상의 값 필요");
    }

    static float NormalizeRatio(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(parameterName, "유한한 능력치 비율 필요");

        return Mathf.Clamp01(value);
    }

    void ApplyTargetImmediately()
    {
        for (int i = 0; i < AxisCount; i++)
            _values[i] = _targetValues[i];

        _elapsed = 0f;
        _isAnimating = false;

        graphic.SetVerticesDirty();
    }

    static Vector4 GetUv(Vector3 position, Rect rect, Vector4 outerUv)
    {
        float horizontal = (position.x - rect.xMin) / rect.width;
        float vertical = (position.y - rect.yMin) / rect.height;

        return new Vector4(Mathf.Lerp(outerUv.x, outerUv.z, horizontal), Mathf.Lerp(outerUv.y, outerUv.w, vertical), 0f, 0f);
    }

    /// <summary>
    /// 화면 비활성화 시 진행 중인 전환을 마지막 목표로 확정
    /// </summary>
    protected override void OnDisable()
    {
        if (_isAnimating)
        {
            for (int i = 0; i < AxisCount; i++)
                _values[i] = _targetValues[i];

            _elapsed = 0f;
            _isAnimating = false;
        }

        base.OnDisable();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        ValidateDuration();
    }
#endif

    /// <summary>
    /// 기존 Image의 메시를 현재 표시 비율에 맞는 오각형으로 교체
    /// </summary>
    /// <param name="vertexHelper">기존 Image의 메시 수정 대상</param>
    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount == 0)
            return;

        Image image = (Image)graphic;

        Sprite sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;

        if (sprite == null)
            throw new MissingReferenceException($"{name}: 기존 Fill Image의 스프라이트 연결 필요");

        Rect rect = image.GetPixelAdjustedRect();

        if (rect.width <= 0f || rect.height <= 0f)
            return;

        Vector4 outerUv = DataUtility.GetOuterUV(sprite);

        Color32 vertexColor = image.color;

        float horizontalRadius = rect.width / (2f * Mathf.Sin(72f * Mathf.Deg2Rad));
        float verticalRadius = rect.height / (1f + Mathf.Cos(36f * Mathf.Deg2Rad));

        Vector3 center = new Vector3(rect.center.x, rect.yMax - verticalRadius, 0f);

        // 기존 Image의 사각 메시를 오각형으로 교체
        vertexHelper.Clear();

        vertexHelper.AddVert(center, vertexColor, GetUv(center, rect, outerUv));

        // 위쪽부터 공격력, 주문력, 공격속도, 체력, 방어
        for (int i = 0; i < AxisCount; i++)
        {
            float radians = (90f - i * 72f) * Mathf.Deg2Rad;

            Vector3 offset = new Vector3(Mathf.Cos(radians) * horizontalRadius, Mathf.Sin(radians) * verticalRadius, 0f);
            Vector3 originalPosition = center + offset;
            Vector3 changedPosition = center + offset * _values[i];

            // 기존 스프라이트의 UV를 유지하며 정점 위치만 변경
            vertexHelper.AddVert(changedPosition, vertexColor, GetUv(originalPosition, rect, outerUv));
        }

        for (int i = 0; i < AxisCount; i++)
        {
            int current = i + 1;
            int next = (i + 1) % AxisCount + 1;

            vertexHelper.AddTriangle(0, current, next);
        }
    }

    /// <summary>
    /// 다섯 능력치 비율을 새 목표로 등록하고 현재 표시 모양에서 전환
    /// 비활성 상태와 에디터 또는 전환 시간 0에서는 즉시 반영
    /// </summary>
    /// <param name="attack">공격력 비율</param>
    /// <param name="spell">주문력 비율</param>
    /// <param name="attackSpeed">공격속도 비율</param>
    /// <param name="health">최대 체력 비율</param>
    /// <param name="defense">방어력 비율</param>
    public void SetValues(float attack, float spell, float attackSpeed, float health, float defense)
    {
        ValidateDuration();

        attack = NormalizeRatio(attack, nameof(attack));
        spell = NormalizeRatio(spell, nameof(spell));
        attackSpeed = NormalizeRatio(attackSpeed, nameof(attackSpeed));
        health = NormalizeRatio(health, nameof(health));
        defense = NormalizeRatio(defense, nameof(defense));

        bool immediate = !Application.isPlaying || !IsActive() || _duration == 0f;
        bool changed = _targetValues[0] != attack || _targetValues[1] != spell || _targetValues[2] != attackSpeed || _targetValues[3] != health || _targetValues[4] != defense;

        // 같은 목표의 반복 요청으로 전환이 재시작되는 상황 방지
        if (!changed)
        {
            if (immediate)
                ApplyTargetImmediately();

            return;
        }

        // 전환 도중 새 요청도 현재 보이는 모양에서 시작
        for (int i = 0; i < AxisCount; i++)
            _startValues[i] = _values[i];

        _targetValues[0] = attack;
        _targetValues[1] = spell;
        _targetValues[2] = attackSpeed;
        _targetValues[3] = health;
        _targetValues[4] = defense;

        _elapsed = 0f;

        if (immediate)
        {
            ApplyTargetImmediately();
            return;
        }

        _isAnimating = true;
    }
}