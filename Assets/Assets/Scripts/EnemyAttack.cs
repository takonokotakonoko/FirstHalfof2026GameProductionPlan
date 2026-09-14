using UnityEngine;

public enum EnemyAttackShape
{
    Circle,
    Rectangle,
    Square
}

public class EnemyAttack : MonoBehaviour
{
    [Header("UŒ‚Ý’è")]
    [SerializeField] private EnemyAttackShape attackShape = EnemyAttackShape.Circle;
    [SerializeField, Min(0f)] private float attackRange = 1.5f;
    [SerializeField, Min(0f)] private float attackWidth = 1f;
    [SerializeField, Min(0f)] private float attackDamage = 10f;
    [SerializeField, Min(0.01f)] private float attackInterval = 1f;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private Color attackRangeColor = new Color(1f, 0.2f, 0.1f, 0.2f);

    private Transform target;
    private float nextAttackTime;

    public float AttackRange => attackRange;

    private void Start()
    {
        GameObject targetObject = GameObject.FindWithTag(targetTag);
        if (targetObject != null)
            target = targetObject.transform;
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

        Gizmos.color = rangeColor;
        if (attackShape == EnemyAttackShape.Circle)
        {
            Gizmos.DrawSphere(transform.position, attackRange);
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
            Gizmos.DrawWireSphere(transform.position, attackRange);
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
