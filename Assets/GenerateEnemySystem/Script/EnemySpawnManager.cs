using System.Collections.Generic;
using UnityEngine;

public class EnemySpawnManager : MonoBehaviour
{
    private class ActiveEnemy
    {
        public GameObject instance;
        public GameObject prefab;
    }

    // 敵の種類ごとのステータス（ギャング・警察・軍隊など）。生成時にプレファブへ上書きする。
    [System.Serializable]
    private class EnemyTypeStats
    {
        [JpLabel("種類名")] public string typeName;
        [Min(0f)] [JpLabel("攻撃力")] public float attackDamage = 10f;
        [Min(1f)] [JpLabel("体力")] public float maxHealth = 20f;
        [Min(0f)] [JpLabel("移動速度（m/s）")] public float moveSpeed = 0.5f;
        [Min(0.01f)] [JpLabel("攻撃間隔（秒）")] public float attackInterval = 2f;
        [Min(0f)] [JpLabel("攻撃範囲（半径m）")] public float attackRange = 0.5f;
    }

    // プレイヤーからの距離ごとの、敵の種類の出現割合。
    [System.Serializable]
    private class SpawnDistanceBand
    {
        [Tooltip("プレイヤーからこの距離（m）以内に生成した敵に、この割合を使う。距離の小さい順に並べる。")]
        [JpLabel("この距離まで（m）")] public float maxDistance = 5f;
        [Tooltip("「敵の種類」と同じ順番で出現の重みを並べる。例：ギャング50・警察40・軍隊10")]
        [JpLabel("種類ごとの出現割合")] public float[] typeWeights;
    }

    [Header("Phase Settings")]
    [SerializeField] [JpLabel("フェーズ設定")] private EnemyPhaseSettings phaseSettings;

    [Header("Enemy Types")]
    [Tooltip("生成した敵に上書きするステータス。空の場合はプレファブの値のまま生成する。")]
    [SerializeField] [JpLabel("敵の種類")] private EnemyTypeStats[] enemyTypes =
    {
        new EnemyTypeStats { typeName = "ギャング", attackDamage = 10f, maxHealth = 20f, moveSpeed = 0.5f, attackInterval = 2f, attackRange = 0.5f },
        new EnemyTypeStats { typeName = "警察", attackDamage = 15f, maxHealth = 30f, moveSpeed = 0.5f, attackInterval = 2f, attackRange = 0.5f },
        new EnemyTypeStats { typeName = "軍隊", attackDamage = 30f, maxHealth = 60f, moveSpeed = 0.5f, attackInterval = 2f, attackRange = 0.5f }
    };
    [SerializeField] [JpLabel("距離ごとの出現割合")] private SpawnDistanceBand[] spawnDistanceBands =
    {
        new SpawnDistanceBand { maxDistance = 5f, typeWeights = new[] { 100f, 0f, 0f } },
        new SpawnDistanceBand { maxDistance = 10f, typeWeights = new[] { 50f, 40f, 10f } },
        new SpawnDistanceBand { maxDistance = 15f, typeWeights = new[] { 10f, 30f, 60f } }
    };

    [Header("Stealth Scaling (per player level, bosses excluded)")]
    [Tooltip("プレイヤーのレベルが1上がるごとの、敵の最大体力の増加率。0.2 = 20%")]
    [SerializeField, Min(0f)] [JpLabel("体力の増加率（1レベルごと）")] private float healthIncreasePerLevel = 0.2f;
    [Tooltip("体力の増加はこのレベルまで。これより上のレベルでは増えない。")]
    [SerializeField, Min(1)] [JpLabel("体力が増えるレベルの上限")] private int healthScalingMaxLevel = 11;
    [Tooltip("プレイヤーのレベルが1上がるごとの、敵の移動速度の増加率。0.1 = 10%")]
    [SerializeField, Min(0f)] [JpLabel("移動速度の増加率（1レベルごと）")] private float moveSpeedIncreasePerLevel = 0.1f;
    [SerializeField, Min(0f)] [JpLabel("移動速度の上限（m/s）")] private float maxEnemyMoveSpeed = 2f;
    [Tooltip("プレイヤーのレベルが1上がるごとに、敵の生成間隔を短くする秒数。")]
    [SerializeField, Min(0f)] [JpLabel("生成間隔の短縮（1レベルごと・秒）")] private float spawnIntervalDecreasePerLevel = 0.2f;
    [SerializeField, Min(0.01f)] [JpLabel("生成間隔の下限（秒）")] private float minSpawnInterval = 1f;

