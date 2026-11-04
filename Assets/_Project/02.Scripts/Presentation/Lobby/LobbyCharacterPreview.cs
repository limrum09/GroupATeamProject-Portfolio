using System;
using UnityEngine;

/// <summary>
/// 로비 전용 캐릭터 더미의 생성과 교체 및 애니메이션 표시
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyCharacterPreview : MonoBehaviour
{
    [Header("----- 로비 상태 -----")]
    [SerializeField] LobbyManager _lobby;

    [Header("----- 더미 표시 위치 -----")]
    [SerializeField] Transform _anchor;

    Animator _instance;
    PlayerAnimation _displayedPrefab;

    bool _started;
    bool _subscribed;

    void Awake()
    {
        if (_lobby == null)
            throw new MissingReferenceException($"{name}: LobbyManager 연결 필요");
    }

    void Start()
    {
        // 씬 오브젝트의 Awake 완료 이후 최초 연결
        _started = true;
        Subscribe();
    }

    void OnEnable()
    {
        if (_started)
            Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();

        if (_instance != null)
            Destroy(_instance.gameObject);
    }

    void Subscribe()
    {
        if (_subscribed)
            return;

        _lobby.OnChanged += Refresh;
        _subscribed = true;

        // 구독 이전에 결정된 선택 상태도 즉시 반영
        Refresh();
    }

    void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _subscribed = false;

        if (_lobby != null)
            _lobby.OnChanged -= Refresh;
    }

    void Refresh()
    {
        LobbyCharacterDefinition character = _lobby.SelectedCharacter;

        if (character == null)
            throw new InvalidOperationException($"{name}: 로비 캐릭터 초기화 이후 표시 필요");

        // 난이도와 메타 상태 변경에서는 기존 외형 유지
        if (_instance != null && _displayedPrefab == character.PreviewPrefab)
            return;

        Show(character.PreviewPrefab);
    }

    /// <summary>
    /// 지정 프리팹으로 로비 더미 교체
    /// </summary>
    /// <param name="previewPrefab">루트 Animator를 가진 로비 전용 프리팹</param>
    void Show(PlayerAnimation previewPrefab)
    {
        if (_anchor == null)
            throw new MissingReferenceException($"{name}: 더미 표시 위치 연결 필요");

        if (!_anchor.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 더미 표시 위치는 이 오브젝트의 계층 안에 배치 필요");

        if (previewPrefab == null)
            throw new ArgumentNullException(nameof(previewPrefab));

        PlayerAnimation prefab = Instantiate(previewPrefab, _anchor);
        Animator next = prefab.PrefabAnimator;

        prefab.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        // 로비 표시 위치를 유지하는 제자리 애니메이션
        next.applyRootMotion = false;

        Animator previous = _instance;
        _instance = next;
        _displayedPrefab = previewPrefab;

        if (previous != null)
        {
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
        }
    }

    /// <summary>
    /// 현재 더미에 설정된 애니메이션 트리거 실행
    /// </summary>
    /// <param name="triggerName">Animator Controller에 정의된 Trigger 이름</param>
    public void PlayTrigger(string triggerName)
    {
        if (_instance == null)
            throw new InvalidOperationException($"{name}: 더미 생성 이후 사용 필요");

        if (string.IsNullOrWhiteSpace(triggerName))
            throw new ArgumentException("애니메이션 트리거 이름 필요", nameof(triggerName));

        foreach (AnimatorControllerParameter parameter in _instance.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Trigger || parameter.name != triggerName)
                continue;

            _instance.SetTrigger(triggerName);
            return;
        }

        throw new InvalidOperationException($"{name}: 더미 Animator에 정의되지 않은 Trigger: {triggerName}");
    }
}