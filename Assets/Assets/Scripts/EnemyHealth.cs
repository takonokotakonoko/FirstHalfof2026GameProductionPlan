using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    private void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damageAmount)
    {
        currentHealth -= damageAmount;
        Debug.Log($"{gameObject.name} に {damageAmount} ダメージ！ 残りHP: {currentHealth}");

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}
