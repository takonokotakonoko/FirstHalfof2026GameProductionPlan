using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    [Header("死亡効果音（SoundPlayerに登録した名前）")]
    [SerializeField] private string deathSoundName = "enemyDefeat";
    private float currentHealth;
    public int experimentPoint = 1;
    private bool isDead;

    private void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damageAmount)
    {
        if (isDead)
            return;

        currentHealth -= damageAmount;
        Debug.Log($"{gameObject.name} に {damageAmount} ダメージ！ 残りHP: {currentHealth}");

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;

        SoundPlayer.PlayIfExists(deathSoundName);

        LevelManager levelManager = GameObject.FindAnyObjectByType<LevelManager>();
        if (levelManager == null)
        {
            Debug.LogError("EnemyHealth: LevelManager が見つからないため、経験値を付与できません。", this);
        }
        else
        {
            levelManager.GetExp(experimentPoint);
        }

        Destroy(gameObject);
    }
}
