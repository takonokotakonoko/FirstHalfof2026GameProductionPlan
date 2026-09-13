using UnityEngine;

// タスク12：プレイヤーの初期スポーン地点を、原点付近の基幹道路マス上に自動配置する。
// チャンク生成はランダムなため、原点がそのまま建物（敷地）の内部になり得るのを避ける目的。
// 画面暗転などの演出は現時点ではスコープ外（基本設計.md タスク12参照）。
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private ChunkManager chunkManager;
    [SerializeField] private Rigidbody playerRigidbody;
    [Tooltip("スポーン地点の探索基準となるワールド座標（通常はワールド原点）。")]
    [SerializeField] private Vector3 spawnSearchOrigin = Vector3.zero;
    [Tooltip("テレポート後にY座標へ加算する高さ。地面にめり込まないよう、道路面より少し高い位置に着地させ、あとは重力で自然落下させる。")]
    [SerializeField] private float spawnHeightOffset = 2f;

    private bool hasSpawned;

    private void Start()
    {
        if (chunkManager == null || playerRigidbody == null)
        {
            Debug.LogWarning("PlayerSpawner: chunkManagerまたはplayerRigidbodyが未設定です。");
            return;
        }

        Vector2Int targetCoord = chunkManager.GetChunkCoordForWorldPosition(spawnSearchOrigin);

        if (chunkManager.TryGetLoadedChunk(targetCoord, out GenerateClusterBuildings chunk))
        {
            SpawnOnChunk(chunk);
        }
        else
        {
            chunkManager.ChunkGenerated += HandleChunkGenerated;
        }
    }

    private void OnDestroy()
    {
        if (chunkManager != null)
        {
            chunkManager.ChunkGenerated -= HandleChunkGenerated;
        }
    }

    private void HandleChunkGenerated(Vector2Int coord, GenerateClusterBuildings chunk)
    {
        if (hasSpawned)
        {
            return;
        }

        Vector2Int targetCoord = chunkManager.GetChunkCoordForWorldPosition(spawnSearchOrigin);
        if (coord != targetCoord)
        {
            return;
        }

        chunkManager.ChunkGenerated -= HandleChunkGenerated;
        SpawnOnChunk(chunk);
    }

    private void SpawnOnChunk(GenerateClusterBuildings chunk)
    {
        hasSpawned = true;

        if (chunk.TryFindNearestMajorRoadCell(spawnSearchOrigin, out Vector3 result))
        {
            playerRigidbody.position = result + Vector3.up * spawnHeightOffset;
            playerRigidbody.linearVelocity = Vector3.zero;
        }
        else
        {
            Debug.LogWarning("PlayerSpawner: 基幹道路のマスが見つかりませんでした。プレイヤーは初期位置のままです。");
        }
    }
}
