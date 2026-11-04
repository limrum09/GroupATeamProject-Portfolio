using System;
using UnityEngine;

/// <summary>
/// 맵에 직렬화되는 구역별 웨이브 설정
/// </summary>
[Serializable]
public class CombatAreaSpawnData
{
    [Header("----- 웨이브 -----")]
    [SerializeField] WaveSpawnData[] _waveSpawnDatas = new WaveSpawnData[0];

    [Header("----- 추가 웨이브 -----")]
    [SerializeField] WaveSpawnData[] _addWaveSpawnData = new WaveSpawnData[0];

    [SerializeField] string _encounterId;

    public string EncounterId => _encounterId;
    public WaveSpawnData[] WaveSpawnDatas => _waveSpawnDatas;
    public WaveSpawnData[] AdditionalWaves => _addWaveSpawnData;

#if UNITY_EDITOR
    /// <summary>
    /// 편집 도구에서 소환 범위에 대응할 빈 웨이브 추가
    /// </summary>
    public void AppendWaveForEditor()
    {
        if (_waveSpawnDatas == null)
            throw new InvalidOperationException("웨이브 목록 구성 필요");

        int index = _waveSpawnDatas.Length;
        Array.Resize(ref _waveSpawnDatas, index + 1);
        _waveSpawnDatas[index] = new WaveSpawnData();
    }
#endif
}

/// <summary>
/// 웨이브의 소환 배치 조건
/// </summary>
[Serializable]
public class WaveSpawnData
{
    [Header("----- 배치 조건 -----")]
    [SerializeField, Min(0f)] float _minimumPlayerDistance = 5f;

    public float MinimumPlayerDistance => _minimumPlayerDistance;

    /// <summary>
    /// 플레이어 최소 이격 거리 검사
    /// </summary>
    public void Validate()
    {
        if (float.IsNaN(_minimumPlayerDistance) ||
            float.IsInfinity(_minimumPlayerDistance) || _minimumPlayerDistance < 0f)
        {
            throw new ArgumentOutOfRangeException("minimumPlayerDistance", "유한한 0 이상의 최소 거리 필요");
        }
    }
}
