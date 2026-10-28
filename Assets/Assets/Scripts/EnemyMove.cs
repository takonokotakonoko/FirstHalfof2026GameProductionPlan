using UnityEngine;

public class EnemyMove : MonoBehaviour
{
    [Header("移動設定")]
    [SerializeField] private float moveSpeed = 0.5f;
    [Tooltip("プレイヤーとの距離（中心どうし）がこれ以下になったら止まる。攻撃範囲に入ったときも止まる")]
    [SerializeField, Min(0f)] private float stoppingDistance = 0f;
    [SerializeField] private string targetTag = "Player";

    private Transform target;
    private EnemyAttack enemyAttack;
    [Header("敵のアニメーション")]
    private Animator animator;

    public float MoveSpeed => moveSpeed;

    private void Awake()
    {
        enemyAttack = GetComponent<EnemyAttack>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        FindTarget();
    }

    public void SetMoveSpeed(float speed)
    {
        moveSpeed = Mathf.Max(0f, speed);
    }

    private void FindTarget()
    {
        GameObject targetObject = GameObject.FindWithTag(targetTag);
        if (targetObject != null)
            target = targetObject.transform;
    }

    // プレイヤーとの距離に関わらず、常にプレイヤーに向かって進む
    private void Update()
    {
        if (target == null)
        {
            FindTarget();
            if (target == null)
                return;
        }

        Vector3 moveDirection = target.position - transform.position;
        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(moveDirection);
        }

        // 攻撃範囲内なら移動しない
        if (enemyAttack != null && enemyAttack.IsTargetInRange(target))
        {
            SetMoving(false);
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
            SetMoving(true);
        }
        else
        {
            SetMoving(false);
        }
    }

    private void SetMoving(bool isMoving)
    {
        if (animator != null)
            animator.SetBool("IsMoving", isMoving);
    }
}
