using Unity.VisualScripting;
using UnityEngine;

public class Health : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damageAmount)
    {
        currentHealth -= damageAmount;
         Debug.Log($"{gameObject.name} に {damageAmount} のダメージ！ 残りHP: {currentHealth}");

        if(currentHealth <= 0f)
        {
            Die();
        }
    }

    void Die()
    {
        Destroy(gameObject);
    }
}
