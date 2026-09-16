using System.Collections.Generic;
using UnityEngine;

public class EnemySpawnManager : MonoBehaviour
{
    private class ActiveEnemy
    {
        public GameObject instance;
        public GameObject prefab;
    }

    [Header("Phase Settings")]
    [SerializeField] private EnemyPhaseSettings phaseSettings;

    [Header("Spawn Radius")]
    [Tooltip("プレイヤーに近すぎる位置には生成しない距離（未確定・仮値）。")]
    [SerializeField] private float minSpawnRadius = 15f;
    [Tooltip("この距離を超えた位置には生成しない（未確定・仮値）。")]
    [SerializeField] private float maxSpawnRadius = 40f;
    [Tooltip("生成する敵のY座標。地面が完全に平坦なため、プレイヤーのY座標を流用せず固定値で指定する。Enemy.prefabの場合、元のシーンで正しく接地していた高さが0.85だった。")]
    [SerializeField] private float spawnPositionY = 0.85f;

    [Header("Spawn Validation")]
    [Tooltip("生成候補位置が既存のColliderと重なっていないか判定する球の半径。敵の実際のサイズに近い値にする（未確定・仮値）。")]
    [SerializeField] private float spawnOverlapCheckRadius = 0.6f;
    [Tooltip("重ならない位置が見つかるまで候補をやり直す最大回数。すべて失敗した場合はその回の生成を見送る。")]
    [SerializeField] private int maxSpawnPositionAttempts = 10;
    [Tooltip("デバッグ用。trueにすると、重ならない位置が見つからなかった場合でも最後に試した候補位置へ強制的に生成する（埋まっている場所を目視で確認するため）。本来の（見送る）挙動を確認する場合はfalseにする。")]
    [SerializeField] private bool debugForceSpawnOnOverlapFailure = true;

    [Header("Despawn")]
    [Tooltip("この距離を超えた生存中の敵は即座に削除する（未確定・仮値）。")]
    [SerializeField] private float despawnRadius = 60f;
    [Tooltip("この距離を超えた生存中の敵が確率的な間引きの対象になる。despawnRadius未満にすること（未確定・仮値）。")]
    [SerializeField] private float thinningRadius = 45f;
    [Tooltip("間引き対象の敵に対し、1回の判定あたりで削除される確率（未確定・仮値）。")]
    [Range(0f, 1f)]
    [SerializeField] private float thinningChancePerCheck = 0.01f;

    [Header("Timing")]
    [SerializeField] private float updateIntervalSeconds = 0.5f;

    private Transform player;
    private EnemyPool pool;
    private float updateTimer;
    private float spawnTimer;
    private float elapsedSeconds;
    private EnemyPhaseSettings.Phase currentPhase;

    private readonly List<ActiveEnemy> activeEnemies = new List<ActiveEnemy>();

    private void Awake()
    {
        pool = new EnemyPool(transform);
    }

    private void Start()
    {
        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
        }
        else
        {
            Debug.LogWarning("EnemySpawnManager: Playerタグのオブジェクトが見つかりません。敵生成は動作しません。");
        }

        if (phaseSettings == null)
        {
            Debug.LogWarning("EnemySpawnManager: phaseSettingsが割り当てられていません。");
            return;
        }

