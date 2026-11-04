using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로비 스토리 목록 항목의 생성 및 재사용
/// </summary>
public class CharacterStoryViewPool : MonoBehaviour
{
    [Header("----- 목록과 초기 풀 -----")]
    [SerializeField] RectTransform _contentRoot;
    [SerializeField] List<CharacterStoryItemView> _items = new List<CharacterStoryItemView>();

    public IReadOnlyList<CharacterStoryItemView> Items => _items;

    /// <summary>
    /// 초기 풀과 복제에 필요한 참조 및 계층 검사
    /// </summary>
    /// <param name="contentRoot">스토리 목록 ScrollRect의 Content</param>
    public void ValidateBindings(RectTransform contentRoot)
    {
        if (_contentRoot == null || _contentRoot != contentRoot || _items == null || _items.Count == 0)
            throw new MissingReferenceException($"{name}: 목록 Content와 초기 스토리 항목 연결 필요");

        HashSet<CharacterStoryItemView> references = new HashSet<CharacterStoryItemView>();
        foreach (CharacterStoryItemView item in _items)
        {
            if (item == null || item.transform.parent != _contentRoot || !references.Add(item))
                throw new InvalidOperationException($"{name}: Content 직속 스토리 항목을 중복 없이 연결 필요");

            item.ValidateBindings();
        }
    }

    /// <summary>
    /// 항목을 비활성 상태로 정리하고 필요한 수량만큼 풀 확장
    /// </summary>
    /// <param name="count">표시할 스토리 수량</param>
    public void Prepare(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        Clear();
        while (_items.Count < count)
            _items.Add(Instantiate(_items[0], _contentRoot, false));
    }

    /// <summary>
    /// 콘텐츠가 연결된 항목 활성화
    /// </summary>
    /// <param name="count">바인딩을 완료한 스토리 수량</param>
    public void Activate(int count)
    {
        if (count < 0 || count > _items.Count)
            throw new ArgumentOutOfRangeException(nameof(count));

        for (int index = 0; index < count; index++)
            _items[index].gameObject.SetActive(true);
    }

    /// <summary>
    /// 풀 항목의 콘텐츠 해제 및 비활성화
    /// </summary>
    public void Clear()
    {
        foreach (CharacterStoryItemView item in _items)
        {
            item.Unbind();
            item.gameObject.SetActive(false);
        }
    }
}
