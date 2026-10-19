using UnityEngine;
using UnityEngine.UI;

public class LevelManager : MonoBehaviour
{
    public int totalExp = 0;    //enemiesがもつexpをここに加算
    public int expToNextLv; //レベルアップのための必要経験値
    [SerializeField] private Text playerLevelText;
    PlayerHealth player;

    public bool CanReduceExpRequirement => CanReduceExpRequirementTo(2);

    public bool CanReduceExpRequirementTo(int minimumRequirement)
    {
        return minimumRequirement > 0 && expToNextLv > minimumRequirement;
    }

    private void Start()
    {
        player = GameObject.FindAnyObjectByType<PlayerHealth>();
        if (player == null)
        {
            Debug.LogError("LevelManager: PlayerHealth が見つかりません。", this);
        }
        else
        {
            UpdatePlayerLevelText();
        }

        if (playerLevelText == null)
        {
            Debug.LogWarning("LevelManager: Player Level Text が未設定です。Legacy TextをInspectorで割り当ててください。", this);
        }
    }

    //  EnemyHealth.cs側でtotalExpを加算する
    public void GetExp(int amount)
    {
        Debug.Log($"LevelManager: GetExp called. Gained EXP: {amount}, Current EXP: {totalExp}", this);

        if (amount <= 0)
        {
            Debug.LogWarning($"LevelManager: 経験値は1以上を指定してください（指定値: {amount}）。", this);
            return;
        }

        if (expToNextLv <= 0)
        {
            Debug.LogError("LevelManager: expToNextLv は1以上に設定してください。", this);
            return;
        }

        totalExp += amount;
        Debug.Log($"LevelManager: 経験値を加算しました。現在の経験値: {totalExp}/{expToNextLv}", this);

        while (totalExp >= expToNextLv)
        {
            if (player == null)
                player = GameObject.FindAnyObjectByType<PlayerHealth>();

            if (player == null)
            {
                Debug.LogError("LevelManager: PlayerHealth が見つからないため、レベルアップできません。", this);
                return;
            }

            totalExp -= expToNextLv;
            player.LevelUp();
            UpdatePlayerLevelText();
            Debug.Log($"LevelManager: レベルアップしました。残り経験値: {totalExp}/{expToNextLv}", this);
        }
    }

    public void ReduceExpRequirement(int amount, int minimumRequirement)
    {
        ReduceExpRequirement(amount, minimumRequirement, int.MaxValue);
    }

    public void ReduceExpRequirement(int amount, int minimumRequirement, int maximumRequirement)
    {
        if (amount <= 0 || minimumRequirement <= 0 || maximumRequirement < minimumRequirement)
        {
            Debug.LogWarning("LevelManager: 減少量と必要経験値の最小値・最大値を確認してください。", this);
            return;
        }

        expToNextLv = Mathf.Clamp(expToNextLv - amount, minimumRequirement, maximumRequirement);
    }

    private void UpdatePlayerLevelText()
    {
        if (playerLevelText == null || player == null)
            return;

        playerLevelText.text = $"レベル: {player.level}";
    }

}
