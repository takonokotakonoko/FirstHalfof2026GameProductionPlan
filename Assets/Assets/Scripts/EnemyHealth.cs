using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    [Header("死亡効果音（SoundPlayerに登録した名前）")]
    [SerializeField] private string deathSoundName = "enemyDefeat";
    private float currentHealth;
    public int experimentPoint = 1;
    private bool isDead;

    /// <summary>ダメージを受けたときに通知する（引数はダメージ量）。エフェクト再生などに使用。</summary>
    public event System.Action<float> Damaged;

    private void Awake()
    {
        if (!TryGetComponent<EnemyDamageEffectSpawner>(out _))
        {
            gameObject.AddComponent<EnemyDamageEffectSpawner>();
        }
    }

    private void OnEnable()
    {
        currentHealth = maxHealth;
        isDead = false;
    }

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
        Damaged?.Invoke(damageAmount);

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
        if (levelManager != null)
        {
            levelManager.GetExp(experimentPoint);
        }

        Destroy(gameObject);
    }
}
