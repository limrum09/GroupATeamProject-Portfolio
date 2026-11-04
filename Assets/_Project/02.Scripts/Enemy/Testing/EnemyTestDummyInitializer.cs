using System;
using UnityEngine;

/// <summary>
/// 비활성 테스트 타겟의 전투 상태 초기화와 활성화
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class EnemyTestDummyInitializer : MonoBehaviour
{
    [Header("----- 더미 연결 -----")]
    [SerializeField] CharacterStatus _status;
    [SerializeField] CharacterDefinition _definition;

    void Awake()
    {
        if (_status == null || _definition == null)
            throw new MissingReferenceException($"{name}: 더미 CharacterStatus와 Definition 연결 필요");

        if (_status.gameObject.activeSelf)
            throw new InvalidOperationException($"{name}: 초기화 전 더미는 비활성 상태로 저장 필요");

        // CharacterStatus.Awake 이전에 체력과 기본 스탯 구성
        _status.InitializeActor(_definition);
        _status.gameObject.SetActive(true);
    }
}