        currentPhase = phaseSettings.GetActivePhase(elapsedSeconds);
    }

    private void Update()
    {
        if (player == null || phaseSettings == null)
        {
            return;
        }

        elapsedSeconds += Time.deltaTime;

        updateTimer -= Time.deltaTime;
        if (updateTimer <= 0f)
        {
            updateTimer = updateIntervalSeconds;
            RefreshPhase();
            DespawnOutOfRangeEnemies();
        }

        // 生成の間隔はフェーズごとに変えられるため、デスポーン判定とは別のタイマーで管理する。
        spawnTimer -= Time.deltaTime;
        if (currentPhase != null && spawnTimer <= 0f)
        {
            spawnTimer = currentPhase.spawnIntervalSeconds;
            TrySpawnEnemy();
        }
    }

    private void RefreshPhase()
    {
        EnemyPhaseSettings.Phase newPhase = phaseSettings.GetActivePhase(elapsedSeconds);
        if (newPhase == currentPhase)
        {
            return;
        }

        EnemyPhaseSettings.Phase previousPhase = currentPhase;
        currentPhase = newPhase;
        spawnTimer = 0f;

        Debug.Log($"EnemySpawnManager: フェーズ変更 {previousPhase?.phaseName ?? "(なし)"} → {currentPhase?.phaseName ?? "(なし)"}（経過時間: {elapsedSeconds:F1}秒）");

        if (previousPhase?.enemyTypes != null)
        {
            // 直前フェーズにのみ含まれ、新フェーズには含まれない種類の非表示在庫を即時破棄する。
            // プールが保持しているのは非アクティブな個体のみのため、画面上のアクティブな個体には影響しない。
            foreach (EnemyPhaseSettings.EnemyTypeEntry entry in previousPhase.enemyTypes)
            {
                if (entry.prefab == null || EnemyPhaseSettings.ContainsPrefab(currentPhase, entry.prefab))
                {
                    continue;
                }

                pool.ClearPoolFor(entry.prefab);
            }
        }

        // 新フェーズがclearPreviousPhaseEnemiesOnEnterを有効にしている場合、
        // 画面上にアクティブな個体のうち新フェーズに含まれない種類も即座に削除する。
        // falseの場合は何もしない＝距離超過・確率的な間引きで自然に退場するまで残る。
        if (currentPhase != null && currentPhase.clearPreviousPhaseEnemiesOnEnter)
        {
            int forceClearedCount = 0;
            for (int i = activeEnemies.Count - 1; i >= 0; i--)
            {
                ActiveEnemy enemy = activeEnemies[i];
                if (enemy.instance != null && EnemyPhaseSettings.ContainsPrefab(currentPhase, enemy.prefab))
                {
                    continue;
                }

                ReleaseActiveEnemyAt(i, keepInPool: false);
                forceClearedCount++;
            }

            if (forceClearedCount > 0)
            {
                Debug.Log($"EnemySpawnManager: フェーズ「{currentPhase.phaseName}」開始に伴い、前フェーズの敵を{forceClearedCount}体強制削除しました");
            }
        }
    }

    private void TrySpawnEnemy()
    {
        if (currentPhase == null || activeEnemies.Count >= currentPhase.maxActiveEnemies)
        {
            return;
        }

        GameObject prefab = phaseSettings.PickEnemyPrefab(currentPhase);
        if (prefab == null)
        {
            return;
        }

        if (!TryFindValidSpawnPosition(out Vector3 spawnPosition))
        {
            // 候補位置がすべて建物などと重なっていた。無理に埋め込まず、この回の生成を見送る
            // （次のspawnTimer周期でまた試す）。
            Debug.Log($"EnemySpawnManager: 生成位置が見つからず今回は見送り（{maxSpawnPositionAttempts}回試行、すべて何かと重なっていた）");
            return;
        }

        GameObject instance = pool.Get(prefab, spawnPosition, Quaternion.identity);
        activeEnemies.Add(new ActiveEnemy { instance = instance, prefab = prefab });

        Debug.Log($"EnemySpawnManager: 敵を生成 {prefab.name}（フェーズ: {currentPhase.phaseName}, 位置: {spawnPosition}, 現在数: {activeEnemies.Count}/{currentPhase.maxActiveEnemies}）");
    }

    // プレイヤー周囲のリング内でランダムな候補位置を複数回試し、既存のColliderと重ならない
    // 位置が見つかればtrueを返す。マインクラフトの自然湧きが「候補位置が塞がっていたら
    // 別の候補を試し、それでもダメなら今回は湧かせない」方式を採るのと同じ考え方。
    // トリガーColliderは実体のある障害物ではないため判定から除外する。
    private bool TryFindValidSpawnPosition(out Vector3 position)
    {
        Vector3 lastCandidate = default;
        for (int i = 0; i < maxSpawnPositionAttempts; i++)
        {
            Vector3 candidate = GetRandomSpawnPosition();
            lastCandidate = candidate;
            if (!Physics.CheckSphere(candidate, spawnOverlapCheckRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                position = candidate;
                return true;
            }
        }

        if (debugForceSpawnOnOverlapFailure)
        {
            // デバッグ用：本来は見送るべきだが、埋まっている様子を目視確認できるようあえて生成する。
            Debug.LogWarning($"EnemySpawnManager: 生成位置がColliderと重なっていましたが、デバッグ用フラグ（debugForceSpawnOnOverlapFailure）により強制的に生成しました（位置: {lastCandidate}）");
            position = lastCandidate;
            return true;
        }

        position = default;
        return false;
    }

    private Vector3 GetRandomSpawnPosition()
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Random.Range(minSpawnRadius, maxSpawnRadius);
        Vector3 horizontalOffset = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        Vector3 position = player.position + horizontalOffset;
        position.y = spawnPositionY;
        return position;
    }

    private void DespawnOutOfRangeEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            ActiveEnemy enemy = activeEnemies[i];
            if (enemy.instance == null)
            {
                // 撃破等で既にDestroyされ、このリストから未整理のまま残っていたケース。
                Debug.Log($"EnemySpawnManager: 消滅済みの個体をリストから除去（撃破等ですでにDestroyされていた, {enemy.prefab?.name}）");
                ReleaseActiveEnemyAt(i, keepInPool: false);
                continue;
            }

            float distance = Vector3.Distance(player.position, enemy.instance.transform.position);

            bool isBeyondDespawnRadius = distance > despawnRadius;
            bool shouldDespawn = isBeyondDespawnRadius;
            if (!shouldDespawn && distance > thinningRadius)
            {
                shouldDespawn = Random.value < thinningChancePerCheck;
            }

            if (!shouldDespawn)
            {
                continue;
            }

            bool keepInPool = EnemyPhaseSettings.ContainsPrefab(currentPhase, enemy.prefab);
            string reason = isBeyondDespawnRadius ? "距離超過" : "確率的な間引き";
            Debug.Log($"EnemySpawnManager: 敵をデスポーン {enemy.prefab.name}（理由: {reason}, 距離: {distance:F1}, プールに戻す: {keepInPool}）");
            ReleaseActiveEnemyAt(i, keepInPool);
        }
    }

    // activeEnemies[index]をプールへ返却（またはkeepInPool=falseならDestroy）し、リストから取り除く。
    // instanceが既に破棄済み（撃破等）の場合はプールに触れず、リストから取り除くだけ。
    private void ReleaseActiveEnemyAt(int index, bool keepInPool)
    {
        ActiveEnemy enemy = activeEnemies[index];
        if (enemy.instance != null)
        {
            pool.Release(enemy.prefab, enemy.instance, keepInPool);
        }

        activeEnemies.RemoveAt(index);
    }

    private void OnDrawGizmosSelected()
    {
        if (player == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(player.position, minSpawnRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(player.position, maxSpawnRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(player.position, despawnRadius);
    }
}
