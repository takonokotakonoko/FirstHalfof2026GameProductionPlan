using System;
using System.Collections.Generic;
using UnityEngine;

public class ChunkManager : MonoBehaviour
{
    [Header("Chunk Prefab")]
    [Tooltip("autoGenerateOnAwakeがfalseに設定されたGenerateClusterBuildingsプレファブ。")]
    [SerializeField] private GenerateClusterBuildings chunkPrefab;
    [SerializeField] private int globalSeed = 12345;

    [Header("Load Radius (auto)")]
    [SerializeField] private float baseSpeedMetersPerMinute = 600f;
    [Tooltip("移動速度バフによる加算分（＋α）。")]
    [SerializeField] private float speedBuffMetersPerMinute = 0f;
    [SerializeField] private float gameTimeMinutes = 20f;
    [Tooltip("ボス戦などによる時間バッファ（＋α）。")]
    [SerializeField] private float bossFightBufferMinutes = 5f;
    [SerializeField] private float safetyMultiplier = 2f;

    [Header("Load Radius (manual override)")]
    [Tooltip("trueにすると上の自動計算式を無視してmanualRadiusMetersを使う（動作テスト用）。")]
    [SerializeField] private bool useManualRadius = false;
    [SerializeField] private float manualRadiusMeters = 3000f;

    [Header("Streaming")]
    [SerializeField] private float updateIntervalSeconds = 0.5f;
    [Tooltip("読み込み境界での連続ロード/アンロードを防ぐための追加マージン（チャンク数換算）。")]
    [SerializeField] private int unloadMarginChunks = 1;
    [Tooltip("同時に生成コルーチンを実行中にできるチャンク数の上限。GenerateChunkは複数フレームに分割されたコルーチンで実行されるため、この値は「1tickで開始する数」ではなく「同時に生成が進行中でよい数」を意味する。大きくしすぎると、1フレームあたりに複数チャンク分の生成ステップが重なって再びフレームが重くなる。")]
    [SerializeField] private int maxChunkLoadsPerTick = 4;
    [Tooltip("1回のUpdateで非アクティブ化（休眠キャッシュ送り）するチャンク数の上限。")]
    [SerializeField] private int maxChunkUnloadsPerTick = 8;

    [Header("Chunk Cache")]
    [Tooltip("読み込み範囲外に出たチャンクを即Destroyせず、非アクティブ化した状態でこの数まで保持する。範囲内に戻った際はSetActive(true)するだけで済み、Instantiate/GenerateChunkのやり直しが発生しない。\n" +
        "注意：タイルのオブジェクトプーリング（パフォーマンス課題1）が未対策のため、1チャンクあたり実質数万GameObjectになり得る。この値はチャンク数ではなく「数万×この値」個のGameObjectを同時に非アクティブ保持するコストとして見積もること。大きくしすぎるとGC負荷・メモリ使用量が増え、往復時の再生成コストを削っても総合的には悪化し得る。")]
    [SerializeField] private int maxDormantChunks = 8;
    [Tooltip("休眠キャッシュが上限を超えた場合に、1回のUpdateで実際にDestroy（メモリ解放）するチャンク数の上限。プレイヤーから遠いチャンクから優先的に破棄される。")]
    [SerializeField] private int maxChunkEvictionsPerTick = 2;

    private Transform player;
    private float updateTimer;
    private readonly Dictionary<Vector2Int, GenerateClusterBuildings> loadedChunks = new Dictionary<Vector2Int, GenerateClusterBuildings>();

    // 生成コルーチンが完了するまでの間、対象チャンク座標を保持する（パフォーマンス課題2対策）。
    // GenerateChunkが複数フレームに分割されたコルーチンになったため、生成完了前に同じ座標へ
    // 重複してLoadChunkが呼ばれる（＝二重にInstantiateされる）のを防ぐために必要。
    private readonly HashSet<Vector2Int> loadingChunks = new HashSet<Vector2Int>();

    // 読み込み範囲外だがDestroyはされていないチャンク（非アクティブ化のみ）。
    // GameObject階層と破壊済み建物の欠落状態をそのまま保持するため、再訪時は再生成不要。
    private readonly Dictionary<Vector2Int, GenerateClusterBuildings> dormantChunks = new Dictionary<Vector2Int, GenerateClusterBuildings>();

    // チャンクGameObject自体が破棄されても、破壊済み建物の記録はここに残り続ける（プレイセッション中は保持）。
    private readonly Dictionary<Vector2Int, HashSet<int>> destroyedLotIdsByChunk = new Dictionary<Vector2Int, HashSet<int>>();

    private float ChunkWorldWidth => chunkPrefab.GridWidth * chunkPrefab.CellWidth;
    private float ChunkWorldHeight => chunkPrefab.GridHeight * chunkPrefab.CellHeight;

    public float LoadRadiusMeters =>
        useManualRadius
            ? manualRadiusMeters
            : (baseSpeedMetersPerMinute + speedBuffMetersPerMinute)
              * (gameTimeMinutes + bossFightBufferMinutes)
              * safetyMultiplier;

