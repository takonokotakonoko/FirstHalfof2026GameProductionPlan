using UnityEngine;

// タスク12：プレイヤーの初期スポーン地点を、原点付近の基幹道路マス上に自動配置する。
// チャンク生成はランダムなため、原点がそのまま建物（敷地）の内部になり得るのを避ける目的。
// 画面暗転などの演出は現時点ではスコープ外（基本設計.md タスク12参照）。
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] [JpLabel("チャンクマネージャー")] private ChunkManager chunkManager;
    [SerializeField] [JpLabel("プレイヤーRigidbody")] private Rigidbody playerRigidbody;
    [Tooltip("スポーン地点の探索基準となるワールド座標（通常はワールド原点）。")]
    [SerializeField] [JpLabel("スポーン探索基準座標")] private Vector3 spawnSearchOrigin = Vector3.zero;
    [Tooltip("テレポート後にY座標へ加算する高さ。地面にめり込まないよう、道路面より少し高い位置に着地させ、あとは重力で自然落下させる。")]
    [SerializeField] [JpLabel("スポーン高さオフセット")] private float spawnHeightOffset = 4f;
    // デバッグ用スイッチ。プレイヤーが着地に失敗する（空中で止まる/地面をすり抜ける）不具合の調査時に、
    // 「TryFindNearestMajorRoadCellの座標計算がおかしいのか」と「それ以外（Rigidbody設定やタイミング）が
    // おかしいのか」を切り分けるために追加した。trueにすると道路探索を経由せず、spawnSearchOriginの
    // 真上・高さspawnHeightOffsetへ単純にテレポートする。通常運用ではfalseのままでよい。
    [Tooltip("デバッグ用。trueの場合はTryFindNearestMajorRoadCellによる道路探索を行わず、spawnSearchOriginのXZ真上・高さspawnHeightOffsetへ固定でテレポートする。「本当に地面を参照した位置に出ているか」を切り分けるためのスイッチ。")]
    [SerializeField] [JpLabel("固定スポーン位置を使用（デバッグ）")] private bool debugUseFixedSpawnPosition = false;

    private bool hasSpawned;

    private void Awake()
    {
        // Inspectorの割り当て漏れでスポーン制御ごと無効化されるのを避ける。
        if (playerRigidbody == null)
        {
            playerRigidbody = GetComponent<Rigidbody>();
        }

        if (chunkManager == null)
        {
            chunkManager = FindFirstObjectByType<ChunkManager>();
        }
    }

    private void Start()
    {
        if (chunkManager == null || playerRigidbody == null)
        {
            Debug.LogError($"PlayerSpawner: 参照を解決できません（chunkManager={chunkManager}, playerRigidbody={playerRigidbody}）。スポーン制御は機能しません。");
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

        if (debugUseFixedSpawnPosition)
        {
            Vector3 fixedPosition = new Vector3(spawnSearchOrigin.x, spawnHeightOffset, spawnSearchOrigin.z);
            Debug.Log($"PlayerSpawner[DEBUG]: 固定位置 {fixedPosition} へテレポートします（道路探索はスキップ）。");
            playerRigidbody.position = fixedPosition;
            playerRigidbody.linearVelocity = Vector3.zero;
            return;
        }

        if (chunk.TryFindNearestMajorRoadCell(spawnSearchOrigin, out Vector3 result))
        {
            Vector3 targetPosition = result + Vector3.up * spawnHeightOffset;

            // デバッグ用ログ。上のdebugUseFixedSpawnPositionで座標計算自体は正しいと確認できた後、
            // 「テレポート先に本当に地面のColliderが存在するか（生成されていないのか、生成されているのに
            // プレイヤーがすり抜けているのか）」を切り分けるために追加した。テレポート先の真下へ
            // Raycastを飛ばし、ヒットしたColliderと距離をログに出す。ヒットしなければ地面が
            // 生成されていない可能性が高い。
            if (Physics.Raycast(targetPosition, Vector3.down, out RaycastHit hit, spawnHeightOffset + 10f))
            {
                Debug.Log($"PlayerSpawner[DEBUG]: 真下Raycastが {hit.collider.name} (Layer={LayerMask.LayerToName(hit.collider.gameObject.layer)}) に距離{hit.distance:F2}でヒット。着地予定Y={hit.point.y:F2}");
            }
            else
            {
                Debug.LogWarning($"PlayerSpawner[DEBUG]: {targetPosition} の真下{spawnHeightOffset + 10f}m以内にColliderが見つかりません。地面が生成されていない可能性があります。");
            }

            Debug.Log($"PlayerSpawner: 基幹道路セル {result} を発見。{targetPosition} へテレポートします。");
            playerRigidbody.position = targetPosition;
            // テレポートまでに落下で蓄積した速度を消し、この高さから落とし直す。
            playerRigidbody.linearVelocity = Vector3.zero;
        }
        else
        {
            Debug.LogWarning("PlayerSpawner: 基幹道路のマスが見つかりませんでした。プレイヤーは初期位置のままです。");
        }
    }
}
