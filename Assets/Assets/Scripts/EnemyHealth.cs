using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    [Header("死亡効果音")]
    [SerializeField] private AudioClip deathSound;
    [SerializeField, Range(0f, 1f)] private float deathSoundVolume = 1f;
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

        if (deathSound == null)
        {
            Debug.LogWarning("EnemyHealth: 死亡SEが未設定です。InspectorでAudio Clipを割り当ててください。", this);
        }
        else
        {
            AudioSource.PlayClipAtPoint(deathSound, transform.position, deathSoundVolume);
        }

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
