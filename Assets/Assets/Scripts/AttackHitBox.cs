using UnityEngine;

public class AttackHitBox : MonoBehaviour
{
    [SerializeField] private float damage = 25f;
    [SerializeField] private PlayerHealth playerHealth;

    private void Awake()
    {
        if (playerHealth == null)
            playerHealth = GetComponentInParent<PlayerHealth>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.TryGetComponent<IDamage>(out var damageable))
        {
            float finalDamage = playerHealth != null
                ? damage * playerHealth.StatMultiplier
                : damage;
            damageable.TakeDamage(finalDamage);
        }   
    }
}
