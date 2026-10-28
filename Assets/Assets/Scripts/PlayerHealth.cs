using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private Slider HpBar;
    [Header("レベルアップ効果音（SoundPlayerに登録した名前）")]
    [SerializeField] private string levelUpSoundName = "levelUp";
    [Min(1)] public int level = 1;

    [Header("レベルアップ効果")]
    [Tooltip("1レベルごとの基礎攻撃力の増加率。0.1 = 10%")]
    [SerializeField, Min(0f)] private float attackIncreasePerLevel = 0.1f;
    [Tooltip("レベルアップ時に回復する体力（最大体力に対する割合）。0.1 = 10%")]
    [SerializeField, Range(0f, 1f)] private float levelUpHealRate = 0.1f;

    private float currentHealth;

    // 時間経過報酬「全能力n倍」で掛かる倍率（攻撃力・最大体力）
    private float powerMultiplier = 1f;

    /// <summary>攻撃力に掛かる倍率（レベルによる増加 × 全能力の倍率）。</summary>
    public float StatMultiplier => (1f + Mathf.Max(0, level - 1) * attackIncreasePerLevel) * powerMultiplier;
    public float MaxHealth => GetMaxHealth();

    /// <summary>プレイヤーがダメージを受けたときに通知するイベント。</summary>
    public event System.Action<float> Damaged;

    private void Awake()
    {
        level = Mathf.Max(1, level);
        currentHealth = GetMaxHealth();
        UpdateHealthBar();
    }

    public void LevelUp()
    {
        level++;
        Heal(GetMaxHealth() * levelUpHealRate);
        SoundPlayer.PlayIfExists(levelUpSoundName);
        Debug.Log($"Player level up! Level: {level}, HP: {currentHealth}/{GetMaxHealth()}");
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || currentHealth <= 0f)
            return;

        currentHealth = Mathf.Min(currentHealth + amount, GetMaxHealth());
        UpdateHealthBar();
    }

    /// <summary>
    /// 攻撃力と最大体力に倍率を掛けます（時間経過報酬「全能力n倍」用）。
    /// 現在の体力も同じ割合で増えます。
    /// </summary>
    public void MultiplyPower(float multiplier)
    {
        if (multiplier <= 0f)
        {
            Debug.LogWarning($"PlayerHealth: 倍率は0より大きくしてください（指定値: {multiplier}）。", this);
            return;
        }

        powerMultiplier *= multiplier;
        currentHealth *= multiplier;
        UpdateHealthBar();
    }

    public void AddMaxHealth(float amount)
    {
        AddMaxHealth(amount, 1f, float.MaxValue);
    }

    public bool CanIncreaseMaxHealth(float maximumValue)
    {
        return GetMaxHealth() < maximumValue;
    }

    public void AddMaxHealth(float amount, float minimumValue, float maximumValue)
    {
        if (amount <= 0f || minimumValue <= 0f || maximumValue < minimumValue)
        {
            Debug.LogWarning(
                $"PlayerHealth: 体力の増加量・最小値・最大値が不正です（増加量: {amount}, 最小値: {minimumValue}, 最大値: {maximumValue}）。",
                this);
            return;
        }

        float previousMaxHealth = GetMaxHealth();
        float nextMaxHealth = Mathf.Clamp(previousMaxHealth + amount, minimumValue, maximumValue);
        float actualIncrease = nextMaxHealth - previousMaxHealth;
        if (actualIncrease <= 0f)
            return;

        maxHealth += actualIncrease / powerMultiplier;
        currentHealth += actualIncrease;
        UpdateHealthBar();
    }

    public void TakeDamage(float damageAmount)
    {
        if (currentHealth <= 0f)
            return;

        currentHealth -= damageAmount;
        UpdateHealthBar();
        Debug.Log($"{gameObject.name} に {damageAmount} ダメージ！ 残りHP: {currentHealth}");
        Damaged?.Invoke(damageAmount);

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private float GetMaxHealth()
    {
        return maxHealth * powerMultiplier;
    }

    private void UpdateHealthBar()
    {
        if (HpBar == null)
            return;

        HpBar.maxValue = GetMaxHealth();
        HpBar.value = currentHealth;
    }

    private void Die()
    {
        Debug.Log("Player は倒れた");
        // 必要ならここでリスポーンやゲームオーバー処理を入れる
    }
}