    [Header("Spawn Radius")]
    [Tooltip("プレイヤーに近すぎる位置には生成しない距離。")]
    [SerializeField] [JpLabel("最小生成半径")] private float minSpawnRadius = 2f;
    [Tooltip("この距離を超えた位置には生成しない。距離ごとの出現割合の最大距離に合わせる。")]
    [SerializeField] [JpLabel("最大生成半径")] private float maxSpawnRadius = 15f;
    [Tooltip("生成する敵のY座標。地面が完全に平坦なため、プレイヤーのY座標を流用せず固定値で指定する。Enemy.prefabの場合、元のシーンで正しく接地していた高さが0.85だった。")]
    [SerializeField] [JpLabel("生成Y座標")] private float spawnPositionY = 0.85f;

    [Header("Spawn Validation")]
    [Tooltip("生成可能なセル（道路・横断歩道・歩道・信号機セル）の判定に使う。未設定なら自動で探す。シーンに無い場合はセル判定を行わない（テストシーン用）。")]
    [SerializeField] [JpLabel("チャンクマネージャー")] private ChunkManager chunkManager;
    [Tooltip("生成候補位置が既存のColliderと重なっていないか判定する球の半径。地面タイルとの接触は重なりとみなさない。敵の実際のサイズに近い値にする（未確定・仮値）。")]
    [SerializeField] [JpLabel("重なり判定半径")] private float spawnOverlapCheckRadius = 0.6f;
    [Tooltip("重ならない位置が見つかるまで候補をやり直す最大回数。すべて失敗した場合はその回の生成を見送る。")]
    [SerializeField] [JpLabel("生成位置 試行回数上限")] private int maxSpawnPositionAttempts = 10;
    [Tooltip("デバッグ用。trueにすると、重ならない位置が見つからなかった場合でも最後に試した候補位置へ強制的に生成する（埋まっている場所を目視で確認するため）。ただし生成可能なセル（道路等）の判定を通った候補に限り、敷地の上には強制生成しない。本来の（見送る）挙動を確認する場合はfalseにする。")]
    [SerializeField] [JpLabel("重なり時に強制生成（デバッグ）")] private bool debugForceSpawnOnOverlapFailure = true;

    [Header("Despawn")]
    [Tooltip("この距離を超えた生存中の敵は即座に削除する（未確定・仮値）。")]
    [SerializeField] [JpLabel("デスポーン半径")] private float despawnRadius = 60f;
    [Tooltip("この距離を超えた生存中の敵が確率的な間引きの対象になる。despawnRadius未満にすること（未確定・仮値）。")]
    [SerializeField] [JpLabel("間引き半径")] private float thinningRadius = 45f;
    [Tooltip("間引き対象の敵に対し、1回の判定あたりで削除される確率（未確定・仮値）。")]
    [Range(0f, 1f)]
    [SerializeField] [JpLabel("間引き確率（判定あたり）")] private float thinningChancePerCheck = 0.01f;

    [Header("Timing")]
    [SerializeField] [JpLabel("更新間隔（秒）")] private float updateIntervalSeconds = 0.5f;

    private Transform player;
    private EnemyPool pool;
    private float updateTimer;
    private float spawnTimer;
    private float elapsedSeconds;
    private float spawnIntervalAdjustment;
    private int playerLevel = 1;
    private EnemyPhaseSettings.Phase currentPhase;

    public bool CanDecreaseSpawnInterval =>
        CanAdjustSpawnInterval(-1f, 1f, float.MaxValue);

    private readonly List<ActiveEnemy> activeEnemies = new List<ActiveEnemy>();
    private readonly Collider[] overlapResults = new Collider[16];

    private void Awake()
    {
        pool = new EnemyPool(transform);

        if (chunkManager == null)
        {
            chunkManager = FindFirstObjectByType<ChunkManager>();
        }
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

        if (chunkManager == null)
        {
            Debug.LogWarning("EnemySpawnManager: ChunkManagerが見つかりません。道路上かどうかのセル判定を行わずに生成します。");
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
            spawnTimer = GetCurrentSpawnInterval();
            TrySpawnEnemy();
        }
    }

    public void AdjustSpawnInterval(float adjustment)
    {
        AdjustSpawnInterval(adjustment, 1f, float.MaxValue);
    }

