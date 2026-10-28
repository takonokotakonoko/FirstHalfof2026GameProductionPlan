using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour, IDamage
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private Slider HpBar;
    [Header("レベルアップ効果音（SoundPlayerに登録した名前）")]
    [SerializeField] private string levelUpSoundName = "levelUp";
    [Min(1)] public int level = 1;
    private float currentHealth;

    public float StatMultiplier => 1f + Mathf.Max(0, level - 1) * 0.1f;
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
        float previousMaxHealth = GetMaxHealth();
        level++;
        currentHealth += GetMaxHealth() - previousMaxHealth;
        UpdateHealthBar();
        SoundPlayer.PlayIfExists(levelUpSoundName);
        Debug.Log($"Player level up! Level: {level}, Max HP: {GetMaxHealth()}");
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

        maxHealth += actualIncrease / StatMultiplier;
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
        return maxHealth * StatMultiplier;
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
