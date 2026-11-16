#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 즉시 종료되는 범위 효과의 Scene 뷰 확인용 표시
/// </summary>
[DisallowMultipleComponent]
public class AreaEffectDebugPreview : MonoBehaviour
{
    CombatHitbox _hitbox;
    double _hideAt;

    void Update()
    {
        if (Time.timeAsDouble >= _hideAt)
            Destroy(gameObject);
    }

    void OnDrawGizmos()
    {
        // 초기화 이전 및 Scene 뷰 외부의 표시 제외
        if (_hitbox == null || UnityEditor.SceneView.currentDrawingSceneView == null)
            return;

        if (Time.timeAsDouble >= _hideAt)
            return;

        CombatHitQuery.DrawPreview(transform.position, transform.rotation, Vector3.zero, _hitbox);
    }

    /// <summary>
    /// 공격 발생 씬에 1초간 유지되는 범위 표시 생성
    /// </summary>
    /// <param name="origin">범위 판정 기준 위치</param>
    /// <param name="rotation">범위 판정 기준 회전</param>
    /// <param name="hitbox">검증된 타격 범위 설정</param>
    /// <param name="scene">공격이 발생한 씬</param>
    public static void Show(Vector3 origin, Quaternion rotation, CombatHitbox hitbox, Scene scene)
    {
        GameObject previewObject = new GameObject("AreaEffect_DebugPreview");

        previewObject.transform.SetPositionAndRotation(origin, rotation);

        if (previewObject.scene != scene)
            SceneManager.MoveGameObjectToScene(previewObject, scene);

        AreaEffectDebugPreview preview = previewObject.AddComponent<AreaEffectDebugPreview>();

        preview._hitbox = hitbox;
        preview._hideAt = Time.timeAsDouble + 1d;
    }
}
#endif