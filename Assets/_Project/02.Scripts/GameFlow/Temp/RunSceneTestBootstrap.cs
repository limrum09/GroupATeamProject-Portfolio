using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 에디터에서 직접 실행한 런 씬의 테스트 조건 구성과 시작 요청
/// </summary>
[DisallowMultipleComponent]
public class RunSceneTestBootstrap : MonoBehaviour
{
    [Serializable]
    struct TestMetaPurchase
    {
        [SerializeField] string _upgradeId;
        [SerializeField, Min(0)] int _rank;

        public string UpgradeId => _upgradeId;
        public int Rank => _rank;
    }

    [Header("----- 테스트 실행 -----")]
    [SerializeField] bool _autoStartInEditor = true;
    [SerializeField] GameFlowController _gameFlow;

    [Header("----- 캐릭터 설정 -----")]
    [Tooltip("메타 강화 JSON에서 참조하는 전체 캐릭터 성장 설정")]
    [SerializeField] CharacterGrowthDefinition[] _characters = Array.Empty<CharacterGrowthDefinition>();

    [SerializeField, Min(0)] int _testCharacterIndex;
    [SerializeField] Difficulty _testDifficulty = Difficulty.Normal;

    [Header("----- 기존 메타 강화 정의 -----")]
    [SerializeField] TextAsset _upgradesJson;
    [SerializeField] TextAsset _ranksJson;
    [SerializeField] TextAsset _effectsJson;

    [Header("----- 숙련도 레벨표 -----")]
    [SerializeField] MasteryLevelDefinition _integratedMasteryDefinition;
    [SerializeField] MasteryLevelDefinition _characterMasteryDefinition;

    [Header("----- 선택 캐릭터의 테스트 구매 기록 -----")]
    [SerializeField]TestMetaPurchase[] _purchases = Array.Empty<TestMetaPurchase>();

    [Header("----- 테스트 장착 슬롯 -----")]
    [Tooltip("6개 슬롯의 UpgradeId, 빈 문자열은 미장착")]
    [SerializeField]string[] _equippedUpgradeIds = new string[MetaProgression.EquipSlotCount];

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartInitialSceneTest()
    {
        // SceneFlow가 존재하는 실행에서는 기존 진입 경로 사용
        if (FindFirstObjectByType<SceneFlowController>(FindObjectsInactive.Include) != null)
            return;

        Scene initialScene = SceneManager.GetActiveScene();
        RunSceneTestBootstrap bootstrap = null;

        // 최초 활성 씬의 루트에 명시적으로 배치한 테스트 진입점만 탐색
        foreach (GameObject root in initialScene.GetRootGameObjects())
        {
            RunSceneTestBootstrap candidate = root.GetComponent<RunSceneTestBootstrap>();

            if (candidate == null || !candidate.isActiveAndEnabled || !candidate._autoStartInEditor)
                continue;

            if (bootstrap != null)
                throw new InvalidOperationException($"{initialScene.name}: 자동 실행할 테스트 진입점은 하나만 배치 필요");

            bootstrap = candidate;
        }

        // 테스트 진입점이 없는 최초 씬은 자동 시작 대상에서 제외
        if (bootstrap == null)
            return;

        bootstrap.StartCoroutine(bootstrap.BeginRunWhenReady());
    }

    IEnumerator BeginRunWhenReady()
    {
        ValidateConfiguration();

        while (true)
        {
            if (!isActiveAndEnabled || _gameFlow == null || !_gameFlow.isActiveAndEnabled)
                throw new InvalidOperationException($"{name}: 테스트 준비 중 Bootstrap 또는 GameFlow 연결 해제");

            if (_gameFlow.HasSceneStarted)
                break;

            yield return null;
        }

        RunStartRequest request = CreateStartRequest();
        QuestManager.Instance.InitializeForTests();
        _gameFlow.BeginRun(request);
    }

    void ValidateConfiguration()
    {
        if (_gameFlow == null)
            throw new MissingReferenceException($"{name}: GameFlowController 연결 필요");

        if (_characters == null || _characters.Length == 0)
            throw new InvalidOperationException($"{name}: 캐릭터 성장 설정 목록 필요");

        if (_testCharacterIndex < 0 || _testCharacterIndex >= _characters.Length)
            throw new InvalidOperationException($"{name}: 테스트 캐릭터 인덱스 범위 확인 필요");

        if (_upgradesJson == null || _ranksJson == null || _effectsJson == null)
            throw new MissingReferenceException($"{name}: 메타 강화 정의와 단계 및 효과 JSON 연결 필요");

        if (_purchases == null)
            throw new InvalidOperationException($"{name}: 테스트 구매 기록 배열 필요");

        if (_equippedUpgradeIds == null || _equippedUpgradeIds.Length != MetaProgression.EquipSlotCount)
            throw new InvalidOperationException($"{name}: 장착 슬롯은 {MetaProgression.EquipSlotCount}개 필요");

        RunStartRequest.ValidateDifficulty(_testDifficulty);

        if(_integratedMasteryDefinition == null || _characterMasteryDefinition == null)
            throw new MissingReferenceException($"{name}: 통합 숙련도와 캐릭터 숙련도 레벨표 연결 필요");

        _integratedMasteryDefinition.Validate();
        _characterMasteryDefinition.Validate();
    }

    RunStartRequest CreateStartRequest()
    {
        IReadOnlyDictionary<string, MetaUpgradeData> upgrades = MetaUpgradeJsonLoader.Load(_upgradesJson, _ranksJson, _effectsJson);

        // 실제 저장 상태와 분리된 테스트용 메타 성장 객체
        MetaProgression progression = new MetaProgression(_characters, upgrades, _integratedMasteryDefinition, _characterMasteryDefinition);

        CharacterGrowthDefinition character = _characters[_testCharacterIndex];

        if (!(character.CharacterDefinition is PlayerDefinition))
            throw new InvalidOperationException($"{name}: 테스트 캐릭터에 PlayerDefinition 연결 필요");

        List<MetaUpgradeRecord> purchases = new List<MetaUpgradeRecord>(_purchases.Length);

        foreach (TestMetaPurchase purchase in _purchases)
        {
            purchases.Add(new MetaUpgradeRecord(character.CharacterId, purchase.UpgradeId, purchase.Rank));
        }

        // 기존 구매 단계 검증 경로 재사용
        progression.RestorePurchases(purchases);

        string[] slots = new string[MetaProgression.EquipSlotCount];

        for (int i = 0; i < slots.Length; i++)
        {
            string upgradeId = _equippedUpgradeIds[i];

            // Inspector의 빈 입력을 미장착 슬롯으로 변환
            slots[i] = string.IsNullOrEmpty(upgradeId) ? null : upgradeId;
        }

        MetaLoadoutRecord loadout = new MetaLoadoutRecord(character.CharacterId, slots);

        // 구매 여부와 대상 캐릭터 및 중복 장착 검증
        progression.RestoreLoadouts(new MetaLoadoutRecord[] { loadout });

        // 실제 런과 동일한 메타 보너스 계산 경로
        RunGrowthSnapshot snapshot = progression.CreateRunSnapshot(character.CharacterId);

        return new RunStartRequest(snapshot, _testDifficulty);
    }
#endif
}