    public bool CanAdjustSpawnInterval(float adjustment, float minimumInterval, float maximumInterval)
    {
        if (currentPhase == null
            || adjustment == 0f
            || minimumInterval <= 0f
            || maximumInterval < minimumInterval)
        {
            return false;
        }

        float currentInterval = GetCurrentSpawnInterval();
        float targetInterval = Mathf.Clamp(currentInterval + adjustment, minimumInterval, maximumInterval);
        return !Mathf.Approximately(currentInterval, targetInterval);
    }

    public void AdjustSpawnInterval(float adjustment, float minimumInterval, float maximumInterval)
    {
        if (currentPhase == null
            || adjustment == 0f
            || minimumInterval <= 0f
            || maximumInterval < minimumInterval)
        {
            Debug.LogWarning("EnemySpawnManager: 出現間隔の増減量と最小値・最大値を確認してください。", this);
            return;
        }

        float currentInterval = GetCurrentSpawnInterval();
        float targetInterval = Mathf.Clamp(currentInterval + adjustment, minimumInterval, maximumInterval);
        spawnIntervalAdjustment = targetInterval - currentPhase.spawnIntervalSeconds + GetLevelIntervalDecrease();
        spawnTimer = Mathf.Min(spawnTimer, targetInterval);
    }

    /// <summary>
    /// プレイヤーのレベルを伝えます。これ以降に生成する敵の体力・移動速度と、生成間隔に反映されます。
    /// </summary>
    public void SetPlayerLevel(int level)
    {
        playerLevel = Mathf.Max(1, level);
        spawnTimer = Mathf.Min(spawnTimer, GetCurrentSpawnInterval());
    }

    private float GetCurrentSpawnInterval()
    {
        return currentPhase == null
            ? minSpawnInterval
            : Mathf.Max(minSpawnInterval,
                currentPhase.spawnIntervalSeconds + spawnIntervalAdjustment - GetLevelIntervalDecrease());
    }

    private float GetLevelIntervalDecrease()
    {
        return spawnIntervalDecreasePerLevel * (playerLevel - 1);
    }

    // 生成した敵に、出現距離に応じた種類のステータスとレベルによる強化を適用する
    private void ApplyEnemyStats(GameObject instance, float distanceFromPlayer)
    {
        EnemyHealth health = instance.GetComponent<EnemyHealth>();
        if (health != null && health.IsBoss)
        {
            return;
        }

        EnemyTypeStats stats = PickEnemyType(distanceFromPlayer);
        if (stats == null)
        {
            return;
        }

        int levelsAboveOne = playerLevel - 1;
        int healthLevelsAboveOne = Mathf.Min(playerLevel, healthScalingMaxLevel) - 1;

        if (health != null)
        {
            health.SetMaxHealth(stats.maxHealth * (1f + healthIncreasePerLevel * Mathf.Max(0, healthLevelsAboveOne)));
        }

        EnemyMove move = instance.GetComponent<EnemyMove>();
        if (move != null)
        {
            float speed = stats.moveSpeed * (1f + moveSpeedIncreasePerLevel * levelsAboveOne);
            move.SetMoveSpeed(Mathf.Min(speed, Mathf.Max(stats.moveSpeed, maxEnemyMoveSpeed)));
        }

        EnemyAttack attack = instance.GetComponent<EnemyAttack>();
        if (attack != null)
        {
            attack.SetStats(stats.attackDamage, stats.attackInterval, stats.attackRange);
        }

        instance.name = $"{instance.name.Split('（')[0]}（{stats.typeName}）";
    }

    private EnemyTypeStats PickEnemyType(float distanceFromPlayer)
    {
        if (enemyTypes == null || enemyTypes.Length == 0)
        {
            return null;
        }

        SpawnDistanceBand band = null;
        if (spawnDistanceBands != null)
        {
            // 距離の小さい順に見て、最初に当てはまる距離帯を使う。どれにも当てはまらなければ最後の距離帯を使う。
            foreach (SpawnDistanceBand candidate in spawnDistanceBands)
            {
                if (candidate == null)
                {
                    continue;
                }

                band = candidate;
                if (distanceFromPlayer <= candidate.maxDistance)
                {
                    break;
                }
            }
        }

        if (band == null || band.typeWeights == null)
        {
            return enemyTypes[0];
        }

        float totalWeight = 0f;
        int count = Mathf.Min(band.typeWeights.Length, enemyTypes.Length);
        for (int i = 0; i < count; i++)
        {
            totalWeight += Mathf.Max(0f, band.typeWeights[i]);
        }

        if (totalWeight <= 0f)
        {
            return enemyTypes[0];
        }

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        for (int i = 0; i < count; i++)
        {
            cumulative += Mathf.Max(0f, band.typeWeights[i]);
            if (roll <= cumulative)
            {
                return enemyTypes[i];
            }
        }

        return enemyTypes[count - 1];
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
            // 候補位置がすべて敷地の上か、何かと重なっていた。無理に埋め込まず、この回の生成を見送る
            // （次のspawnTimer周期でまた試す）。
            Debug.Log($"EnemySpawnManager: 生成位置が見つからず今回は見送り（{maxSpawnPositionAttempts}回試行、すべて敷地の上か何かと重なっていた）");
            return;
        }

