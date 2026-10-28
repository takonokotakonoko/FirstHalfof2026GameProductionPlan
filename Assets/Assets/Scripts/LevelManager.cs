using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LevelManager : MonoBehaviour
{
    [Serializable]
    private class ExpRequirement
    {
        [InspectorName("開始レベル")]
        [Tooltip("このレベル以上のとき、下の必要経験値を使います")]
        [Min(1)] public int fromLevel = 1;

        [InspectorName("必要経験値")]
        [Min(1)] public int exp = 3;
    }

    public int totalExp = 0;    //enemiesがもつexpをここに加算
    public int expToNextLv; //レベルアップのための必要経験値（レベルに応じて自動で設定される）
    [SerializeField] private Text playerLevelText;

    [Header("レベルアップ必要経験値")]
    [Tooltip("開始レベルの小さい順に登録してください。例：Lv1〜2は3、Lv3〜4は5、Lv5以降は10")]
    [SerializeField] private List<ExpRequirement> expRequirements = new List<ExpRequirement>
    {
        new ExpRequirement { fromLevel = 1, exp = 3 },
        new ExpRequirement { fromLevel = 3, exp = 5 },
        new ExpRequirement { fromLevel = 5, exp = 10 }
    };

    [Header("レベルアップ時の移動速度上昇")]
    [Tooltip("1レベルごとの移動速度の上昇率。0.1 = 10%")]
    [SerializeField, Min(0f)] private float moveSpeedIncreasePerLevel = 0.1f;
    [Tooltip("レベルアップで上がる移動速度の上限（m/s）")]
    [SerializeField, Min(0.1f)] private float maxMoveSpeed = 4f;

    PlayerHealth player;
    private PlayerMove playerMove;

    // 建物破壊報酬「必要経験値量の減少」で減らした量と、その下限
    private int expRequirementReduction;
    private int expRequirementMinimum = 1;

    public bool CanReduceExpRequirement => CanReduceExpRequirementTo(2);

    public bool CanReduceExpRequirementTo(int minimumRequirement)
    {
        return minimumRequirement > 0 && expToNextLv > minimumRequirement;
    }

    private void Start()
    {
        player = GameObject.FindAnyObjectByType<PlayerHealth>();
        playerMove = GameObject.FindAnyObjectByType<PlayerMove>();

        if (player == null)
        {
            Debug.LogError("LevelManager: PlayerHealth が見つかりません。", this);
        }
        else
        {
            UpdatePlayerLevelText();
        }

        UpdateExpToNextLevel();

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
            ApplyLevelUpEffects();
            UpdateExpToNextLevel();
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

        // 減らした量を覚えておき、レベルが上がって必要経験値が変わっても効果が残るようにする
        expRequirementReduction += amount;
        expRequirementMinimum = minimumRequirement;
        UpdateExpToNextLevel();
    }

    private void ApplyLevelUpEffects()
    {
        if (playerMove == null)
            playerMove = GameObject.FindAnyObjectByType<PlayerMove>();

        if (playerMove != null && moveSpeedIncreasePerLevel > 0f && playerMove.CanIncreaseMoveSpeedUpTo(maxMoveSpeed))
            playerMove.IncreaseMoveSpeed(moveSpeedIncreasePerLevel, maxMoveSpeed);

        // 敵のステルス強化（体力・移動速度・再出現間隔）。シーンに複数あってもすべてに伝える
        foreach (EnemySpawnManager spawnManager in GameObject.FindObjectsByType<EnemySpawnManager>())
            spawnManager.SetPlayerLevel(player.level);
    }

    private void UpdateExpToNextLevel()
    {
        int level = player != null ? player.level : 1;
        int baseRequirement = GetBaseRequirement(level);
        if (baseRequirement <= 0)
            return;

        expToNextLv = Mathf.Max(
            Mathf.Min(expRequirementMinimum, baseRequirement),
            baseRequirement - expRequirementReduction);
    }

    private int GetBaseRequirement(int level)
    {
        int requirement = 0;
        int bestFromLevel = int.MinValue;
        foreach (ExpRequirement entry in expRequirements)
        {
            if (entry == null || entry.fromLevel > level || entry.fromLevel < bestFromLevel)
                continue;

            bestFromLevel = entry.fromLevel;
            requirement = entry.exp;
        }

        return requirement > 0 ? requirement : expToNextLv;
    }

    private void UpdatePlayerLevelText()
    {
        if (playerLevelText == null || player == null)
            return;

        playerLevelText.text = $"レベル: {player.level}";
    }

}
