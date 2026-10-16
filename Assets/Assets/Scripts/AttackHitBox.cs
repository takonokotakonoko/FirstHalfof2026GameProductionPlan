using Unity.VisualScripting;
using UnityEngine;

public class AttackHitBox : MonoBehaviour
{
    [SerializeField] private float damage = 25f;
    private PlayerHealth playerHealth;

    private void Awake()
    {
        playerHealth = GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
            Debug.LogError("AttackHitBox requires a PlayerHealth component on this object or a parent.", this);
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
