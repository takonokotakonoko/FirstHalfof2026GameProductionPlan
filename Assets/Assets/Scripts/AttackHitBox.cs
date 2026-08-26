using Unity.VisualScripting;
using UnityEngine;

public class AttackHitBox : MonoBehaviour
{
    [SerializeField] private float damage = 25f;

    private void OnTriggerEnter(Collider other)
    {
        if(other.TryGetComponent<IDamage>(out var damageable))
        {
            damageable.TakeDamage(damage);
        }   
    }
}
