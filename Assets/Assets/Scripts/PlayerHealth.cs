using System;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;
    public Slider playerHpBar;

    private void Start()
    {
        currentHealth = maxHealth;
        HpGauge(playerHpBar, currentHealth, maxHealth);
    }

    public void TakeDamage(float damageAmount)
    {
        currentHealth = Mathf.Max(currentHealth - damageAmount, 0f);
        HpGauge(playerHpBar, currentHealth, maxHealth);
        Debug.Log($"{gameObject.name} に {damageAmount} ダメージ！ 残りHP: {currentHealth}");

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log("Player は倒れた");
        
        // GameManagerにプレイヤー敗北を報告
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlayerDefeated();
        }
    }

    private void HpGauge(Slider Hpbar, float currentHp, float maxHp)
    {
        if (Hpbar == null) return;

        Hpbar.maxValue = maxHp;
        Hpbar.value = Mathf.Clamp(currentHp, 0f, maxHp);
    }
}
