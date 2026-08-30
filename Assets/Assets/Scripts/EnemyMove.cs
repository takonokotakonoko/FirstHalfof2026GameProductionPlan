using UnityEngine;

public class EnemyMove : MonoBehaviour, IDamage
{
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private SphereCollider searchZone;
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    private Transform target;

    private void Start()
    {
        currentHealth = maxHealth;
        GameObject targetObject = GameObject.FindWithTag(targetTag);
        if (targetObject != null)
            target = targetObject.transform;
    }

    public void TakeDamage(float damageAmount)
    {
        currentHealth -= damageAmount;
        Debug.Log($"{gameObject.name} に {damageAmount} ダメージ！ 残りHP: {currentHealth}");

        if (currentHealth <= 0f)
        {
            Destroy(gameObject);
        }
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

        transform.position = Vector3.MoveTowards(transform.position, target.position, moveSpeed * Time.deltaTime);
    }
}
