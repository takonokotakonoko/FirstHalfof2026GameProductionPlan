using System;
using UnityEngine;

// 生成された建物1棟ごとに付与される識別情報。
// チャンクが破棄→再生成されても「どの建物が壊されたか」を引き継げるようにするための土台。
// 将来の攻撃・破壊ロジックは、対象のGameObjectからこのコンポーネントを取得してDestroyBuilding()を呼ぶだけでよい。
public class BuildingInstance : MonoBehaviour
{
    public static event Action<Vector2Int, int> Destroyed;

    public Vector2Int ChunkCoord { get; private set; }
    public int LotId { get; private set; }

    public void Initialize(Vector2Int chunkCoord, int lotId)
    {
        ChunkCoord = chunkCoord;
        LotId = lotId;
    }

    public void DestroyBuilding()
    {
        Destroyed?.Invoke(ChunkCoord, LotId);
        Destroy(gameObject);
    }
}
