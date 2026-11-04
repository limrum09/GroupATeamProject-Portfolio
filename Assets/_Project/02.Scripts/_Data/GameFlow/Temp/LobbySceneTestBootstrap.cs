using System;
using UnityEngine;

/// <summary>
/// Context 메뉴를 통한 로비 준비와 초기화 완료 후 씬 재로드 요청
/// </summary>
[DisallowMultipleComponent]
public class LobbySceneTestBootstrap : MonoBehaviour
{
    [Header("----- 최초 실행용 비활성 자식 -----")]
    [SerializeField] MetaProgressionInitializer _metaProgression;
    [SerializeField] SceneFlowController _sceneFlow;

    bool _busy;

#if UNITY_EDITOR
    SceneFlowController FindRunningSceneFlow()
    {
        SceneFlowController found = null;

        foreach (SceneFlowController controller in FindObjectsByType<SceneFlowController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!controller.isActiveAndEnabled)
                throw new InvalidOperationException($"{controller.name}: SceneFlowController 컴포넌트 활성 설정 필요");

            if (found != null)
                throw new InvalidOperationException("활성 SceneFlowController는 하나만 배치 필요");

            found = controller;
        }

        return found;
    }

    [ContextMenu("테스트/로비 준비 및 다시 불러오기")]
    async void ReloadLobbyFromContext()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Play 중에 실행 필요", this);
            return;
        }

        if (_busy)
        {
            Debug.LogWarning("로비 준비를 기다리는 중입니다.", this);
            return;
        }

        _busy = true;

        try
        {
            SceneFlowController flow = FindRunningSceneFlow();

            if (flow == null)
            {
                if (_metaProgression == null || _sceneFlow == null)
                    throw new MissingReferenceException($"{name}: 메타 초기화와 SceneFlow 자식 연결 필요");

                if (!_metaProgression.enabled || !_sceneFlow.enabled)
                    throw new InvalidOperationException($"{name}: 준비 오브젝트의 컴포넌트 체크 활성 필요");

                if (_metaProgression.transform.parent != transform || _sceneFlow.transform.parent != transform)
                    throw new InvalidOperationException($"{name}: 두 준비 오브젝트를 이 오브젝트의 직접 자식으로 연결 필요");

                if (_metaProgression.gameObject.activeSelf || _sceneFlow.gameObject.activeSelf)
                    throw new InvalidOperationException($"{name}: 두 준비 오브젝트는 초기 비활성 상태로 저장 필요");

                // Awake의 DontDestroyOnLoad 호출 전에 루트로 분리
                _metaProgression.transform.SetParent(null, true);
                _sceneFlow.transform.SetParent(null, true);

                _metaProgression.gameObject.SetActive(true);
                _sceneFlow.gameObject.SetActive(true);

                flow = _sceneFlow;
            }

            bool initialized = await flow.EnsureInitializedAsync(retryFailed: true);

            // 대기 중 씬이나 대상이 종료된 경우 처리 중단
            if (this == null || flow == null)
                return;

            if (!initialized)
            {
                Debug.LogError("초기화 실패. 설정을 수정한 뒤 Context 메뉴에서 다시 시도하세요.", this);
                return;
            }

            if (!flow.TryReturnToMetaScene(true))
                Debug.LogWarning("현재 씬 이동 요청을 받을 수 없습니다.", this);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            _busy = false;
        }
    }
#endif
}