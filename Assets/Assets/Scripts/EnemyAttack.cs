using UnityEngine;

public enum EnemyAttackShape
{
    Circle,
    Rectangle,
    Square
}

public class EnemyAttack : MonoBehaviour
{
    [Header("攻撃設定")]
    [SerializeField] private EnemyAttackShape attackShape = EnemyAttackShape.Circle;
    [SerializeField, Min(0f)] private float attackRange = 0.5f;
    [SerializeField, Min(0f)] private float attackWidth = 1f;
    [SerializeField, Min(0f)] private float attackDamage = 10f;
    [SerializeField, Min(0.01f)] private float attackInterval = 2f;
    [Tooltip("ONにすると、円形の攻撃範囲を「敵の体の表面からプレイヤーの体の表面まで」の距離で判定する。OFFなら中心どうしの距離で判定する")]
    [SerializeField] private bool measureFromBodySurface = true;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private Color attackRangeColor = new Color(1f, 0.2f, 0.1f, 0.2f);
    private Transform target;
    private Collider targetCollider;
    private SphereCollider bodyCollider;
    private float nextAttackTime;

    public float AttackRange => attackRange;

    private void Awake()
    {
        bodyCollider = GetComponent<SphereCollider>();
        if (bodyCollider != null && bodyCollider.isTrigger)
            bodyCollider = null;
    }

    private void Start()
    {
        GameObject targetObject = GameObject.FindWithTag(targetTag);
        if (targetObject != null)
        {
            target = targetObject.transform;
            targetCollider = targetObject.GetComponent<Collider>();
        }
    }

    /// <summary>攻撃力・攻撃間隔・攻撃範囲を変更します（生成時のステータス設定用）。</summary>
    public void SetStats(float damage, float interval, float range)
    {
        attackDamage = Mathf.Max(0f, damage);
        attackInterval = Mathf.Max(0.01f, interval);
        attackRange = Mathf.Max(0f, range);
    }

    private void Update()
    {
        if (target == null)
            return;

        if (IsTargetInRange(target) && Time.time >= nextAttackTime)
        {
            AttackTarget();
            nextAttackTime = Time.time + attackInterval;
        }
    }

    public bool IsTargetInRange(Transform targetTransform)
    {
        if (targetTransform == null)
            return false;

        if (attackShape == EnemyAttackShape.Circle && measureFromBodySurface)
            return GetSurfaceDistance(targetTransform) <= attackRange;

        Vector3 localTargetPosition = transform.InverseTransformPoint(targetTransform.position);
        localTargetPosition.y = 0f;

        switch (attackShape)
        {
            case EnemyAttackShape.Rectangle:
                return IsInsideRectangle(localTargetPosition, attackWidth, attackRange);
            case EnemyAttackShape.Square:
                return IsInsideRectangle(localTargetPosition, attackRange, attackRange);
            default:
                return localTargetPosition.sqrMagnitude <= attackRange * attackRange;
        }
    }

    // 敵の体の表面から、プレイヤーのColliderの一番近い点までの水平距離
    private float GetSurfaceDistance(Transform targetTransform)
    {
        Vector3 origin = transform.position;
        Vector3 closestPoint = targetTransform == target && targetCollider != null && targetCollider.enabled
            ? targetCollider.ClosestPoint(origin)
            : targetTransform.position;

        Vector3 offset = closestPoint - origin;
        offset.y = 0f;

        float bodyRadius = bodyCollider != null
            ? bodyCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z)
            : 0f;

        return Mathf.Max(0f, offset.magnitude - bodyRadius);
    }

    private bool IsInsideRectangle(Vector3 localPosition, float width, float depth)
    {
        return localPosition.z >= 0f
            && localPosition.z <= depth
            && Mathf.Abs(localPosition.x) <= width * 0.5f;
    }

    private void AttackTarget()
    {
        IDamage damageable = target.GetComponentInParent<IDamage>();
        if (damageable != null)
            damageable.TakeDamage(attackDamage);
    }

    private void OnDrawGizmosSelected()
    {
        Color rangeColor = attackRangeColor;
        rangeColor.a = Mathf.Clamp01(rangeColor.a);

        float circleRadius = attackRange;
        if (attackShape == EnemyAttackShape.Circle && measureFromBodySurface)
        {
            SphereCollider body = GetComponent<SphereCollider>();
            if (body != null && !body.isTrigger)
                circleRadius += body.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        }

        Gizmos.color = rangeColor;
        if (attackShape == EnemyAttackShape.Circle)
        {
            Gizmos.DrawSphere(transform.position, circleRadius);
        }
        else
        {
            float width = attackShape == EnemyAttackShape.Square ? attackRange : attackWidth;
            Vector3 center = transform.position + transform.forward * (attackRange * 0.5f);
            Vector3 size = new Vector3(width, 0.1f, attackRange);

            Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
        }

        Gizmos.color = new Color(rangeColor.r, rangeColor.g, rangeColor.b, 1f);
        if (attackShape == EnemyAttackShape.Circle)
        {
            Gizmos.DrawWireSphere(transform.position, circleRadius);
        }
        else
        {
            float width = attackShape == EnemyAttackShape.Square ? attackRange : attackWidth;
            Vector3 center = transform.position + transform.forward * (attackRange * 0.5f);
            Vector3 size = new Vector3(width, 0.1f, attackRange);

            Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