    private void OnEnable()
    {
        BuildingInstance.Destroyed += HandleBuildingDestroyed;
    }

    private void OnDisable()
    {
        BuildingInstance.Destroyed -= HandleBuildingDestroyed;
    }

    private void HandleBuildingDestroyed(Vector2Int coord, int lotId)
    {
        if (!destroyedLotIdsByChunk.TryGetValue(coord, out HashSet<int> lotIds))
        {
            lotIds = new HashSet<int>();
            destroyedLotIdsByChunk[coord] = lotIds;
        }

        lotIds.Add(lotId);
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
            Debug.LogWarning("ChunkManager: Playerタグのオブジェクトが見つかりません。チャンクストリーミングは動作しません。");
        }

        if (chunkPrefab == null)
        {
            Debug.LogWarning("ChunkManager: chunkPrefabが割り当てられていません。");
        }
    }

    private void Update()
    {
        if (player == null || chunkPrefab == null)
        {
            return;
        }

        updateTimer -= Time.deltaTime;
        if (updateTimer > 0f)
        {
            return;
        }

        updateTimer = updateIntervalSeconds;
        RefreshChunks();
    }

    private void RefreshChunks()
    {
        float chunkWidth = ChunkWorldWidth;
        float chunkHeight = ChunkWorldHeight;

        if (chunkWidth <= 0f || chunkHeight <= 0f)
        {
            return;
        }

        float loadRadius = LoadRadiusMeters;
        float unloadRadius = loadRadius + unloadMarginChunks * Mathf.Max(chunkWidth, chunkHeight);

        int chunkRangeX = Mathf.CeilToInt(loadRadius / chunkWidth) + 1;
        int chunkRangeY = Mathf.CeilToInt(loadRadius / chunkHeight) + 1;

        Vector2Int playerChunk = WorldToChunkCoord(player.position, chunkWidth, chunkHeight);
        HashSet<Vector2Int> neededChunks = new HashSet<Vector2Int>();

        for (int dx = -chunkRangeX; dx <= chunkRangeX; dx++)
        {
            for (int dy = -chunkRangeY; dy <= chunkRangeY; dy++)
            {
                Vector2Int coord = new Vector2Int(playerChunk.x + dx, playerChunk.y + dy);
                if (GetDistanceToChunkCenter(coord, chunkWidth, chunkHeight) <= loadRadius)
                {
                    neededChunks.Add(coord);
                }
            }
        }

        // 休眠キャッシュ（非アクティブ化のみでDestroyされていないチャンク）が再び必要になった場合は
        // SetActive(true)するだけで済ませ、Instantiate/GenerateChunkをやり直さない。
        List<Vector2Int> toReactivate = null;
        foreach (Vector2Int coord in neededChunks)
        {
            if (!loadedChunks.ContainsKey(coord) && dormantChunks.ContainsKey(coord))
            {
                toReactivate ??= new List<Vector2Int>();
                toReactivate.Add(coord);
            }
        }

        if (toReactivate != null)
        {
            foreach (Vector2Int coord in toReactivate)
            {
                GenerateClusterBuildings chunk = dormantChunks[coord];
                dormantChunks.Remove(coord);
                if (chunk != null)
                {
                    chunk.gameObject.SetActive(true);
                    loadedChunks[coord] = chunk;
                }
            }
        }

        // 未ロード分（休眠キャッシュにも存在せず、生成コルーチンも実行中でない＝初見のチャンク）を
        // 近い順に並べ、同時に生成中のチャンク数がmaxChunkLoadsPerTickを超えないようにロードする。
        // GenerateChunkは複数フレームに分割されたコルーチンなので、ここでの「ロード」は
        // 「生成コルーチンを開始する」ことを指し、完了はOnChunkGeneratedで検知する。
        List<Vector2Int> pendingLoads = null;
        foreach (Vector2Int coord in neededChunks)
        {
            if (!loadedChunks.ContainsKey(coord) && !loadingChunks.Contains(coord))
            {
                pendingLoads ??= new List<Vector2Int>();
                pendingLoads.Add(coord);
            }
        }

        if (pendingLoads != null)
        {
            pendingLoads.Sort((a, b) =>
                GetDistanceToChunkCenter(a, chunkWidth, chunkHeight)
                    .CompareTo(GetDistanceToChunkCenter(b, chunkWidth, chunkHeight)));

            int availableSlots = Mathf.Max(0, maxChunkLoadsPerTick - loadingChunks.Count);
            int loadCount = Mathf.Min(availableSlots, pendingLoads.Count);
            for (int i = 0; i < loadCount; i++)
            {
                LoadChunk(pendingLoads[i], chunkWidth, chunkHeight);
            }
        }

        // 範囲外に出たチャンクはDestroyせず非アクティブ化のみ行い、休眠キャッシュへ移す。
        // GameObject階層（破壊済み建物の欠落状態を含む）をそのまま保持することで、
        // チャンク境界付近を往復しても再生成コストが発生しない。
        List<Vector2Int> toDeactivate = null;
        foreach (KeyValuePair<Vector2Int, GenerateClusterBuildings> kvp in loadedChunks)
        {
            if (GetDistanceToChunkCenter(kvp.Key, chunkWidth, chunkHeight) > unloadRadius)
            {
                toDeactivate ??= new List<Vector2Int>();
                toDeactivate.Add(kvp.Key);
            }
        }

        if (toDeactivate != null)
        {
            int deactivateCount = Mathf.Min(maxChunkUnloadsPerTick, toDeactivate.Count);
            for (int i = 0; i < deactivateCount; i++)
            {
                DeactivateChunk(toDeactivate[i]);
            }
        }

        // 休眠キャッシュが上限を超えた分だけ、プレイヤーから遠い順に実際にDestroyしてメモリを解放する。
        // 破壊済み建物のIDはdestroyedLotIdsByChunkに残り続けるため、将来同じ座標を再訪して
        // ゼロから再生成することになっても破壊状態は正しく復元される。
        if (dormantChunks.Count > maxDormantChunks)
        {
            List<Vector2Int> evictionCandidates = new List<Vector2Int>(dormantChunks.Keys);
            evictionCandidates.Sort((a, b) =>
                GetDistanceToChunkCenter(b, chunkWidth, chunkHeight)
                    .CompareTo(GetDistanceToChunkCenter(a, chunkWidth, chunkHeight)));

            int evictCount = Mathf.Min(maxChunkEvictionsPerTick, dormantChunks.Count - maxDormantChunks);
            for (int i = 0; i < evictCount; i++)
            {
                EvictChunk(evictionCandidates[i]);
            }
        }
    }

    private float GetDistanceToChunkCenter(Vector2Int coord, float chunkWidth, float chunkHeight)
    {
        Vector3 origin = ChunkCoordToWorldOrigin(coord, chunkWidth, chunkHeight);
        Vector2 chunkCenter = new Vector2(origin.x + chunkWidth * 0.5f, origin.z + chunkHeight * 0.5f);
        Vector2 playerPos = new Vector2(player.position.x, player.position.z);
        return Vector2.Distance(playerPos, chunkCenter);
    }

    private void LoadChunk(Vector2Int coord, float chunkWidth, float chunkHeight)
    {
        Vector3 origin = ChunkCoordToWorldOrigin(coord, chunkWidth, chunkHeight);
        GenerateClusterBuildings chunk = Instantiate(chunkPrefab, origin, Quaternion.identity, transform);
        chunk.name = $"Chunk_{coord.x}_{coord.y}";
        destroyedLotIdsByChunk.TryGetValue(coord, out HashSet<int> destroyedLotIds);
        loadingChunks.Add(coord);
        chunk.GenerateChunk(HashChunkCoord(globalSeed, coord), coord, destroyedLotIds, globalSeed, () => OnChunkGenerated(coord, chunk));
    }

    // GenerateChunkのコルーチンが完了した時点で呼ばれる。生成中にプレイヤーが移動して
    // 読み込み範囲外になっていても、ひとまずloadedChunksへ入れる（次のRefreshChunksで
    // 通常通り休眠キャッシュへ回収される）。
    private void OnChunkGenerated(Vector2Int coord, GenerateClusterBuildings chunk)
    {
        loadingChunks.Remove(coord);

        if (chunk == null)
        {
            return;
        }

        loadedChunks[coord] = chunk;
    }

    private void DeactivateChunk(Vector2Int coord)
    {
        if (!loadedChunks.TryGetValue(coord, out GenerateClusterBuildings chunk))
        {
            return;
        }

        loadedChunks.Remove(coord);

        if (chunk == null)
        {
            return;
        }

        chunk.gameObject.SetActive(false);
        dormantChunks[coord] = chunk;
    }

    private void EvictChunk(Vector2Int coord)
    {
        if (!dormantChunks.TryGetValue(coord, out GenerateClusterBuildings chunk))
        {
            return;
        }

        dormantChunks.Remove(coord);

        if (chunk != null)
        {
            Destroy(chunk.gameObject);
        }
    }

    private static Vector2Int WorldToChunkCoord(Vector3 worldPosition, float chunkWidth, float chunkHeight)
    {
        int x = Mathf.FloorToInt(worldPosition.x / chunkWidth);
        int y = Mathf.FloorToInt(worldPosition.z / chunkHeight);
        return new Vector2Int(x, y);
    }

    private static Vector3 ChunkCoordToWorldOrigin(Vector2Int coord, float chunkWidth, float chunkHeight)
    {
        return new Vector3(coord.x * chunkWidth, 0f, coord.y * chunkHeight);
    }

    // グローバルシードとチャンク座標から決定論的な乱数シードを作る。
    // 同じチャンク座標には常に同じレイアウトが再生成される。
    private static int HashChunkCoord(int seed, Vector2Int coord)
    {
        unchecked
        {
            int hash = (seed * 486187739 + coord.x * 73856093) ^ (coord.y * 19349663);
            return hash == 0 ? 1 : hash;
        }
    }

    private void OnDrawGizmos()
    {
        if (player == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(player.position, LoadRadiusMeters);
    }
}
