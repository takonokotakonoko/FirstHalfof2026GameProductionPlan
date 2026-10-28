using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    [Tooltip("ONにすると、プレイヤーのレベルに合わせた敵の強化（ステルス強化）と、出現距離による種類の変更を受けない")]
    [SerializeField] private bool isBoss = false;
    [Header("死亡効果音（SoundPlayerに登録した名前）")]
    [SerializeField] private string deathSoundName = "enemyDefeat";
    private float currentHealth;
    public int experimentPoint = 1;
    private bool isDead;

    /// <summary>ダメージを受けたときに通知する（引数はダメージ量）。エフェクト再生などに使用。</summary>
    public event System.Action<float> Damaged;

    public bool IsBoss => isBoss;

    /// <summary>最大体力を変更し、体力を満タンにします（生成時のステータス設定用）。</summary>
    public void SetMaxHealth(float value)
    {
        maxHealth = Mathf.Max(1f, value);
        currentHealth = maxHealth;
        isDead = false;
    }

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
