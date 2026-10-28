using UnityEngine;

public class EnemyMove : MonoBehaviour
{
    [Header("�ړ��ݒ�")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField, Min(0f)] private float stoppingDistance = 1.5f;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private SphereCollider searchZone;

    private Transform target;
    private EnemyAttack enemyAttack;
    [Header("敵のアニメーション")]
    private Animator animator;

    private void Awake()
    {
        enemyAttack = GetComponent<EnemyAttack>();
        animator = GetComponentInChildren<Animator>();
    }

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

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag(targetTag))
            return;

        if (target == null)
            target = other.transform;

        Vector3 moveDirection = target.position - transform.position;
        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(moveDirection);
        }

        // 攻撃範囲内なら移動しない
        if (enemyAttack != null && enemyAttack.IsTargetInRange(target))
        {
            if (animator != null)
                animator.SetBool("IsMoving", false);

            return;
        }

        float distance = moveDirection.magnitude;

        float moveDistance = Mathf.Min(
            moveSpeed * Time.deltaTime,
            Mathf.Max(0f, distance - stoppingDistance)
        );

        if (moveDistance > 0.001f)
        {
            transform.position += moveDirection.normalized * moveDistance;

            if (animator != null)
                animator.SetBool("IsMoving", true);
        }
        else if(moveDistance <= 0.001 || (enemyAttack != null && !enemyAttack.IsTargetInRange(target)))
        {
            if (animator != null)
                animator.SetBool("IsMoving", false);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if(other.CompareTag(targetTag) && animator != null)
        {
            animator.SetBool("IsMoving", false);
        }
    }
}
