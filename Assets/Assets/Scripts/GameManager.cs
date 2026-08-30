using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("ゲーム状態")]
    private float gameStartTime;
    private float elapsedTime;
    private bool isGameActive = true;

    [Header("敵統計")]
    private int defeatedEnemyCount = 0;
    private int totalScore = 0;

    [Header("敵のスコア設定")]
    [SerializeField] private int scorePerEnemy = 100;

    [Header("UI表示")]
    [SerializeField] private TextMeshProUGUI elapsedTimeDisplay;
    [SerializeField] private TextMeshProUGUI scoreDisplay;
    [SerializeField] private TextMeshProUGUI enemyCountDisplay;

    private void Awake()
    {
        // シングルトンパターン
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        gameStartTime = Time.time;
    }

    private void Update()
    {
        if (isGameActive)
        {
            elapsedTime = Time.time - gameStartTime;
            UpdateUIDisplay();
        }
    }

    /// <summary>
    /// UI表示を更新
    /// </summary>
    private void UpdateUIDisplay()
    {
        // 経過時間表示（分:秒形式）
        if (elapsedTimeDisplay != null)
        {
            int minutes = (int)(elapsedTime / 60f);
            int seconds = (int)(elapsedTime % 60f);
            elapsedTimeDisplay.text = $"{minutes:D2}:{seconds:D2}";
        }

        // スコア表示
        if (scoreDisplay != null)
        {
            scoreDisplay.text = $"Score: {totalScore}";
        }

        // 敵倒数表示
        if (enemyCountDisplay != null)
        {
            enemyCountDisplay.text = $"Enemies: {defeatedEnemyCount}";
        }
    }

    /// <summary>
    /// 敵が倒されたときに呼び出す
    /// </summary>
    public void OnEnemyDefeated(int score = 0)
    {
        if (!isGameActive)
            return;

        defeatedEnemyCount++;
        
        // スコアが指定されていなければデフォルト値を使用
        int earnedScore = score > 0 ? score : scorePerEnemy;
        totalScore += earnedScore;

        UpdateUIDisplay();

        Debug.Log($"敵を倒しました！ 倒数: {defeatedEnemyCount}, スコア: {totalScore}");
    }

    /// <summary>
    /// 経過時間を取得（秒単位）
    /// </summary>
    public float GetElapsedTime()
    {
        return elapsedTime;
    }

    /// <summary>
    /// 倒した敵の数を取得
    /// </summary>
    public int GetDefeatedEnemyCount()
    {
        return defeatedEnemyCount;
    }

    /// <summary>
    /// 現在のスコアを取得
    /// </summary>
    public int GetTotalScore()
    {
        return totalScore;
    }

    /// <summary>
    /// ゲームの状態を取得
    /// </summary>
    public bool IsGameActive()
    {
        return isGameActive;
    }

    /// <summary>
    /// ゲーム終了処理（敵全滅やボス戦勝利など）
    /// </summary>
    public void EndGame()
    {
        if (!isGameActive)
            return;

        isGameActive = false;
        
        Debug.Log($"=== ゲーム終了 ===");
        Debug.Log($"経過時間: {elapsedTime:F2}秒");
        Debug.Log($"倒した敵の数: {defeatedEnemyCount}");
        Debug.Log($"最終スコア: {totalScore}");

        // ゲーム終了UI表示などをここに追加

        StartCoroutine(ReturnToHomeScreenAfterDelay(2f));
    }

    /// <summary>
    /// プレイヤーがやられた場合
    /// </summary>
    public void OnPlayerDefeated()
    {
        if (!isGameActive)
            return;

        isGameActive = false;

        Debug.Log($"=== ゲームオーバー ===");
        Debug.Log($"経過時間: {elapsedTime:F2}秒");
        Debug.Log($"倒した敵の数: {defeatedEnemyCount}");
        Debug.Log($"獲得スコア: {totalScore}");

        // ゲームオーバーUI表示などをここに追加

        StartCoroutine(ReturnToHomeScreenAfterDelay(2f));
    }

    /// <summary>
    /// 指定時間後にホーム画面に戻る
    /// </summary>
    private IEnumerator ReturnToHomeScreenAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ReturnToHomeScreen();
    }

    /// <summary>
    /// ホーム画面に戻る
    /// </summary>
    public void ReturnToHomeScreen()
    {
        Time.timeScale = 1f; // タイムスケール正常化
        SceneManager.LoadScene("HomeScreen"); // ホーム画面シーン名を指定
    }

    /// <summary>
    /// ゲーム統計をリセット（新規ゲーム開始時）
    /// </summary>
    public void ResetGameStats()
    {
        gameStartTime = Time.time;
        elapsedTime = 0f;
        defeatedEnemyCount = 0;
        totalScore = 0;
        isGameActive = true;

        UpdateUIDisplay();

        Debug.Log("ゲーム統計をリセットしました");
    }
}