        GameObject instance = pool.Get(prefab, spawnPosition, Quaternion.identity);
        activeEnemies.Add(new ActiveEnemy { instance = instance, prefab = prefab });

        Vector3 offsetFromPlayer = spawnPosition - player.position;
        offsetFromPlayer.y = 0f;
        ApplyEnemyStats(instance, offsetFromPlayer.magnitude);

        Debug.Log($"EnemySpawnManager: 敵を生成 {prefab.name}（フェーズ: {currentPhase.phaseName}, 位置: {spawnPosition}, 現在数: {activeEnemies.Count}/{currentPhase.maxActiveEnemies}）");
    }

    // プレイヤー周囲のリング内でランダムな候補位置を複数回試し、既存のColliderと重ならない
    // 位置が見つかればtrueを返す。マインクラフトの自然湧きが「候補位置が塞がっていたら
    // 別の候補を試し、それでもダメなら今回は湧かせない」方式を採るのと同じ考え方。
    // トリガーColliderは実体のある障害物ではないため判定から除外する。
    // 判定は2段階：①生成データ上で敷地（建物の足元）でないか、②Colliderと重なっていないか。
    // ①は物理判定ではColliderの隙間などを見逃すため、チャンクの生成データ（landMap等）で直接確かめる。
    private bool TryFindValidSpawnPosition(out Vector3 position)
    {
        Vector3 lastOverlappingCandidate = default;
        bool hasOverlappingCandidate = false;
        for (int i = 0; i < maxSpawnPositionAttempts; i++)
        {
            Vector3 candidate = GetRandomSpawnPosition();
            if (!IsSpawnableCell(candidate))
            {
                continue;
            }

            if (!IsOverlappingObstacle(candidate))
            {
                position = candidate;
                return true;
            }

            lastOverlappingCandidate = candidate;
            hasOverlappingCandidate = true;
        }

        // 強制生成は①を通った（敷地ではない）候補に限る。敷地の上に出してしまうと、このデバッグ機能で
        // 確認したい「Colliderとの重なり」とは別の不具合（建物の中への生成）を再現してしまうため。
        if (debugForceSpawnOnOverlapFailure && hasOverlappingCandidate)
        {
            // デバッグ用：本来は見送るべきだが、埋まっている様子を目視確認できるようあえて生成する。
            Debug.LogWarning($"EnemySpawnManager: 生成位置がColliderと重なっていましたが、デバッグ用フラグ（debugForceSpawnOnOverlapFailure）により強制的に生成しました（位置: {lastOverlappingCandidate}）");
            position = lastOverlappingCandidate;
            return true;
        }

        position = default;
        return false;
    }

    // 候補位置が道路・横断歩道・歩道・信号機セルの上ならtrue。読み込み済みでないチャンク（生成中を含む）の上は
    // 地面の有無も分からないためfalseにする。ChunkManagerが無いシーンでは判定を行わない。
    private bool IsSpawnableCell(Vector3 candidate)
    {
        if (chunkManager == null)
        {
            return true;
        }

        Vector2Int coord = chunkManager.GetChunkCoordForWorldPosition(candidate);
        return chunkManager.TryGetLoadedChunk(coord, out GenerateClusterBuildings chunk)
            && chunk.IsEnemySpawnableAtWorldPosition(candidate);
    }

    // 地面タイルに接しているだけの場合は重なりとみなさない（歩道の段差などで判定球が地面に触れても見送らない）。
    // 建物・信号機・他の敵などは従来どおり障害物として扱う。
    private bool IsOverlappingObstacle(Vector3 candidate)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(candidate, spawnOverlapCheckRadius, overlapResults, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapResults[i];
            GenerateClusterBuildings chunk = hit.GetComponentInParent<GenerateClusterBuildings>();
            if (chunk != null && chunk.IsWalkableSurfaceCollider(hit))
            {
                continue;
            }

            return true;
        }

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
