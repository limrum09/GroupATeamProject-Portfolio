using UnityEngine;

public enum CombatHitShape
{
    Box,
    Sphere
}

[System.Serializable]
public class CombatHitbox
{
    [SerializeField] CombatHitShape _hitShape = CombatHitShape.Box;
    [SerializeField] Vector3 _offset = new Vector3(0f, 0f, 0f);
    [SerializeField] Vector3 _boxSize = new Vector3(2f, 2f, 2f);
    [SerializeField] float _radius = 5f;

    public CombatHitShape HitShape => _hitShape;
    public Vector3 Offset => _offset;
    public Vector3 BoxSize => _boxSize;
    public float Radius => _radius;

    public void ClampValues()
    {
        _boxSize.x = Mathf.Max(0.01f, _boxSize.x);
        _boxSize.y = Mathf.Max(0.01f, _boxSize.y);
        _boxSize.z = Mathf.Max(0.01f, _boxSize.z);
        _radius = Mathf.Max(0.01f, _radius);
    }
}
