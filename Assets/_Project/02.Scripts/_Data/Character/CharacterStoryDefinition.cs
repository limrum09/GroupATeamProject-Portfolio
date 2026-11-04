using UnityEngine;

/// <summary>
/// 캐릭터별 콘텐츠와 해금 조건 JSON 참조
/// </summary>
[CreateAssetMenu(
    menuName = "Project/Character Story Definition",
    fileName = "_CharacterStoryDefinition")]
public class CharacterStoryDefinition : ScriptableObject
{
    [Header("----- CSV 변환 JSON -----")]
    [SerializeField] TextAsset _storiesJson;
    [SerializeField] TextAsset _unlockConditionsJson;

    public TextAsset StoriesJson => _storiesJson;
    public TextAsset UnlockConditionsJson => _unlockConditionsJson;

    /// <summary>
    /// 콘텐츠와 해금 조건 로딩에 필요한 JSON 참조 검사
    /// </summary>
    public void Validate()
    {
        if (_storiesJson == null || _unlockConditionsJson == null)
            throw new MissingReferenceException($"{name}: 스토리, 해금 조건 JSON 연결 필요");
    }
}
