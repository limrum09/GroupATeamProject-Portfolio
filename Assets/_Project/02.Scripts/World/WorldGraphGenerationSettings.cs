using System;
using UnityEngine;

/// <summary>
/// 삼각 격자 그래프의 경로·노드 수·연결 및 표시 설정
/// </summary>
[Serializable]
public class WorldGraphGenerationSettings
{
    [Header("----- 경로 및 노드 수 -----")]
    [SerializeField, Min(2)] int _bossDistance = 3;
    [SerializeField, Min(1)] int _minimumAdditionalNodes = 1;
    [SerializeField, Min(1)] int _maximumAdditionalNodes = 3;
    [SerializeField, Min(1)] int _branchRowPadding = 2;

    [Header("----- 연결 규칙 -----")]
    [SerializeField, Range(2, 6)] int _maximumConnections = 4;
    [SerializeField, Range(0f, 1f)] float _additionalConnectionChance = 0.25f;
    [SerializeField, Min(1)] int _minimumLeafNodes = 1;
    [SerializeField, Min(1)] int _maximumLeafNodes = 3;
    [SerializeField, Min(0)] int _minimumTwoConnectionNodes = 0;
    [SerializeField, Min(0)] int _maximumTwoConnectionNodes = 3;

    [Header("----- 생성 및 표시 설정 -----")]
    [SerializeField, Range(0f, 0.15f)] float _positionJitter = 0.08f;
    [SerializeField, Min(1)] int _maximumAttempts = 32;

    public int BossDistance => _bossDistance;
    public int MinimumAdditionalNodes => _minimumAdditionalNodes;
    public int MaximumAdditionalNodes => _maximumAdditionalNodes;
    public int BranchRowPadding => _branchRowPadding;
    public int MaximumConnections => _maximumConnections;
    public float AdditionalConnectionChance => _additionalConnectionChance;
    public int MinimumLeafNodes => _minimumLeafNodes;
    public int MaximumLeafNodes => _maximumLeafNodes;
    public int MinimumTwoConnectionNodes => _minimumTwoConnectionNodes;
    public int MaximumTwoConnectionNodes => _maximumTwoConnectionNodes;
    public float PositionJitter => _positionJitter;
    public int MaximumAttempts => _maximumAttempts;

    /// <summary>
    /// 기본 생성 설정 구성
    /// </summary>
    public WorldGraphGenerationSettings()
    {
    }

    /// <summary>
    /// 생성 규모와 연결 수별 노드 개수 범위 구성
    /// </summary>
    /// <param name="bossDistance">보스까지의 최단 이동 횟수</param>
    /// <param name="minimumAdditionalNodes">추가 노드 수 하한</param>
    /// <param name="maximumAdditionalNodes">추가 노드 수 상한</param>
    /// <param name="minimumLeafNodes">연결 1개 노드 수 하한</param>
    /// <param name="maximumLeafNodes">연결 1개 노드 수 상한</param>
    /// <param name="minimumTwoConnectionNodes">연결 2개 노드 수 하한</param>
    /// <param name="maximumTwoConnectionNodes">연결 2개 노드 수 상한</param>
    public WorldGraphGenerationSettings(int bossDistance, int minimumAdditionalNodes,
        int maximumAdditionalNodes, int minimumLeafNodes = 1, int maximumLeafNodes = 3,
        int minimumTwoConnectionNodes = 0, int maximumTwoConnectionNodes = 3)
    {
        _bossDistance = bossDistance;
        _minimumAdditionalNodes = minimumAdditionalNodes;
        _maximumAdditionalNodes = maximumAdditionalNodes;
        _minimumLeafNodes = minimumLeafNodes;
        _maximumLeafNodes = maximumLeafNodes;
        _minimumTwoConnectionNodes = minimumTwoConnectionNodes;
        _maximumTwoConnectionNodes = maximumTwoConnectionNodes;
    }

    /// <summary>
    /// 생성 범위와 확률 및 명백한 개수 충돌 검사
    /// </summary>
    public void Validate()
    {
        if (_bossDistance < 2 || _minimumAdditionalNodes < 1 ||
            _maximumAdditionalNodes < _minimumAdditionalNodes || _branchRowPadding < 1 ||
            _maximumConnections < 2 || _maximumConnections > 6 || _maximumAttempts < 1 ||
            _minimumLeafNodes < 1 || _maximumLeafNodes < _minimumLeafNodes ||
            _minimumTwoConnectionNodes < 0 || _maximumTwoConnectionNodes < _minimumTwoConnectionNodes)
        {
            throw new InvalidOperationException("유효한 노드 수·연결 수 범위와 생성 횟수 필요");
        }

        if (float.IsNaN(_additionalConnectionChance) || float.IsInfinity(_additionalConnectionChance) ||
            _additionalConnectionChance < 0f || _additionalConnectionChance > 1f ||
            float.IsNaN(_positionJitter) || float.IsInfinity(_positionJitter) ||
            _positionJitter < 0f || _positionJitter > 0.15f)
        {
            throw new InvalidOperationException("유효한 연결 확률과 위치 보정 범위 필요");
        }

        checked
        {
            int maximumNodeCount = _bossDistance + 1 + _maximumAdditionalNodes;
            if ((long)_minimumLeafNodes + _minimumTwoConnectionNodes > maximumNodeCount)
                throw new InvalidOperationException("연결 수별 최소 노드 합계가 전체 최대 노드 수 초과");

            _ = _bossDistance + _branchRowPadding + 1;
        }
    }
}
