using System;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 한 스플라인에 배정할 반딧불과 이동 범위 설정
/// </summary>
[Serializable]
public class FireflyPathSettings
{
    [SerializeField, InspectorName("스플라인 연결")] SplineContainer _container;
    [SerializeField, InspectorName("스플라인 인덱스")] int _splineIndex;
    [Tooltip("0은 해당 경로의 생성 제외. 전체 개체 수는 모든 경로의 합계")]
    [SerializeField, InspectorName("개체 수")] int _count = 20;
    [Tooltip("열린 경로에서는 최초 이동 방향")]
    [SerializeField, InspectorName("역방향 시작")] bool _reverse;
    [SerializeField, InspectorName("기본 속도 (월드 단위/초)")] float _speed = 0.2f;
    [Tooltip("동일 경로 이웃의 경로상 거리. 흔들림을 포함한 직선 거리가 아님")]
    [SerializeField, InspectorName("최소 간격")] float _minSpacing = 0.3f;
    [SerializeField, InspectorName("최대 간격")] float _maxSpacing = 1f;
    [SerializeField, InspectorName("수평 흔들림 반경")] float _horizontalSway = 0.12f;
    [SerializeField, InspectorName("수직 흔들림 높이")] float _verticalSway = 0.1f;
    [SerializeField, InspectorName("흔들림 주기 (초)")] float _swayPeriod = 4f;
    [SerializeField, InspectorName("왕복 감속 기준 시간 (초)")] float _turnDuration = 1f;

    public SplineContainer Container => _container;
    public int SplineIndex => _splineIndex;
    public int Count => _count;
    public bool Reverse => _reverse;
    public float Speed => _speed;
    public float MinSpacing => _minSpacing;
    public float MaxSpacing => _maxSpacing;
    public float HorizontalSway => _horizontalSway;
    public float VerticalSway => _verticalSway;
    public float SwayPeriod => _swayPeriod;
    public float TurnDuration => _turnDuration;
}
