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
        if (DamageTargetFilter.IsBuilding(other.transform))
            return;

        IDamage damageable = other.GetComponentInParent<IDamage>();
        if (damageable != null)
        {
            float finalDamage = playerHealth != null
                ? damage * playerHealth.StatMultiplier
                : damage;
            damageable.TakeDamage(finalDamage);
        }
    }
}
