using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;
using System.Collections.Generic;

public class GenerateClusterBuildings : MonoBehaviour
{
    public enum LandType
    {
        Empty,
        Lot,
        Road,
        Building,
    }

    private enum RoadClass
    {
        None,
        Major,
        Local,
    }

    private enum RoadGenerationMode
    {
        Grid,
        RecursiveSubdivision,
        // チャンク座標＋グローバルシードだけで決まる決定論的な格子で幹線道路を配置する。
        // どのチャンクから計算しても同じ結果になるため、チャンク境界で街区のリズムが途切れない。
        GlobalLattice,
    }

    [Header("Grid")]
    [Tooltip("チャンク生成時は「1チャンクあたりの横方向マス数」として扱われる。")]
    [SerializeField] [JpLabel("グリッド横幅")] private int gridWidth = 20;
    [Tooltip("チャンク生成時は「1チャンクあたりの縦方向マス数」として扱われる。")]
    [SerializeField] [JpLabel("グリッド縦幅")] private int gridHeight = 20;

    [Header("Cell Size")]
    [SerializeField] [JpLabel("セル幅")] private float cellWidth = 3f;
    [SerializeField] [JpLabel("セル高さ")] private float cellHeight = 3f;

    [Header("Road Generation")]
    [SerializeField] [JpLabel("道路生成モード")] private RoadGenerationMode roadGenerationMode = RoadGenerationMode.RecursiveSubdivision;
    [SerializeField] [JpLabel("幹線道路 最小間隔")] private int majorRoadMinInterval = 8;
    [SerializeField] [JpLabel("幹線道路 最大間隔")] private int majorRoadMaxInterval = 14;
    [SerializeField] [JpLabel("幹線道路幅")] private int majorRoadWidth = 3;
    [SerializeField] [JpLabel("外周道路を生成")] private bool generateOuterBoundaryRoads = true;
    [SerializeField] [JpLabel("乱数シード")] private int randomSeed = 0;
    [Tooltip("RecursiveSubdivisionモードでのみ使用。区画が分割可能でもこの確率で分割を打ち切り、大きめの街区として確定する。")]
    [SerializeField] [Range(0f, 1f)] [JpLabel("再帰分割 打ち切り確率")] private float recursiveSplitStopChance = 0.15f;

    [Header("Global Lattice Variance")]
    [Tooltip("GlobalLatticeモード専用。ワールド座標のノイズで格子の粗密を変化させる強さ。ノイズが低い場所ほどジッター幅を縮めて規則的にする。ジッター幅の上限（幹線道路の間隔を最小〜最大間隔に収める値）は超えない。0だとジッター幅が常に上限で一定になる。")]
    [SerializeField] [Range(0f, 1f)] [JpLabel("格子ジッター強度")] private float latticeVarianceStrength = 0.5f;
    [Tooltip("GlobalLatticeモード専用。ノイズが低いセルの道路を間引いて隣と合体させ、大きめの街区を作る最大確率。")]
    [SerializeField] [Range(0f, 0.9f)] [JpLabel("街区合体確率")] private float latticeBlockMergeChance = 0.25f;
    [Tooltip("GlobalLatticeモード専用。粗密ノイズのスケール。小さいほど粗密の切り替わりが緩やかになる。")]
    [SerializeField] [JpLabel("粗密ノイズ周波数")] private float latticeNoiseFrequency = 0.15f;
    [Tooltip("GlobalLatticeモード専用。街区合体のムラ（クラスター）を作る低周波ノイズのスケール。latticeNoiseFrequencyより小さい値にすると、広い範囲でまとまって大きい街区が生まれるエリアができる。")]
    [SerializeField] [JpLabel("クラスターノイズ周波数")] private float latticeClusterFrequency = 0.02f;
    [Tooltip("GlobalLatticeモード専用。クラスターノイズが合体確率に与える影響の強さ。0でクラスター化なし（latticeBlockMergeChanceが一様に効く従来通り）。1に近いほど「合体しまくるエリア」と「全く合体しないエリア」の差がはっきり出る。")]
    [SerializeField] [Range(0f, 1f)] [JpLabel("クラスター強度")] private float latticeClusterStrength = 0.6f;

    [Header("Building Generation")]
    [SerializeField] [JpLabel("建物 最小サイズ")] private int minBuildingSize = 1;
    [SerializeField] [JpLabel("建物 最大サイズ")] private int maxBuildingSize = 4;
    [SerializeField] [JpLabel("建物パディング幅")] private int buildingPaddingWidth = 1;
    [Tooltip("街区を敷地に再帰分割する際、まだ分割可能でもこの確率で打ち切り、大きめの敷地として確定する。")]
    [SerializeField] [Range(0f, 1f)] [JpLabel("敷地分割 打ち切り確率")] private float lotSplitStopChance = 0.35f;
    [Tooltip("パフォーマンス課題2対策：建物Instantiateを1フレームあたりこの数だけ処理してyieldする。小さいほど1フレームの負荷は下がるが、チャンク1個の生成完了までのフレーム数は伸びる。")]
    [SerializeField] [JpLabel("1フレームあたりの建物生成数")] private int buildingsInstantiatedPerFrame = 40;

    [Header("Sidewalk Generation")]
    [SerializeField] [JpLabel("定期ポータルを有効化")] private bool enablePeriodicPortals = false;
    [FormerlySerializedAs("guardrailPortalInterval")]
    [SerializeField] [JpLabel("歩道 ポータル間隔")] private int sidewalkPortalInterval = 8;
    [SerializeField] [JpLabel("ポータル 最小間隔")] private int portalMinSpacing = 3;
    [SerializeField] [JpLabel("手動ポータルシードを使用")] private bool useManualPortalSeeds = false;
    [SerializeField] [JpLabel("手動ポータルシード")] private Vector2Int[] manualPortalSeeds;

    [Header("Prefabs")]
    [Tooltip("1マス（cellWidth×cellHeight）の実寸で作ること。コード側では引き伸ばさず、プレファブのスケールのまま置く。")]
    [SerializeField] [JpLabel("敷地プレファブ")] private GameObject sitePrefab;
    [Tooltip("1マス（cellWidth×cellHeight）の実寸で作ること。コード側では引き伸ばさず、プレファブのスケールのまま置く。")]
    [SerializeField] [JpLabel("道路プレファブ")] private GameObject roadPrefab;
    [Tooltip("1マス（cellWidth×cellHeight）の実寸で作ること。コード側では引き伸ばさず、プレファブのスケールのまま置く。")]
    [SerializeField] [JpLabel("信号機プレファブ")] private GameObject trafficLightsPrefab;
    [FormerlySerializedAs("guardrailPrefab")]
    [SerializeField] [JpLabel("歩道プレファブ")] private GameObject sidewalkPrefab;
    [SerializeField] [JpLabel("ビルプレファブ")] private GameObject billPrefab;
    [Tooltip("1マス（cellWidth×cellHeight）の実寸で作ること。コード側では引き伸ばさず、プレファブのスケールのまま置く。")]
    [SerializeField] [JpLabel("横断歩道プレファブ")] private GameObject crosswalkPrefab;

    [Tooltip("未設定なら1マス版を並べる方式にフォールバックする。道路帯の全幅（majorRoadWidth マス）を1枚でまたぐ実寸（伸縮なし）の横断歩道。幅の分割が無いため、縞模様を全幅で自由に設計できる。")]
    [SerializeField] [JpLabel("横断歩道プレファブ（全幅版）")] private GameObject crosswalkBarPrefab;
    [Tooltip("未設定なら1マス版を並べる方式にフォールバックする。チャンク境界用の半幅版（BoundaryRoadWidth マス分）。各チャンクが半分ずつ出し、継ぎ目で合わさって全幅版と同じ幅になる。180度回転しても同じに見える対称な形で作ること。")]
    [SerializeField] [JpLabel("横断歩道プレファブ（チャンク境界・半幅版）")] private GameObject boundaryCrosswalkBarPrefab;

    [Header("Road Tile Variants (Major)")]
    [Tooltip("未設定ならroadPrefabを1マスずつ敷く方式にフォールバックする。GlobalLatticeモード限定。道路の進行方向1マス×横断方向2マス（3m×6m）の実寸（伸縮なし）タイル。内部の幹線道路には近側・遠側（180度回転）の2枚、チャンク境界帯には1枚を置く。ローカル−X端が歩道側、+X端が道路中央側、+Zが進行方向。")]
    [SerializeField] [JpLabel("幹線道路 半幅プレファブ（3m×6m）")] private GameObject majorRoadHalfPrefab;
    [Tooltip("未設定ならroadPrefabを1マスずつ敷く方式にフォールバックする。GlobalLatticeモード限定。交差点を2×2マス（6m×6m）ずつ覆う実寸（伸縮なし）タイル。十字路は4枚、チャンク境界上の十字路は各チャンク2枚、チャンクの四隅は各チャンク1枚で構成される。ローカル(−X,−Z)の角が交差点の外角（歩道の角）。")]
    [SerializeField] [JpLabel("幹線道路 交差点四半分プレファブ（6m×6m）")] private GameObject majorRoadIntersectionQuarterPrefab;

    [Header("Road Tile Variants (Local)")]
    [Tooltip("未設定ならroadPrefabにフォールバックする。歩道側（敷地に接する縁）のタイル。L型側溝用。1マス（cellWidth×cellHeight）の実寸で作ること。")]
    [SerializeField] [JpLabel("生活道路 縁プレファブ")] private GameObject localRoadEdgePrefab;

    [Header("Building Rarity")]
    [Tooltip("未設定の場合は常にbillPrefabを使用する。設定すると距離に応じて抽選されたプレファブを使用する。")]
    [SerializeField] [JpLabel("レア度設定")] private BuildingRaritySettings raritySettings;
    [Tooltip("レア度抽選の距離を測る基準点（通常はマップのスタート地点＝ワールド原点）。")]
    [SerializeField] [JpLabel("距離基準ワールド座標")] private Vector3 distanceOriginWorldPosition = Vector3.zero;

    [Header("Debug")]
    [Tooltip("チャンク生成完了時に、道路タイルの分類ごとのセル数をConsoleへ出力する。見た目だけでは判断しづらい分類の不具合を切り分けるための診断用。")]
    [SerializeField] [JpLabel("タイル分類のログを出力")] private bool logTileClassificationCounts = false;

    [Header("Chunk Streaming")]
    [Tooltip("falseにするとAwakeで自動生成しない。ChunkManagerがGenerateChunkを明示的に呼び出す運用で使用する。")]
    [SerializeField] [JpLabel("Awakeで自動生成")] private bool autoGenerateOnAwake = true;

    private static readonly Vector2Int[] FourDirections =
    {
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(0, 1),
    };

    // タスク10（道路タイルの向き）用の分類。見た目（プレファブ・回転）はまだ割り当てず、
    // 判定結果のみを持つ。NotRoad以外はGetRoadTileRotationと組み合わせて使う想定。
    private enum RoadTileKind
    {
        NotRoad,
        Interior,            // 対向車線側（センターライン候補）
        EdgeToSidewalk,      // 歩道・敷地側（L型側溝候補）
        EdgeToChunkBoundary, // チャンク境界側（隣チャンクの状態を参照できない）
        Intersection,        // 交差点内
    }

    private LandType[,] landMap;
    private int[,] buildingMap;
    private RoadClass[,] roadClassMap;
    private bool[,] sidewalkMap;
    private bool[,] trafficLightMap;
    private bool[,] crosswalkMap;
    private bool[,] portalMap;
    private int[,] localRoadDistanceFromMajor;

    // Major道路帯の向き情報（タスク10）。帯を塗った箇所（PaintRoadLinesByPositions等）でのみ書き込まれる。
    // roadOffsetFromNearEdge/FarEdgeは-1が「Major道路帯として記録されていない」を表す。
    private bool[,] roadStripIsVertical;
    private int[,] roadOffsetFromNearEdge;
    private int[,] roadOffsetFromFarEdge;
    private bool[,] isMajorIntersectionCell;

    private GameObject groundParent;
    private Vector2Int chunkCoord;
    private readonly HashSet<int> destroyedLotIds = new HashSet<int>();

    // GlobalLatticeモード専用。randomSeed（チャンクごとに違う値）とは別に、
    // 全チャンク共通の値を使うことで、どのチャンクから計算しても同じ格子になるようにする。
    private int latticeSeed;

    // このチャンク専用の乱数。生成はコルーチンで複数フレームに分かれ、ChunkManagerは複数チャンクを同時に
    // 生成するため、共有のUnityEngine.Randomを使うとyieldの間に別チャンクや敵システムの乱数消費が割り込み、
    // 同じシードでも結果が変わってしまう。インスタンスごとに持つことで、生成順・タイミングに依存しない
    // 決定論的な生成にする（破壊状態の復元が「再生成しても同じ建物に同じIDが振られる」ことに依存しているため）。
    // 構造体のUnity.Mathematics.Randomではなく参照型のSystem.Randomを使うのは、再帰関数で共有しても
    // 値コピーで同じ乱数列が繰り返される罠が無いため。詳細は基本設計.md参照。
    private System.Random rng;

    private void Reset()
    {
        AutoConfigureCellSizeFromPrefab();
    }

    private void OnValidate()
    {
        gridWidth = Mathf.Max(1, gridWidth);
        // チャンクを正方形に強制する。gridWidthを基準にgridHeightを常に揃える。
        gridHeight = gridWidth;
        majorRoadMinInterval = Mathf.Max(1, majorRoadMinInterval);
        majorRoadMaxInterval = Mathf.Max(majorRoadMinInterval, majorRoadMaxInterval);
        majorRoadWidth = Mathf.Max(1, majorRoadWidth);
        minBuildingSize = Mathf.Max(1, minBuildingSize);
        maxBuildingSize = Mathf.Max(minBuildingSize, maxBuildingSize);
        buildingPaddingWidth = Mathf.Max(0, buildingPaddingWidth);
        sidewalkPortalInterval = Mathf.Max(1, sidewalkPortalInterval);
        portalMinSpacing = Mathf.Max(1, portalMinSpacing);
        recursiveSplitStopChance = Mathf.Clamp01(recursiveSplitStopChance);
        lotSplitStopChance = Mathf.Clamp01(lotSplitStopChance);
        AutoConfigureCellSizeFromPrefab();
    }

    private void Awake()
    {
        if (autoGenerateOnAwake)
        {
            GenerateLand();
        }
    }

    // ChunkManagerからチャンク単位で呼び出すエントリポイント。
    // seedからチャンクごとに決定論的なレイアウトを生成する。
    public void GenerateChunk(int seed)
    {
        GenerateChunk(seed, Vector2Int.zero, null, seed, null);
    }

    // coordは破壊状態の紐付けおよびGlobalLatticeモードのワールド座標計算に使うチャンク座標。
    // alreadyDestroyedLotIdsに含まれる敷地IDの建物はInstantiateしない（破壊済みのまま復活させないため）。
    // sharedLatticeSeedはGlobalLatticeモード専用。全チャンクで同じ値を渡すことで格子の連続性を保証する
    // （randomSeedはチャンクごとに異なる値のため、これとは別に受け取る）。
    public void GenerateChunk(int seed, Vector2Int coord, IEnumerable<int> alreadyDestroyedLotIds, int sharedLatticeSeed)
    {
        GenerateChunk(seed, coord, alreadyDestroyedLotIds, sharedLatticeSeed, null);
    }

    // パフォーマンス課題2対策：生成処理をコルーチン化し、複数フレームに分割して実行する。
    // onCompleteは生成完了（GameObject階層が出来上がった時点）で呼ばれる。ChunkManagerはこれを
    // 使って「生成中はまだloadedChunksに入れない」制御を行う。
    public void GenerateChunk(int seed, Vector2Int coord, IEnumerable<int> alreadyDestroyedLotIds, int sharedLatticeSeed, System.Action onComplete)
    {
        randomSeed = seed == 0 ? 1 : seed;
        chunkCoord = coord;
        latticeSeed = sharedLatticeSeed;
        destroyedLotIds.Clear();
        if (alreadyDestroyedLotIds != null)
        {
            foreach (int lotId in alreadyDestroyedLotIds)
            {
                destroyedLotIds.Add(lotId);
            }
        }

        StartCoroutine(GenerateLandRoutine(onComplete));
    }

    // 単体テスト/非ストリーミング運用向け（ChunkManagerを介さずAwakeから直接呼ばれる場合など）。
    // 完了を待つ必要がある場合はGenerateChunk(...)のonComplete引数を使うこと。
    public void GenerateLand()
    {
        StartCoroutine(GenerateLandRoutine(null));
    }

    private IEnumerator GenerateLandRoutine(System.Action onComplete)
    {
        // randomSeedが0のときは従来通り「固定しない」（毎回ランダム）。
        rng = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random();

        landMap = new LandType[gridWidth, gridHeight];
        buildingMap = new int[gridHeight, gridWidth];
        roadClassMap = new RoadClass[gridWidth, gridHeight];
        sidewalkMap = new bool[gridWidth, gridHeight];
        trafficLightMap = new bool[gridWidth, gridHeight];
        crosswalkMap = new bool[gridWidth, gridHeight];
        portalMap = new bool[gridWidth, gridHeight];
        localRoadDistanceFromMajor = new int[gridWidth, gridHeight];
        roadStripIsVertical = new bool[gridWidth, gridHeight];
        roadOffsetFromNearEdge = new int[gridWidth, gridHeight];
        roadOffsetFromFarEdge = new int[gridWidth, gridHeight];
        isMajorIntersectionCell = new bool[gridWidth, gridHeight];

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                landMap[x, y] = LandType.Lot;
                buildingMap[y, x] = 0;
                roadClassMap[x, y] = RoadClass.None;
                sidewalkMap[x, y] = false;
                trafficLightMap[x, y] = false;
                crosswalkMap[x, y] = false;
                portalMap[x, y] = false;
                localRoadDistanceFromMajor[x, y] = -1;
                roadStripIsVertical[x, y] = false;
                roadOffsetFromNearEdge[x, y] = -1;
                roadOffsetFromFarEdge[x, y] = -1;
                isMajorIntersectionCell[x, y] = false;
            }
        }

        // パフォーマンス課題2（チャンク生成が重い）対策：各フェーズの間でyield returnし、
        // 1フレームに処理を集中させず複数フレームに分散する。Profilerサンプルは
        // どのフェーズが支配的かを引き続き個別に確認できるよう残してある。
        UnityEngine.Profiling.Profiler.BeginSample("GenerateMajorRoads");
        GenerateMajorRoads();
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("GenerateLots");
        GenerateLots();
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("ConvertRemainingLotGapsToLocalRoads");
        ConvertRemainingLotGapsToLocalRoads();
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("GenerateSidewalkLayout");
        GenerateSidewalkLayout();
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("GenerateCrosswalksAndTrafficLights");
        GenerateCrosswalksAndTrafficLights();
        UnityEngine.Profiling.Profiler.EndSample();
        WarnIfCrosswalkOverlapsMajorIntersection();
        yield return null;

        if (logTileClassificationCounts)
        {
            LogTileClassificationCounts();
        }

        yield return BuildGroundTilesRoutine();

        Debug.Log($"City grid generated: {gridWidth} x {gridHeight}. Lots and buildings placed.");
        onComplete?.Invoke();
    }

    // 1行分の断面を文字列でダンプする。画面上では帯の並びを正確に読み取りづらいため、
    // 「実際にどの分類が何マス並んでいるか」を文字で確認できるようにしてある。
    private string DumpScanline(int y)
    {
        System.Text.StringBuilder line = new System.Text.StringBuilder(gridWidth);

        for (int x = 0; x < gridWidth; x++)
        {
            if (crosswalkMap[x, y])
            {
                line.Append('c');
                continue;
            }

            if (roadClassMap[x, y] == RoadClass.Local)
            {
                line.Append('-');
                continue;
            }

            switch (ClassifyMajorRoadTile(x, y))
            {
                case RoadTileKind.Interior: line.Append('I'); break;
                case RoadTileKind.EdgeToSidewalk: line.Append('E'); break;
                case RoadTileKind.EdgeToChunkBoundary: line.Append('B'); break;
                case RoadTileKind.Intersection: line.Append('X'); break;
                default: line.Append(sidewalkMap[x, y] ? 'w' : '.'); break;
            }
        }

        return line.ToString();
    }

    // 分類ごとのセル数をConsoleへ出力する診断用。見た目では「そもそも生成されていない」のか
    // 「生成されているが見落としている」のかを区別できないため、数字で切り分けられるようにしてある。
    private void LogTileClassificationCounts()
    {
        int interior = 0;
        int edgeToSidewalk = 0;
        int edgeToChunkBoundary = 0;
        int intersection = 0;
        int localRoad = 0;
        int crosswalkOnBoundary = 0;
        int crosswalkElsewhere = 0;

        int boundaryWidth = BoundaryRoadWidth;

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (crosswalkMap[x, y])
                {
                    if (IsBoundaryBandCell(x, y))
                    {
                        crosswalkOnBoundary++;
                    }
                    else
                    {
                        crosswalkElsewhere++;
                    }
                }

                if (roadClassMap[x, y] == RoadClass.Local)
                {
                    localRoad++;
                    continue;
                }

                switch (ClassifyMajorRoadTile(x, y))
                {
                    case RoadTileKind.Interior: interior++; break;
                    case RoadTileKind.EdgeToSidewalk: edgeToSidewalk++; break;
                    case RoadTileKind.EdgeToChunkBoundary: edgeToChunkBoundary++; break;
                    case RoadTileKind.Intersection: intersection++; break;
                }
            }
        }

        // バーにまとまったか、1マス版へフォールバックしたかを数字で確認できるようにする。
        crosswalkBarSkippedByLength = 0;
        crosswalkBarSkippedByMesh = 0;
        crosswalkBarMeshFailureLogged = false;
        ComputeCrosswalkBars(out HashSet<Vector2Int> barCells, out List<MergedTile> bars);
        int fullBars = 0;
        int halfBars = 0;
        foreach (MergedTile bar in bars)
        {
            if (bar.Prefab == crosswalkBarPrefab)
            {
                fullBars++;
            }
            else
            {
                halfBars++;
            }
        }

        // 幹線道路が半幅／四半分タイルにまとまったか、1マス版（roadPrefab）へフォールバックしたかを数字で確認する。
        // 横断歩道セルは横断歩道側が描くため、フォールバックの数には含めない。
        ComputeMajorRoadTiles(out HashSet<Vector2Int> majorTileCells, out List<MergedTile> majorTiles);
        int halfTiles = 0;
        int quarterTiles = 0;
        foreach (MergedTile tile in majorTiles)
        {
            if (tile.Prefab == majorRoadIntersectionQuarterPrefab)
            {
                quarterTiles++;
            }
            else
            {
                halfTiles++;
            }
        }

        int majorFallbackCells = 0;
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (roadClassMap[x, y] == RoadClass.Major && !crosswalkMap[x, y] && !majorTileCells.Contains(new Vector2Int(x, y)))
                {
                    majorFallbackCells++;
                }
            }
        }

        Debug.Log(
            $"[タイル分類] Chunk({chunkCoord.x}, {chunkCoord.y}) grid={gridWidth}x{gridHeight} majorRoadWidth={majorRoadWidth} 境界帯幅={boundaryWidth}\n" +
            $"  横断歩道バー 全幅={fullBars}本 / 半幅={halfBars}本 / バー化されたセル={barCells.Count}\n" +
            $"  バー化されなかった原因 長さ不一致={crosswalkBarSkippedByLength} / メッシュ無し={crosswalkBarSkippedByMesh}\n" +
            $"  crosswalkBarPrefab={(crosswalkBarPrefab != null ? crosswalkBarPrefab.name : "null")} " +
            $"boundaryCrosswalkBarPrefab={(boundaryCrosswalkBarPrefab != null ? boundaryCrosswalkBarPrefab.name : "null")}\n" +
            $"  幹線タイル 半幅={halfTiles}枚 / 四半分={quarterTiles}枚 / フォールバック幹線セル={majorFallbackCells}\n" +
            $"  Interior(紫)={interior} / EdgeToSidewalk(緑)={edgeToSidewalk} / EdgeToChunkBoundary(青)={edgeToChunkBoundary} / Intersection(赤)={intersection}\n" +
            $"  生活道路={localRoad}\n" +
            $"  横断歩道 境界帯={crosswalkOnBoundary} / それ以外={crosswalkElsewhere}\n" +
            $"  断面ダンプ（I=内側 E=歩道側 B=チャンク境界 X=交差点 c=横断歩道 -=生活道路 w=歩道 .=敷地）\n" +
            $"  y=1   : {DumpScanline(1)}\n" +
            $"  y={gridHeight / 2,-4}: {DumpScanline(gridHeight / 2)}");
    }

    // UnityEngine.Random.Range(int, int)と同じ意味（maxExclusiveは含まない）。System.Random.Nextは
    // min > max で例外を投げるが、Unityは投げないため、max ≤ min のときはminを返して挙動を揃える。
    private int RandomRange(int minInclusive, int maxExclusive)
    {
        return maxExclusive <= minInclusive ? minInclusive : rng.Next(minInclusive, maxExclusive);
    }

    // UnityEngine.Random.valueの代わり。範囲は[0, 1)（Unityは[0, 1]）だが、「確率未満か」の比較にしか使わないため影響はない。
    private float RandomValue()
    {
        return (float)rng.NextDouble();
    }

    private void GenerateMajorRoads()
    {
        if (generateOuterBoundaryRoads)
        {
            PaintOuterBoundaryRoads();
        }

        switch (roadGenerationMode)
        {
            case RoadGenerationMode.RecursiveSubdivision:
                GenerateMajorRoadsRecursive();
                break;
            case RoadGenerationMode.GlobalLattice:
                GenerateMajorRoadsLattice();
                break;
            case RoadGenerationMode.Grid:
            default:
                GenerateMajorRoadsGrid();
                break;
        }
    }

    // チャンク同士は互いの外周が接する。両側がmajorRoadWidth分を満額塗ると
    // 境界だけ道路が2倍太くなってしまうため、片側はその半分だけを塗り、
    // 隣接チャンクの半分と合わさって内部の幹線道路（majorRoadWidth）と同じ太さになるようにする。
    // majorRoadWidthが奇数の場合、切り捨てにより継ぎ目の合計幅が1マス細くなる（例: 3→2）。
    private int BoundaryRoadWidth => Mathf.Max(1, majorRoadWidth / 2);

    private void PaintOuterBoundaryRoads()
    {
        int boundaryWidth = BoundaryRoadWidth;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                bool isLeftEdge = x < boundaryWidth;
                bool isRightEdge = x >= gridWidth - boundaryWidth;
                bool isBottomEdge = y < boundaryWidth;
                bool isTopEdge = y >= gridHeight - boundaryWidth;

                if (!isLeftEdge && !isRightEdge && !isBottomEdge && !isTopEdge)
                {
                    continue;
                }

                // 四隅は左右・上下どちらの帯とも言えるが、回転角の判定に軸情報が要るため
                // 左右境界を優先してisVertical=trueとして記録する（コーナーの見た目調整は将来の課題）。
                bool isVertical = isLeftEdge || isRightEdge;

                // 帯の中で敷地側（Near）・チャンクの継ぎ目側（Far）のどちらを向いているかをセルごとに記録する。
                // グリッドの最外周セルはClassifyMajorRoadTileが無条件にEdgeToChunkBoundaryとして確定させるため、
                // ここで意味を持つのは最外周より内側のセル（敷地に面する縁かどうか）の判定。
                int offsetFromNearEdge = isVertical
                    ? (isLeftEdge ? boundaryWidth - 1 - x : x - (gridWidth - boundaryWidth))
                    : (isBottomEdge ? boundaryWidth - 1 - y : y - (gridHeight - boundaryWidth));

                MarkMajorRoadStripCell(x, y, isVertical, offsetFromNearEdge, boundaryWidth);

                // 左右の帯と上下の帯が重なる四隅は、2本の境界道路が交差している場所そのもの。
                // ここを明示的に交差点として記録しておかないと、上の軸記録（左右優先）の都合で
                // 隣接セルとの軸判定がばらつき、角だけ分類がまだらになる。
                // 判定はセルの位置だけで決まるため、角に集まる4チャンクすべてが同じ結論になる。
                if ((isLeftEdge || isRightEdge) && (isBottomEdge || isTopEdge))
                {
                    isMajorIntersectionCell[x, y] = true;
                }
            }
        }
    }

    private void GenerateMajorRoadsGrid()
    {
        int boundaryWidth = BoundaryRoadWidth;
        List<int> majorRoadXPositions = GenerateRandomRoadPositions(boundaryWidth, gridWidth - boundaryWidth, majorRoadMinInterval, majorRoadMaxInterval);
        List<int> majorRoadYPositions = GenerateRandomRoadPositions(boundaryWidth, gridHeight - boundaryWidth, majorRoadMinInterval, majorRoadMaxInterval);

        PaintRoadLinesByPositions(true, majorRoadXPositions, majorRoadWidth);
        PaintRoadLinesByPositions(false, majorRoadYPositions, majorRoadWidth);
    }

    // ワールド座標のセルインデックスとlatticeSeedだけで幹線道路の位置を決める。
    // Random（チャンクごとに状態が違う）を一切使わないため、隣接チャンクが独立に計算しても
    // 同じ格子線を得られ、街区のリズムがチャンク境界で途切れない。
    // majorRoadMinInterval/MaxIntervalの平均を基準間隔、差の半分をジッター幅として流用する
    // （min==maxならジッター0＝完全に規則的な格子になる）。
    private void GenerateMajorRoadsLattice()
    {
        int boundaryWidth = generateOuterBoundaryRoads ? BoundaryRoadWidth : 0;
        PaintLatticeInteriorAxis(true, boundaryWidth);
        PaintLatticeInteriorAxis(false, boundaryWidth);
    }

    private int MacroCellSize => Mathf.Max(majorRoadWidth + 1, (majorRoadMinInterval + majorRoadMaxInterval) / 2);

    // 隣り合う格子線の間隔は「MacroCellSize + 次の線のジッター − 今の線のジッター」で決まり、
    // ジッターは線ごとに独立に決まる。そのため1本あたりのジッター幅を (MacroCellSize − 最小間隔) / 2 に
    // 抑えれば、隣の線を参照せずに間隔が必ず [最小間隔, 最大間隔] に収まる（帯同士の重なり・接触を防ぐ）。
    // 以前は (max − min) / 2 をそのまま1本ずつに与えていたため、間隔のばらつきが想定の2倍になり、
    // ノイズでさらに1.5倍されると間隔が負（帯の重なり）になり得た。詳細は基本設計.md参照。
    // majorRoadWidth + 1 との比較は、最小間隔を道路幅以下に設定された場合でも重ならないようにする保険。
    private int MacroCellJitterRange => Mathf.Max(0, (MacroCellSize - Mathf.Max(majorRoadMinInterval, majorRoadWidth + 1)) / 2);

    private void PaintLatticeInteriorAxis(bool isVertical, int boundaryWidth)
    {
        int axisLength = isVertical ? gridWidth : gridHeight;
        int worldOrigin = isVertical ? chunkCoord.x * gridWidth : chunkCoord.y * gridHeight;
        int macroCellSize = MacroCellSize;

        // 境界道路とのあいだに最低1街区分（majorRoadMinInterval）の余白を必ず空ける。
        // 余白ゼロだと格子線が境界道路にピッタリ隙間なく接することがあり、見た目上
        // 1本の異常に太い道路として繋がって見えてしまう（境界道路＋隣接格子線の合算）。
        int safetyGap = majorRoadMinInterval;

        int firstCellIndex = FloorDiv(worldOrigin, macroCellSize) - 1;
        int lastCellIndex = FloorDiv(worldOrigin + axisLength, macroCellSize) + 1;

        for (int cellIndex = firstCellIndex; cellIndex <= lastCellIndex; cellIndex++)
        {
            // ノイズが低いセルは道路を間引き、隣のセルと合体させて大きめの街区を作る。
            // ワールド座標のノイズだけで決まるので、どのチャンクから計算しても同じ判定になる。
            if (!ShouldPlaceLatticeLine(isVertical, cellIndex))
            {
                continue;
            }

            int worldLinePosition = GetLatticeLineWorldPosition(isVertical, cellIndex);
            int localPosition = worldLinePosition - worldOrigin;

            // 外周の境界道路と重複・近接しすぎる線は間引く。
            if (localPosition < boundaryWidth + safetyGap ||
                localPosition + majorRoadWidth > axisLength - boundaryWidth - safetyGap)
            {
                continue;
            }

            PaintLatticeLine(isVertical, localPosition);
        }
    }

    // ワールド座標に対して滑らかに変化するノイズ値（0〜1）。latticeSeedでパターンを変える。
    private float SampleLatticeNoise(bool isVertical, int cellIndex)
    {
        float seedOffsetA = (latticeSeed % 10000) * 0.0731f;
        float seedOffsetB = (latticeSeed % 7919) * 0.0417f;
        float axisOffset = isVertical ? 0f : 4096f;
        float coord = cellIndex * latticeNoiseFrequency + axisOffset;
        return Mathf.PerlinNoise(coord + seedOffsetA, seedOffsetB);
    }

    private bool ShouldPlaceLatticeLine(bool isVertical, int cellIndex)
    {
        if (latticeBlockMergeChance <= 0f)
        {
            return true;
        }

        float noise = SampleLatticeNoise(isVertical, cellIndex);
        return noise >= latticeBlockMergeChance;
    }

    private void PaintLatticeLine(bool isVertical, int localPosition)
    {
        for (int offset = 0; offset < majorRoadWidth; offset++)
        {
            int local = localPosition + offset;

            if (isVertical)
            {
                if (local < 0 || local >= gridWidth)
                {
                    continue;
                }

                for (int y = 0; y < gridHeight; y++)
                {
                    MarkMajorRoadStripCell(local, y, true, offset, majorRoadWidth);
                }
            }
            else
            {
                if (local < 0 || local >= gridHeight)
                {
                    continue;
                }

                for (int x = 0; x < gridWidth; x++)
                {
                    MarkMajorRoadStripCell(x, local, false, offset, majorRoadWidth);
                }
            }
        }
    }

    private int GetLatticeLineWorldPosition(bool isVertical, int cellIndex)
    {
        int basePosition = cellIndex * MacroCellSize;
        int baseJitterRange = MacroCellJitterRange;

        if (baseJitterRange <= 0)
        {
            return basePosition;
        }

        // ノイズが低いセルほどジッター幅を狭めて規則正しくする。baseJitterRangeは重なりを防ぐ上限なので
        // 超えてはならず、ノイズは「上限以下でどれだけ縮めるか」にだけ使う。
        // latticeVarianceStrength=0なら常にbaseJitterRangeのまま。
        float noise = SampleLatticeNoise(isVertical, cellIndex);
        float scale = Mathf.Lerp(1f - latticeVarianceStrength, 1f, noise);
        int jitterRange = Mathf.Clamp(Mathf.RoundToInt(baseJitterRange * scale), 0, baseJitterRange);

        if (jitterRange <= 0)
        {
            return basePosition;
        }

        int hash = HashLatticeCell(isVertical, cellIndex);
        int jitter = Mathf.Abs(hash % (jitterRange * 2 + 1)) - jitterRange;
        return basePosition + jitter;
    }

    private int HashLatticeCell(bool isVertical, int cellIndex)
    {
        unchecked
        {
            int h = latticeSeed;
            h = h * 486187739 + cellIndex * 73856093;
            h ^= isVertical ? 0x27d4eb2f : 0x165667b1;
            h ^= h >> 15;
            h *= unchecked((int)2246822519);
            h ^= h >> 13;
            return h;
        }
    }

    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if (a % b != 0 && ((a < 0) != (b < 0)))
        {
            q--;
        }

        return q;
    }

    // 敷地全体を1つの区画とみなし、道路1本で2分割→できた区画をそれぞれ独立した乱数でさらに分割…を繰り返す。
    // 区画ごとに分割位置・分割回数が異なるため、街区の大きさ・形が場所ごとにバラバラになる。
    private void GenerateMajorRoadsRecursive()
    {
        int boundaryWidth = generateOuterBoundaryRoads ? BoundaryRoadWidth : 0;
        int x1 = boundaryWidth;
        int y1 = boundaryWidth;
        int x2 = gridWidth - boundaryWidth;
        int y2 = gridHeight - boundaryWidth;

        SplitBlockRecursive(x1, y1, x2, y2);
    }

    private void SplitBlockRecursive(int x1, int y1, int x2, int y2)
    {
        int width = x2 - x1;
        int height = y2 - y1;

        bool canSplitX = width >= majorRoadMinInterval * 2 + majorRoadWidth;
        bool canSplitY = height >= majorRoadMinInterval * 2 + majorRoadWidth;

        if (!canSplitX && !canSplitY)
        {
            // これ以上分割できない区画は、1つの街区として確定する。
            return;
        }

        bool mustSplitX = canSplitX && width > majorRoadMaxInterval;
        bool mustSplitY = canSplitY && height > majorRoadMaxInterval;

        bool splitVertical;
        if (mustSplitX != mustSplitY)
        {
            // 片方の辺だけがmaxIntervalを超えている場合は、その辺を優先的に割る。
            splitVertical = mustSplitX;
        }
        else if (!mustSplitX && !mustSplitY && RandomValue() < recursiveSplitStopChance)
        {
            // まだ分割可能でも、一定確率で打ち切って大きめの街区として確定する。
            return;
        }
        else if (canSplitX && canSplitY)
        {
            // 長辺側を優先的に割る。ほぼ正方形の場合は五分五分でランダムに決める。
            splitVertical = width != height ? width > height : RandomValue() < 0.5f;
        }
        else
        {
            splitVertical = canSplitX;
        }

        if (splitVertical)
        {
            int splitX = RandomRange(x1 + majorRoadMinInterval, x2 - majorRoadMinInterval - majorRoadWidth + 1);
            PaintRoadStrip(true, splitX, majorRoadWidth, y1, y2);
            SplitBlockRecursive(x1, y1, splitX, y2);
            SplitBlockRecursive(splitX + majorRoadWidth, y1, x2, y2);
        }
        else
        {
            int splitY = RandomRange(y1 + majorRoadMinInterval, y2 - majorRoadMinInterval - majorRoadWidth + 1);
            PaintRoadStrip(false, splitY, majorRoadWidth, x1, x2);
            SplitBlockRecursive(x1, y1, x2, splitY);
            SplitBlockRecursive(x1, splitY + majorRoadWidth, x2, y2);
        }
    }

    private void PaintRoadStrip(bool isVertical, int position, int width, int rangeStart, int rangeEnd)
    {
        if (isVertical)
        {
            for (int y = rangeStart; y < rangeEnd; y++)
            {
                for (int offset = 0; offset < width; offset++)
                {
                    MarkMajorRoadStripCell(position + offset, y, true, offset, width);
                }
            }
        }
        else
        {
            for (int x = rangeStart; x < rangeEnd; x++)
            {
                for (int offset = 0; offset < width; offset++)
                {
                    MarkMajorRoadStripCell(x, position + offset, false, offset, width);
                }
            }
        }
    }

    private List<int> GenerateRandomRoadPositions(int start, int end, int minInterval, int maxInterval)
    {
        List<int> positions = new List<int>();
        positions.Add(start);

        int currentPos = start + RandomRange(minInterval, maxInterval + 1);
        while (currentPos < end)
        {
            positions.Add(currentPos);
            currentPos += RandomRange(minInterval, maxInterval + 1);
        }

        return positions;
    }

    private void PaintRoadLinesByPositions(bool isVertical, List<int> positions, int width)
    {
        if (isVertical)
        {
            foreach (int x in positions)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    for (int offset = 0; offset < width; offset++)
                    {
                        int targetX = x + offset;
                        if (targetX < gridWidth)
                        {
                            MarkMajorRoadStripCell(targetX, y, true, offset, width);
                        }
                    }
                }
            }
        }
        else
        {
            foreach (int y in positions)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    for (int offset = 0; offset < width; offset++)
                    {
                        int targetY = y + offset;
                        if (targetY < gridHeight)
                        {
                            MarkMajorRoadStripCell(x, targetY, false, offset, width);
                        }
                    }
                }
            }
        }
    }

    private void MarkRoadCell(int x, int y, RoadClass roadClass)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return;
        }

        landMap[x, y] = LandType.Road;

        if (roadClass == RoadClass.Major)
        {
            roadClassMap[x, y] = RoadClass.Major;
        }
        else if (roadClass == RoadClass.Local && roadClassMap[x, y] == RoadClass.None)
        {
            roadClassMap[x, y] = RoadClass.Local;
        }
    }

    // Major道路の帯を塗る箇所（PaintRoadStrip/PaintLatticeLine/PaintRoadLinesByPositions）専用。
    // 帯の軸方向と、帯の中で近い端・遠い端から何列目かを記録する（タスク10：道路タイルの向き判定用）。
    // 既に別軸の帯として記録済みのセルに塗る場合は、南北・東西の帯が重なる交差点とみなす。
    private void MarkMajorRoadStripCell(int x, int y, bool isVertical, int offsetFromNearEdge, int stripWidth)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return;
        }

        bool wasAlreadyMajor = roadClassMap[x, y] == RoadClass.Major;
        bool crossesOtherAxis = wasAlreadyMajor && roadOffsetFromNearEdge[x, y] >= 0 && roadStripIsVertical[x, y] != isVertical;

        MarkRoadCell(x, y, RoadClass.Major);

        if (crossesOtherAxis)
        {
            isMajorIntersectionCell[x, y] = true;
            return;
        }

        roadStripIsVertical[x, y] = isVertical;
        roadOffsetFromNearEdge[x, y] = offsetFromNearEdge;
        roadOffsetFromFarEdge[x, y] = stripWidth - 1 - offsetFromNearEdge;
    }

    // Major道路セル1つの分類を判定する（タスク10：見た目の割り当てはまだ行わない）。
    private RoadTileKind ClassifyMajorRoadTile(int x, int y)
    {
        if (roadClassMap[x, y] != RoadClass.Major)
        {
            return RoadTileKind.NotRoad;
        }

        // 交差点判定を最外周判定より先に行う。交差点かどうかは「南北・東西の両方の帯として塗られたか」
        // で決まり、対岸＝隣接チャンク側の情報を必要としない。しかも塗る位置はワールド座標から決まる
        // 決定論的な格子線なので、隣チャンクが独立に計算しても必ず同じ結論になる。
        // 以前は最外周を無条件にEdgeToChunkBoundaryにしていたため、境界帯と内部道路が交わる箇所が
        // 「赤・青・青・赤」に分断されて見えていた。
        if (isMajorIntersectionCell[x, y] || HasAdjacentDifferentAxisMajorRoad(x, y))
        {
            return RoadTileKind.Intersection;
        }

        // 交差点でない最外周セルは、対岸の状態を参照できず帯のオフセット判定ができないため、
        // 一律チャンク境界として扱う。
        if (x == 0 || x == gridWidth - 1 || y == 0 || y == gridHeight - 1)
        {
            return RoadTileKind.EdgeToChunkBoundary;
        }

        bool isEdge = roadOffsetFromNearEdge[x, y] == 0 || roadOffsetFromFarEdge[x, y] == 0;
        return isEdge ? RoadTileKind.EdgeToSidewalk : RoadTileKind.Interior;
    }

    // RecursiveSubdivisionモード（区画分割方式）では、T字路の接続部分は「同じセルが2軸から
    // 重複して塗られる」形にならず、帯同士が隣接するだけになる（例：垂直帯の側面に水平帯の端が接する）。
    // isMajorIntersectionCellだけではこのケースを検出できないため、4方向いずれかに自分と異なる
    // 軸のMajor道路帯が隣接していないかを見て補完する。
    private bool HasAdjacentDifferentAxisMajorRoad(int x, int y)
    {
        bool isVertical = roadStripIsVertical[x, y];

        if (x > 0 && roadClassMap[x - 1, y] == RoadClass.Major && roadStripIsVertical[x - 1, y] != isVertical) return true;
        if (x < gridWidth - 1 && roadClassMap[x + 1, y] == RoadClass.Major && roadStripIsVertical[x + 1, y] != isVertical) return true;
        if (y > 0 && roadClassMap[x, y - 1] == RoadClass.Major && roadStripIsVertical[x, y - 1] != isVertical) return true;
        if (y < gridHeight - 1 && roadClassMap[x, y + 1] == RoadClass.Major && roadStripIsVertical[x, y + 1] != isVertical) return true;

        return false;
    }

    // Local道路（生活道路）セル1つの分類を判定する。センターラインは持たせない方針のため、
    // 帯のオフセット情報は使わず、隣接セルがLotかどうかだけを見る動的判定にしている。
    private RoadTileKind ClassifyLocalRoadTile(int x, int y)
    {
        if (roadClassMap[x, y] != RoadClass.Local)
        {
            return RoadTileKind.NotRoad;
        }

        bool hasLotLeft = x > 0 && landMap[x - 1, y] == LandType.Lot;
        bool hasLotRight = x < gridWidth - 1 && landMap[x + 1, y] == LandType.Lot;
        bool hasLotUp = y > 0 && landMap[x, y - 1] == LandType.Lot;
        bool hasLotDown = y < gridHeight - 1 && landMap[x, y + 1] == LandType.Lot;

        return (hasLotLeft || hasLotRight || hasLotUp || hasLotDown) ? RoadTileKind.EdgeToSidewalk : RoadTileKind.Interior;
    }

    // Major道路タイルの回転角を求める。プレファブ側の前提：ローカルZ軸方向に沿って
    // センターライン／L型側溝の模様が伸びているものとする（isVertical=trueの帯なら無回転で正面が合う）。
    // Intersection（交差点）は十字路のみを想定し、対称なプレファブを前提に常に無回転とする
    // （実際にはT字/L字になる場所もあるが、見た目の妥協として許容する。詳細は基本設計.md参照）。
    private Quaternion GetMajorRoadTileRotation(int x, int y, RoadTileKind kind)
    {
        switch (kind)
        {
            case RoadTileKind.Interior:
            case RoadTileKind.EdgeToChunkBoundary:
                return Quaternion.Euler(0f, roadStripIsVertical[x, y] ? 0f : 90f, 0f);

            case RoadTileKind.EdgeToSidewalk:
                {
                    float baseYaw = roadStripIsVertical[x, y] ? 0f : 90f;
                    // 帯の反対側の縁（Far側）は180度反転させ、L型側溝が歩道側を向くようにする。
                    if (roadOffsetFromFarEdge[x, y] == 0)
                    {
                        baseYaw += 180f;
                    }

                    return Quaternion.Euler(0f, baseYaw, 0f);
                }

            case RoadTileKind.Intersection:
            case RoadTileKind.NotRoad:
            default:
                return Quaternion.identity;
        }
    }

    // Local道路（生活道路）タイルの回転角を求める。既存のGetSidewalkRotationと同じ考え方で、
    // 隣接するLot方向を見てL型側溝の向きを合わせる。複数方向にLotが隣接する角セルは
    // 左→右→上→下の優先順位で最初に該当した方向を採用する（詳細な複合パターンは将来の課題）。
    private Quaternion GetLocalRoadTileRotation(int x, int y, RoadTileKind kind)
    {
        if (kind != RoadTileKind.EdgeToSidewalk)
        {
            return Quaternion.identity;
        }

        bool hasLotLeft = x > 0 && landMap[x - 1, y] == LandType.Lot;
        bool hasLotRight = x < gridWidth - 1 && landMap[x + 1, y] == LandType.Lot;
        bool hasLotUp = y > 0 && landMap[x, y - 1] == LandType.Lot;

        if (hasLotLeft) return Quaternion.Euler(0f, 270f, 0f);
        if (hasLotRight) return Quaternion.Euler(0f, 90f, 0f);
        if (hasLotUp) return Quaternion.Euler(0f, 180f, 0f);
        return Quaternion.identity;
    }

    // 横断歩道タイルの回転角を求める。縞模様は道路の進行方向に平行に伸びる前提のため、
    // GetMajorRoadTileRotationのInterior/EdgeToChunkBoundaryと同じ0°/90°の考え方をそのまま使う
    // （Near/Far反転は非対称な模様向けの調整のため、対称な横断歩道では不要）。
    // Major道路上の横断歩道はMarkMajorRoadStripCellでroadStripIsVerticalが記録済み。
    // 生活道路上の横断歩道はConvertSidewalkSandwichedLocalRoadsToCrosswalksで同じ意味合いのroadStripIsVerticalを記録している。
    private Quaternion GetCrosswalkTileRotation(int x, int y)
    {
        return Quaternion.Euler(0f, roadStripIsVertical[x, y] ? 0f : 90f, 0f);
    }

    private void GenerateLots()
    {
        List<RectInt> blocks = FindBlockRects();
        int lotId = 1;

        foreach (RectInt block in blocks)
        {
            bool touchesXMin = block.xMin > 0 && roadClassMap[block.xMin - 1, block.yMin] == RoadClass.Major;
            bool touchesXMax = block.xMax < gridWidth && roadClassMap[block.xMax, block.yMin] == RoadClass.Major;
            bool touchesYMin = block.yMin > 0 && roadClassMap[block.xMin, block.yMin - 1] == RoadClass.Major;
            bool touchesYMax = block.yMax < gridHeight && roadClassMap[block.xMin, block.yMax] == RoadClass.Major;

            SplitLotRecursive(block.xMin, block.yMin, block.xMax, block.yMax, touchesXMin, touchesXMax, touchesYMin, touchesYMax, ref lotId);
        }
    }

    // 道路で区切られた連結領域（街区）を矩形として検出する。
    // 幹線道路がグリッド方式・再帰分割方式のどちらで作られていても、街区は必ず矩形になる前提で動作する。
    private List<RectInt> FindBlockRects()
    {
        List<RectInt> blocks = new List<RectInt>();
        bool[,] visited = new bool[gridWidth, gridHeight];
        int[] dirX = { -1, 1, 0, 0 };
        int[] dirY = { 0, 0, -1, 1 };

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (visited[x, y] || landMap[x, y] == LandType.Road)
                {
                    continue;
                }

                int minX = x, maxX = x, minY = y, maxY = y;
                Queue<Vector2Int> queue = new Queue<Vector2Int>();
                visited[x, y] = true;
                queue.Enqueue(new Vector2Int(x, y));

                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    minX = Mathf.Min(minX, cell.x);
                    maxX = Mathf.Max(maxX, cell.x);
                    minY = Mathf.Min(minY, cell.y);
                    maxY = Mathf.Max(maxY, cell.y);

                    for (int i = 0; i < 4; i++)
                    {
                        int nx = cell.x + dirX[i];
                        int ny = cell.y + dirY[i];

                        if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight || visited[nx, ny])
                        {
                            continue;
                        }

                        if (landMap[nx, ny] == LandType.Road)
                        {
                            continue;
                        }

                        visited[nx, ny] = true;
                        queue.Enqueue(new Vector2Int(nx, ny));
                    }
                }

                blocks.Add(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }
        }

        return blocks;
    }

    // 街区の矩形を1本のパディングで2分割→できた区画をそれぞれ独立した乱数でさらに分割…を繰り返し、
    // 敷地（建物1棟分の土地）を再帰的に切り出す。幹線道路の再帰分割と同じ考え方。
    // touchesX/Y系フラグは、この矩形が東西南北どの大道路に接しているかを表す。
    // 接している辺の数だけ歩道に1マスずつ奪われるため、その分だけ最小サイズを底上げして、
    // 歩道の奥に必ずminBuildingSize分の建物用セルが残るようにする。
    private void SplitLotRecursive(int x1, int y1, int x2, int y2, bool touchesXMin, bool touchesXMax, bool touchesYMin, bool touchesYMax, ref int lotId)
    {
        int width = x2 - x1;
        int height = y2 - y1;

        int minSizeXMinSide = (touchesXMin ? 1 : 0) + minBuildingSize;
        int minSizeXMaxSide = (touchesXMax ? 1 : 0) + minBuildingSize;
        int minSizeYMinSide = (touchesYMin ? 1 : 0) + minBuildingSize;
        int minSizeYMaxSide = (touchesYMax ? 1 : 0) + minBuildingSize;

        bool canSplitX = width >= minSizeXMinSide + minSizeXMaxSide + buildingPaddingWidth;
        bool canSplitY = height >= minSizeYMinSide + minSizeYMaxSide + buildingPaddingWidth;

        if (!canSplitX && !canSplitY)
        {
            PaintMapRectLot(lotId, x1, y1, x2, y2);
            lotId++;
            return;
        }

        bool mustSplitX = canSplitX && width > maxBuildingSize;
        bool mustSplitY = canSplitY && height > maxBuildingSize;

        bool splitVertical;
        if (mustSplitX != mustSplitY)
        {
            splitVertical = mustSplitX;
        }
        else if (!mustSplitX && !mustSplitY && RandomValue() < lotSplitStopChance)
        {
            // まだ分割可能でも、一定確率で打ち切って1つの敷地として確定する。
            PaintMapRectLot(lotId, x1, y1, x2, y2);
            lotId++;
            return;
        }
        else if (canSplitX && canSplitY)
        {
            splitVertical = width != height ? width > height : RandomValue() < 0.5f;
        }
        else
        {
            splitVertical = canSplitX;
        }

        if (splitVertical)
        {
            int splitX = RandomRange(x1 + minSizeXMinSide, x2 - minSizeXMaxSide - buildingPaddingWidth + 1);
            SplitLotRecursive(x1, y1, splitX, y2, touchesXMin, false, touchesYMin, touchesYMax, ref lotId);
            SplitLotRecursive(splitX + buildingPaddingWidth, y1, x2, y2, false, touchesXMax, touchesYMin, touchesYMax, ref lotId);
        }
        else
        {
            int splitY = RandomRange(y1 + minSizeYMinSide, y2 - minSizeYMaxSide - buildingPaddingWidth + 1);
            SplitLotRecursive(x1, y1, x2, splitY, touchesXMin, touchesXMax, touchesYMin, false, ref lotId);
            SplitLotRecursive(x1, splitY + buildingPaddingWidth, x2, y2, touchesXMin, touchesXMax, false, touchesYMax, ref lotId);
        }
    }

    private void PaintMapRectLot(int id, int x1, int y1, int x2, int y2)
    {
        for (int y = y1; y < y2; y++)
        {
            for (int x = x1; x < x2; x++)
            {
                if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
                {
                    if (landMap[x, y] != LandType.Road)
                    {
                        if (id > 0)
                        {
                            landMap[x, y] = LandType.Lot;
                            buildingMap[y, x] = id;
                        }
                        else
                        {
                            buildingMap[y, x] = 0;
                        }
                    }
                }
            }
        }
    }

    // 敷地分割で残ったパディング隙間（どの敷地にも属さないLotセル）を生活道路に変換する。
    // 再帰分割の性質上、この隙間は街区の内側から大道路の縁まで貫通しているため、
    // そのまま生活道路網として機能する。
    private void ConvertRemainingLotGapsToLocalRoads()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (landMap[x, y] == LandType.Lot && buildingMap[y, x] == 0)
                {
                    MarkRoadCell(x, y, RoadClass.Local);
                }
            }
        }
    }

    // 街区の角（大道路2方向に接するセル）を信号機に変更し、道路を挟んだ対岸も信号機のときだけ
    // その間の大道路セルを横断歩道に変える。さらに歩道に挟まれた生活道路も横断歩道にする。
    private void GenerateCrosswalksAndTrafficLights()
    {
        MarkBlockCornersAsTrafficLights();
        GenerateCrosswalksAtTrafficLights();
        ConvertSidewalkSandwichedLocalRoadsToCrosswalks();
    }

    // 外周道路帯（チャンクの継ぎ目側）のセルかどうか。横断歩道のバー化と診断ログで共通して使う。
    private bool IsBoundaryBandCell(int x, int y)
    {
        int boundaryWidth = BoundaryRoadWidth;
        return x < boundaryWidth || x >= gridWidth - boundaryWidth
            || y < boundaryWidth || y >= gridHeight - boundaryWidth;
    }

    private void MarkBlockCornersAsTrafficLights()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (buildingMap[y, x] <= 0)
                {
                    continue;
                }

                if (IsLotCornerAdjacentToTwoMajorRoads(x, y))
                {
                    trafficLightMap[x, y] = true;
                    sidewalkMap[x, y] = false;
                }
            }
        }
    }

    private bool IsLotCornerAdjacentToTwoMajorRoads(int x, int y)
    {
        bool hasMajorX = (x > 0 && roadClassMap[x - 1, y] == RoadClass.Major) || (x < gridWidth - 1 && roadClassMap[x + 1, y] == RoadClass.Major);
        bool hasMajorY = (y > 0 && roadClassMap[x, y - 1] == RoadClass.Major) || (y < gridHeight - 1 && roadClassMap[x, y + 1] == RoadClass.Major);
        return hasMajorX && hasMajorY;
    }

    private void GenerateCrosswalksAtTrafficLights()
    {
        int crossDistance = majorRoadWidth + 1;

        // 信号機セルをすべて先に列挙してから処理する。この関数はtrafficLightMapを読むだけで書き換えない
        // （信号機の配置はMarkBlockCornersAsTrafficLightsで確定済み）。
        List<Vector2Int> trafficLightCells = new List<Vector2Int>();
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (trafficLightMap[x, y])
                {
                    trafficLightCells.Add(new Vector2Int(x, y));
                }
            }
        }

        foreach (Vector2Int cell in trafficLightCells)
        {
            foreach (Vector2Int dir in FourDirections)
            {
                int roadX = cell.x + dir.x;
                int roadY = cell.y + dir.y;

                if (roadX < 0 || roadX >= gridWidth || roadY < 0 || roadY >= gridHeight)
                {
                    continue;
                }

                if (roadClassMap[roadX, roadY] != RoadClass.Major)
                {
                    continue;
                }

                int targetX = cell.x + dir.x * crossDistance;
                int targetY = cell.y + dir.y * crossDistance;

                bool targetOutsideGrid = targetX < 0 || targetX >= gridWidth || targetY < 0 || targetY >= gridHeight;

                if (targetOutsideGrid)
                {
                    // 対岸がグリッド外＝チャンクの継ぎ目の外周道路帯を渡る腕。対岸の信号機は隣チャンク側に
                    // あり参照できないが、街区の角の位置は格子線と外周道路帯（どちらもワールド座標で決まる）
                    // だけで決まるため、隣チャンクも同じ列・行に対岸の角を持つ。よって対岸の信号機は
                    // 存在するものとみなし、自チャンクが担当する半幅（BoundaryRoadWidth分）だけを生成する。
                    // 隣チャンクも同じ判定で反対側の半幅を生成するので、継ぎ目で全幅が揃う。
                    // GlobalLatticeモードで格子線が全チャンク共通になることが前提。
                    if (!generateOuterBoundaryRoads || !IsBoundaryBandCell(roadX, roadY))
                    {
                        continue;
                    }
                }
                else if (!trafficLightMap[targetX, targetY])
                {
                    // 対岸が既に信号機（＝本当の交差点の角）のときだけ横断歩道を生成する。
                    // 対岸が歩道のまま（＝街区の直線区間の途中）の場合は何もしない。
                    continue;
                }

                for (int step = 1; step <= majorRoadWidth; step++)
                {
                    int px = cell.x + dir.x * step;
                    int py = cell.y + dir.y * step;

                    // 継ぎ目側はグリッド端で打ち切る（残りの半分は隣チャンクが生成する）。
                    if (px < 0 || px >= gridWidth || py < 0 || py >= gridHeight)
                    {
                        break;
                    }

                    if (roadClassMap[px, py] == RoadClass.Major)
                    {
                        crosswalkMap[px, py] = true;
                    }
                }
            }
        }
    }

    private void ConvertSidewalkSandwichedLocalRoadsToCrosswalks()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (roadClassMap[x, y] != RoadClass.Local)
                {
                    continue;
                }

                bool sandwichedHorizontally = x > 0 && x < gridWidth - 1 && sidewalkMap[x - 1, y] && sidewalkMap[x + 1, y];
                bool sandwichedVertically = y > 0 && y < gridHeight - 1 && sidewalkMap[x, y - 1] && sidewalkMap[x, y + 1];

                if (sandwichedHorizontally || sandwichedVertically)
                {
                    crosswalkMap[x, y] = true;
                    // Major道路のMarkMajorRoadStripCellと同じ意味合いで軸を記録する（横断歩道の回転判定用）。
                    // 左右を歩道に挟まれている＝道路が南北（Vertical帯）に通っている。
                    roadStripIsVertical[x, y] = sandwichedHorizontally;
                }
            }
        }
    }

    private void GenerateSidewalkLayout()
    {
        ComputeLocalRoadDistanceFromMajor();

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                sidewalkMap[x, y] = false;
                trafficLightMap[x, y] = false;
                portalMap[x, y] = false;
            }
        }

        ReserveTopologyPortals();
        if (enablePeriodicPortals)
        {
            ReserveAutomaticPortals();
        }
        ReserveManualPortals();

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (roadClassMap[x, y] != RoadClass.Local)
                {
                    continue;
                }

                if (!IsAdjacentToMajorRoad(x, y))
                {
                    continue;
                }

                if (portalMap[x, y])
                {
                    continue;
                }

                bool isLocalIntersection = IsLocalIntersectionCell(x, y);
                if (isLocalIntersection)
                {
                    continue;
                }

                sidewalkMap[x, y] = true;
            }
        }

        // Mandatory portal cells are always kept open, even if previous rules marked them as sidewalk.
        EnforceMandatoryPortalOverride();

        EnsureEachInteriorLocalComponentHasOpenPortal();
        MarkBuildingCellsAdjacentToMajorRoadsAsSidewalks();
    }

    private void MarkBuildingCellsAdjacentToMajorRoadsAsSidewalks()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (buildingMap[y, x] <= 0)
                {
                    continue;
                }

                if (IsAdjacentToMajorRoadCell(x, y))
                {
                    sidewalkMap[x, y] = true;
                }
            }
        }
    }

    private bool IsAdjacentToMajorRoadCell(int x, int y)
    {
        if (x > 0 && roadClassMap[x - 1, y] == RoadClass.Major) return true;
        if (x < gridWidth - 1 && roadClassMap[x + 1, y] == RoadClass.Major) return true;
        if (y > 0 && roadClassMap[x, y - 1] == RoadClass.Major) return true;
        if (y < gridHeight - 1 && roadClassMap[x, y + 1] == RoadClass.Major) return true;
        return false;
    }

    private void ComputeLocalRoadDistanceFromMajor()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                localRoadDistanceFromMajor[x, y] = -1;
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (roadClassMap[x, y] == RoadClass.Local && IsAdjacentToMajorRoad(x, y))
                {
                    localRoadDistanceFromMajor[x, y] = 0;
                    queue.Enqueue(new Vector2Int(x, y));
                }
            }
        }

        int[] dirX = { -1, 1, 0, 0 };
        int[] dirY = { 0, 0, -1, 1 };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int nextDistance = localRoadDistanceFromMajor[current.x, current.y] + 1;

            for (int i = 0; i < 4; i++)
            {
                int nx = current.x + dirX[i];
                int ny = current.y + dirY[i];

                if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                {
                    continue;
                }

                if (roadClassMap[nx, ny] != RoadClass.Local)
                {
                    continue;
                }

                if (localRoadDistanceFromMajor[nx, ny] != -1)
                {
                    continue;
                }

                localRoadDistanceFromMajor[nx, ny] = nextDistance;
                queue.Enqueue(new Vector2Int(nx, ny));
            }
        }
    }

    private void ReserveTopologyPortals()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (IsMandatoryPortalCell(x, y))
                {
                    portalMap[x, y] = true;
                }
            }
        }
    }

    private void ReserveAutomaticPortals()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (roadClassMap[x, y] != RoadClass.Local)
                {
                    continue;
                }

                if (!IsAdjacentToMajorRoad(x, y))
                {
                    continue;
                }

                if (portalMap[x, y])
                {
                    continue;
                }

                int axisValue = GetPortalAxisValue(x, y);
                if (axisValue % sidewalkPortalInterval == 0)
                {
                    portalMap[x, y] = true;
                }
            }
        }
    }

    private bool IsMandatoryPortalCell(int x, int y)
    {
        if (roadClassMap[x, y] != RoadClass.Local)
        {
            return false;
        }

        if (!IsAdjacentToMajorRoad(x, y))
        {
            return false;
        }

        // Distance 0 edge cell that has at least one Local neighbor deeper than 0 is an entrance.
        if (localRoadDistanceFromMajor[x, y] != 0)
        {
            return false;
        }

        return HasNeighborWithGreaterLocalDepth(x, y);
    }

    private bool HasNeighborWithGreaterLocalDepth(int x, int y)
    {
        int[] dirX = { -1, 1, 0, 0 };
        int[] dirY = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; i++)
        {
            int nx = x + dirX[i];
            int ny = y + dirY[i];

            if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
            {
                continue;
            }

            if (roadClassMap[nx, ny] != RoadClass.Local)
            {
                continue;
            }

            if (localRoadDistanceFromMajor[nx, ny] > localRoadDistanceFromMajor[x, y])
            {
                return true;
            }
        }

        return false;
    }

    private void EnforceMandatoryPortalOverride()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (!IsMandatoryPortalCell(x, y))
                {
                    continue;
                }

                portalMap[x, y] = true;
                sidewalkMap[x, y] = false;
            }
        }
    }

    private void EnsureEachInteriorLocalComponentHasOpenPortal()
    {
        bool[,] visited = new bool[gridWidth, gridHeight];
        int[] dirX = { -1, 1, 0, 0 };
        int[] dirY = { 0, 0, -1, 1 };

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (visited[x, y] || roadClassMap[x, y] != RoadClass.Local)
                {
                    continue;
                }

                Queue<Vector2Int> queue = new Queue<Vector2Int>();
                List<Vector2Int> componentCells = new List<Vector2Int>();
                List<Vector2Int> edgeCells = new List<Vector2Int>();
                bool hasInteriorCell = false;

                visited[x, y] = true;
                queue.Enqueue(new Vector2Int(x, y));

                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    componentCells.Add(cell);

                    bool adjacentToMajor = IsAdjacentToMajorRoad(cell.x, cell.y);
                    if (adjacentToMajor)
                    {
                        edgeCells.Add(cell);
                    }
                    else
                    {
                        hasInteriorCell = true;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        int nx = cell.x + dirX[i];
                        int ny = cell.y + dirY[i];

                        if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                        {
                            continue;
                        }

                        if (visited[nx, ny])
                        {
                            continue;
                        }

                        if (roadClassMap[nx, ny] != RoadClass.Local)
                        {
                            continue;
                        }

                        visited[nx, ny] = true;
                        queue.Enqueue(new Vector2Int(nx, ny));
                    }
                }

                if (!hasInteriorCell || edgeCells.Count == 0)
                {
                    continue;
                }

                bool hasOpenPortal = false;
                for (int i = 0; i < edgeCells.Count; i++)
                {
                    Vector2Int edge = edgeCells[i];
                    if (!sidewalkMap[edge.x, edge.y])
                    {
                        hasOpenPortal = true;
                        break;
                    }
                }

                if (hasOpenPortal)
                {
                    continue;
                }

                Vector2Int selected = edgeCells[0];
                int bestScore = int.MinValue;

                for (int i = 0; i < edgeCells.Count; i++)
                {
                    Vector2Int edge = edgeCells[i];
                    int score = GetLocalNeighborCount(edge.x, edge.y);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        selected = edge;
                    }
                }

                sidewalkMap[selected.x, selected.y] = false;
                portalMap[selected.x, selected.y] = true;
            }
        }
    }

    private int GetLocalNeighborCount(int x, int y)
    {
        int count = 0;
        if (x > 0 && roadClassMap[x - 1, y] == RoadClass.Local) count++;
        if (x < gridWidth - 1 && roadClassMap[x + 1, y] == RoadClass.Local) count++;
        if (y > 0 && roadClassMap[x, y - 1] == RoadClass.Local) count++;
        if (y < gridHeight - 1 && roadClassMap[x, y + 1] == RoadClass.Local) count++;
        return count;
    }

    private void ReserveManualPortals()
    {
        if (!useManualPortalSeeds || manualPortalSeeds == null)
        {
            return;
        }

        for (int i = 0; i < manualPortalSeeds.Length; i++)
        {
            int x = manualPortalSeeds[i].x;
            int y = manualPortalSeeds[i].y;

            if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
            {
                continue;
            }

            if (roadClassMap[x, y] == RoadClass.Local && IsAdjacentToMajorRoad(x, y))
            {
                portalMap[x, y] = true;
            }
        }
    }

    private int GetPortalAxisValue(int x, int y)
    {
        bool hasMajorLeft = x > 0 && roadClassMap[x - 1, y] == RoadClass.Major;
        bool hasMajorRight = x < gridWidth - 1 && roadClassMap[x + 1, y] == RoadClass.Major;
        bool hasMajorUp = y > 0 && roadClassMap[x, y - 1] == RoadClass.Major;
        bool hasMajorDown = y < gridHeight - 1 && roadClassMap[x, y + 1] == RoadClass.Major;

        if (hasMajorLeft || hasMajorRight)
        {
            return y;
        }

        if (hasMajorUp || hasMajorDown)
        {
            return x;
        }

        return x + y;
    }

    private bool IsAdjacentToMajorRoad(int x, int y)
    {
        if (x > 0 && roadClassMap[x - 1, y] == RoadClass.Major) return true;
        if (x < gridWidth - 1 && roadClassMap[x + 1, y] == RoadClass.Major) return true;
        if (y > 0 && roadClassMap[x, y - 1] == RoadClass.Major) return true;
        if (y < gridHeight - 1 && roadClassMap[x, y + 1] == RoadClass.Major) return true;
        return false;
    }

    private bool IsLocalIntersectionCell(int x, int y)
    {
        bool hasLocalLeft = x > 0 && roadClassMap[x - 1, y] == RoadClass.Local;
        bool hasLocalRight = x < gridWidth - 1 && roadClassMap[x + 1, y] == RoadClass.Local;
        bool hasLocalUp = y > 0 && roadClassMap[x, y - 1] == RoadClass.Local;
        bool hasLocalDown = y < gridHeight - 1 && roadClassMap[x, y + 1] == RoadClass.Local;

        int localConnectionCount = 0;
        if (hasLocalLeft) localConnectionCount++;
        if (hasLocalRight) localConnectionCount++;
        if (hasLocalUp) localConnectionCount++;
        if (hasLocalDown) localConnectionCount++;

        return localConnectionCount >= 3;
    }

    private void AutoConfigureCellSizeFromPrefab()
    {
        if (sitePrefab == null)
        {
            return;
        }

        Vector3 prefabSize = GetPrefabSize(sitePrefab);

        if (prefabSize.x > 0.0001f)
        {
            cellWidth = prefabSize.x;
        }

        if (prefabSize.z > 0.0001f)
        {
            cellHeight = prefabSize.z;
        }
    }

    private Vector3 GetPrefabSize(GameObject prefab)
    {
        if (prefab == null)
        {
            return Vector3.one;
        }

        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.size;
        }

        Collider[] colliders = prefab.GetComponentsInChildren<Collider>();
        if (colliders != null && colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds.size;
        }

        MeshFilter meshFilter = prefab.GetComponentInChildren<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            return Vector3.Scale(meshFilter.sharedMesh.bounds.size, prefab.transform.localScale);
        }

        return Vector3.one;
    }

    private IEnumerator BuildGroundTilesRoutine()
    {
        if (sitePrefab == null)
        {
            Debug.LogWarning("sitePrefab is not assigned. Please assign Assets/Prefab/ground/site.prefab.");
            yield break;
        }

        if (roadPrefab == null)
        {
            Debug.LogWarning("roadPrefab is not assigned. Please assign Assets/Prefab/ground/road.prefab.");
            yield break;
        }

        if (groundParent != null)
        {
            DestroyImmediate(groundParent);
        }

        groundParent = new GameObject("GroundTiles");
        groundParent.transform.SetParent(transform, false);

        // 地面タイル（敷地・道路・横断歩道・歩道・信号機）はすべて、チャンク単位・種類ごとにメッシュ結合する
        // （パフォーマンス課題1対策）。個別に触る（壊す）対象は建物だけなので、地面は1マスずつInstantiateしない。
        // 歩道は1チャンクで約4,000マスあり、1個ずつInstantiateすると生成時に1フレームへ集中してカクつきの原因になっていた
        // （基本設計.md「タスク8 追加対応（2026-09-26）」参照）。
        List<CombineInstance> siteCombine = new List<CombineInstance>();
        List<CombineInstance> sidewalkCombine = new List<CombineInstance>();
        List<CombineInstance> trafficLightCombine = new List<CombineInstance>();
        // タスク10：道路タイルはセルごとに使うプレファブ（センターライン/L型側溝等）が変わり得るため、
        // プレファブ種類ごとにCombineInstanceリストを持つ辞書で管理する。
        Dictionary<GameObject, List<CombineInstance>> roadCombineByPrefab = new Dictionary<GameObject, List<CombineInstance>>();
        List<CombineInstance> crosswalkCombine = new List<CombineInstance>();

        // GlobalLatticeモード限定：幹線道路を実寸（伸縮なし）の半幅タイル・交差点四半分タイルで組む。
        // まとめられたセルは1マス版の対象から外す（consumedCellsに入らなかった幹線セルはroadPrefabで1マスずつ敷く）。
        ComputeMajorRoadTiles(out HashSet<Vector2Int> majorRoadTileCells, out List<MergedTile> majorRoadTiles);
        foreach (MergedTile roadTile in majorRoadTiles)
        {
            AddCombineInstance(GetOrCreateCombineList(roadCombineByPrefab, roadTile.Prefab), roadTile.Prefab, roadTile.LocalPosition, roadTile.Rotation);
        }

        // 横断歩道を帯の全幅でまたぐ1枚のバーにまとめる。まとめられたセルは1マス版の対象から外す。
        ComputeCrosswalkBars(out HashSet<Vector2Int> crosswalkBarCells, out List<MergedTile> crosswalkBars);
        Dictionary<GameObject, List<CombineInstance>> crosswalkBarCombineByPrefab = new Dictionary<GameObject, List<CombineInstance>>();
        foreach (MergedTile bar in crosswalkBars)
        {
            AddCombineInstance(GetOrCreateCombineList(crosswalkBarCombineByPrefab, bar.Prefab), bar.Prefab, bar.LocalPosition, bar.Rotation);
        }

        UnityEngine.Profiling.Profiler.BeginSample("BuildGroundTiles.CollectCombineInstances");
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                Vector2Int cellCoord = new Vector2Int(x, y);
                if (majorRoadTileCells.Contains(cellCoord))
                {
                    // 半幅／四半分タイルとしてまとめて配置済みなので、この1マス分は何も置かない。
                    continue;
                }

                Vector3 localPosition = new Vector3((x + 0.5f) * cellWidth, 0f, (y + 0.5f) * cellHeight);

                // 歩道セルは幹線道路セルにならないため、上の判定より後ろに置いても結果は変わらない。
                // 信号機セルはMarkBlockCornersAsTrafficLightsでsidewalkMapがfalseにされるため、ここには来ない。
                if (sidewalkMap[x, y])
                {
                    if (sidewalkPrefab != null)
                    {
                        AddCombineInstance(sidewalkCombine, sidewalkPrefab, localPosition, GetSidewalkRotation(x, y));
                    }

                    continue;
                }

                // 1マス版のタイルも実寸（伸縮なし）で置く。以前はセルの大きさ（cellWidth×1×cellHeight）を
                // スケールとして掛けていたため、実寸3mで作ったモデルが9mに引き伸ばされていた。
                if (trafficLightMap[x, y] && trafficLightsPrefab != null)
                {
                    AddCombineInstance(trafficLightCombine, trafficLightsPrefab, localPosition, Quaternion.identity);
                    continue;
                }

                if (crosswalkMap[x, y] && crosswalkBarCells.Contains(cellCoord))
                {
                    // 全幅版バーとしてまとめて配置済みなので、この1マス分は何も置かない。
                    continue;
                }

                if (crosswalkMap[x, y] && crosswalkPrefab != null)
                {
                    Quaternion crosswalkRotation = GetCrosswalkTileRotation(x, y);
                    AddCombineInstance(crosswalkCombine, crosswalkPrefab, localPosition, crosswalkRotation);
                }
                else if (landMap[x, y] == LandType.Road)
                {
                    GameObject roadVariantPrefab = GetRoadTileVariant(x, y, out Quaternion roadRotation);
                    AddCombineInstance(GetOrCreateCombineList(roadCombineByPrefab, roadVariantPrefab), roadVariantPrefab, localPosition, roadRotation);
                }
                else
                {
                    AddCombineInstance(siteCombine, sitePrefab, localPosition, Quaternion.identity);
                }
            }
        }
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        // Sites/Roads/Crosswalksでそれぞれ数万頂点規模になり得るため、CombineMeshes自体を
        // 1回ずつに分けてyieldする（プロファイル計測でCombineMeshes全体が最大の単一コストだったため）。
        UnityEngine.Profiling.Profiler.BeginSample("BuildGroundTiles.CombineMeshes.Sites");
        CreateCombinedTileMesh("Sites", sitePrefab, siteCombine);
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("BuildGroundTiles.CombineMeshes.Roads");
        foreach (KeyValuePair<GameObject, List<CombineInstance>> roadVariant in roadCombineByPrefab)
        {
            CreateCombinedTileMesh($"Roads_{roadVariant.Key.name}", roadVariant.Key, roadVariant.Value);
        }
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("BuildGroundTiles.CombineMeshes.Crosswalks");
        CreateCombinedTileMesh("Crosswalks", crosswalkPrefab, crosswalkCombine);
        foreach (KeyValuePair<GameObject, List<CombineInstance>> barVariant in crosswalkBarCombineByPrefab)
        {
            CreateCombinedTileMesh($"Crosswalks_Bar_{barVariant.Key.name}", barVariant.Key, barVariant.Value);
        }
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        // 当たり判定の焼き込み（MeshColliderへの代入）を敷地・道路と同じフレームに重ねないよう、独立した1ステップにする。
        UnityEngine.Profiling.Profiler.BeginSample("BuildGroundTiles.CombineMeshes.Sidewalks");
        if (sidewalkPrefab == null)
        {
            Debug.LogWarning("sidewalkPrefab is not assigned. Sidewalk placement is skipped.");
        }
        else
        {
            CreateCombinedTileMesh("Sidewalks", sidewalkPrefab, sidewalkCombine);
        }
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("BuildGroundTiles.CombineMeshes.TrafficLights");
        CreateCombinedTileMesh("TrafficLights", trafficLightsPrefab, trafficLightCombine);
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        yield return PlaceBuildingsRoutine();
    }

    // 実寸（伸縮なし）で1枚配置するタイル。横断歩道バーと、幹線道路の半幅／交差点四半分タイルで共用する。
    private struct MergedTile
    {
        public GameObject Prefab;
        public Vector3 LocalPosition;
        public Quaternion Rotation;
    }

    // GlobalLatticeモード限定：幹線道路を実寸タイルで組む。
    // - 交差点（isMajorIntersectionCell＝2軸の帯が重なったセル）は2×2マスの四半分タイルで覆う。
    //   十字路は4枚、チャンク境界上の十字路は4×2に割れるので各チャンク2枚、チャンクの四隅は各チャンク1枚になり、
    //   どの場合も隣チャンクを参照せずに自チャンク内で完結する。
    // - 直線は帯を横切る方向に2マス（3m×6m）の半幅タイルで覆う。内部の幅4の帯は近側・遠側の2枚、
    //   チャンク境界帯（各チャンク幅2）は1枚。
    // 横断歩道セルは横断歩道側（バーまたは1マス版）が描くため対象外。条件を満たさないセルは
    // consumedCellsに入らず、従来通りroadPrefabで1マスずつ敷かれる。
    // RecursiveSubdivisionモードはT字路が「帯の重なり」にならず交差点を検出できないため対象外にしている。
    private void ComputeMajorRoadTiles(out HashSet<Vector2Int> consumedCells, out List<MergedTile> tiles)
    {
        consumedCells = new HashSet<Vector2Int>();
        tiles = new List<MergedTile>();

        if (roadGenerationMode != RoadGenerationMode.GlobalLattice)
        {
            return;
        }

        // 描画できないプレファブでセルを消費すると、1マス版も描かれず穴が空く。横断歩道バーと同じく先に弾く。
        if (HasUsableMesh(majorRoadIntersectionQuarterPrefab))
        {
            CollectIntersectionQuarterTiles(majorRoadIntersectionQuarterPrefab, consumedCells, tiles);
        }

        if (HasUsableMesh(majorRoadHalfPrefab))
        {
            CollectHalfWidthTiles(majorRoadHalfPrefab, consumedCells, tiles);
        }
    }

    private void CollectIntersectionQuarterTiles(GameObject prefab, HashSet<Vector2Int> consumedCells, List<MergedTile> tiles)
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (!IsQuarterTileCandidate(x, y, consumedCells) || !IsIntersectionRegionAlignedStart(x, y))
                {
                    continue;
                }

                if (!IsQuarterTileCandidate(x + 1, y, consumedCells) ||
                    !IsQuarterTileCandidate(x, y + 1, consumedCells) ||
                    !IsQuarterTileCandidate(x + 1, y + 1, consumedCells))
                {
                    continue;
                }

                if (!TryGetQuarterSidewalkCorner(x, y, out Vector2Int corner))
                {
                    continue;
                }

                tiles.Add(new MergedTile
                {
                    Prefab = prefab,
                    LocalPosition = new Vector3((x + 1f) * cellWidth, 0f, (y + 1f) * cellHeight),
                    Rotation = Quaternion.Euler(0f, GetQuarterTileYaw(corner), 0f),
                });

                consumedCells.Add(new Vector2Int(x, y));
                consumedCells.Add(new Vector2Int(x + 1, y));
                consumedCells.Add(new Vector2Int(x, y + 1));
                consumedCells.Add(new Vector2Int(x + 1, y + 1));
            }
        }
    }

    private bool IsQuarterTileCandidate(int x, int y, HashSet<Vector2Int> consumedCells)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return false;
        }

        return isMajorIntersectionCell[x, y] && !crosswalkMap[x, y] && !consumedCells.Contains(new Vector2Int(x, y));
    }

    // 交差点領域（4×4／4×2／2×4／2×2）を2×2に区切る際、区切りの位置が領域の端から偶数マス目になるようにする。
    // ある2×2ブロックが四半分の条件を満たさず飛ばされた場合でも、1マスずれた位置で誤ったブロックを作らないための保険。
    private bool IsIntersectionRegionAlignedStart(int x, int y)
    {
        int runLeft = 0;
        while (x - runLeft - 1 >= 0 && isMajorIntersectionCell[x - runLeft - 1, y])
        {
            runLeft++;
        }

        int runDown = 0;
        while (y - runDown - 1 >= 0 && isMajorIntersectionCell[x, y - runDown - 1])
        {
            runDown++;
        }

        return runLeft % 2 == 0 && runDown % 2 == 0;
    }

    // 2×2ブロックの4つの対角外側セルのうち、幹線道路でないもの（＝街区の角の敷地）がちょうど1つあれば、
    // その方向がこの四半分の「歩道の角」。十字路の四半分なら残り3方向は腕の道路や隣の四半分になる。
    // グリッド外は継ぎ目の向こうの道路とみなす（境界上の十字路・チャンクの四隅でも同じ規則で向きが決まる）。
    private bool TryGetQuarterSidewalkCorner(int x, int y, out Vector2Int corner)
    {
        corner = Vector2Int.zero;
        int found = 0;

        for (int dx = -1; dx <= 1; dx += 2)
        {
            for (int dy = -1; dy <= 1; dy += 2)
            {
                int cx = dx < 0 ? x - 1 : x + 2;
                int cy = dy < 0 ? y - 1 : y + 2;

                if (cx < 0 || cx >= gridWidth || cy < 0 || cy >= gridHeight)
                {
                    continue;
                }

                if (roadClassMap[cx, cy] != RoadClass.Major)
                {
                    found++;
                    corner = new Vector2Int(dx, dy);
                }
            }
        }

        return found == 1;
    }

    private void CollectHalfWidthTiles(GameObject prefab, HashSet<Vector2Int> consumedCells, List<MergedTile> tiles)
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (!IsHalfTileCandidate(x, y, consumedCells))
                {
                    continue;
                }

                int offsetNear = roadOffsetFromNearEdge[x, y];
                int offsetFar = roadOffsetFromFarEdge[x, y];
                int stripWidth = offsetNear + offsetFar + 1;

                // 半幅タイルの「歩道側セル」だけを起点にする。
                // 幅2（境界帯）はNear側＝敷地側だけ（Far側はチャンクの継ぎ目＝道路中央側）。
                // 幅4（内部）は両端。それ以外の幅は半幅タイルで割り切れないので1マス版に任せる。
                bool isSidewalkSideCell = stripWidth == 2
                    ? offsetNear == 0
                    : stripWidth == 4 && (offsetNear == 0 || offsetFar == 0);

                if (!isSidewalkSideCell)
                {
                    continue;
                }

                int partnerOffsetNear = offsetNear == 0 ? 1 : offsetNear - 1;
                if (!TryFindHalfTilePartner(x, y, partnerOffsetNear, stripWidth, consumedCells, out Vector2Int partner))
                {
                    continue;
                }

                Vector2Int sidewalkDirection = new Vector2Int(x - partner.x, y - partner.y);

                tiles.Add(new MergedTile
                {
                    Prefab = prefab,
                    LocalPosition = new Vector3(((x + partner.x) * 0.5f + 0.5f) * cellWidth, 0f, ((y + partner.y) * 0.5f + 0.5f) * cellHeight),
                    Rotation = Quaternion.Euler(0f, GetHalfTileYaw(sidewalkDirection), 0f),
                });

                consumedCells.Add(new Vector2Int(x, y));
                consumedCells.Add(partner);
            }
        }
    }

    private bool IsHalfTileCandidate(int x, int y, HashSet<Vector2Int> consumedCells)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return false;
        }

        return roadClassMap[x, y] == RoadClass.Major
            && roadOffsetFromNearEdge[x, y] >= 0
            && !isMajorIntersectionCell[x, y]
            && !crosswalkMap[x, y]
            && !sidewalkMap[x, y]
            && !consumedCells.Contains(new Vector2Int(x, y));
    }

    // 帯を横切る方向の隣2セルから、同じ軸・同じ帯幅で目的のオフセットを持つセルを探す。
    // 近い端が低い座標側か高い座標側かは帯によって異なる（内部の格子線は低い側、左・下の境界帯は高い側）ため、両方向を調べる。
    private bool TryFindHalfTilePartner(int x, int y, int partnerOffsetNear, int stripWidth, HashSet<Vector2Int> consumedCells, out Vector2Int partner)
    {
        bool isVertical = roadStripIsVertical[x, y];
        int stepX = isVertical ? 1 : 0;
        int stepY = isVertical ? 0 : 1;

        for (int sign = -1; sign <= 1; sign += 2)
        {
            int px = x + stepX * sign;
            int py = y + stepY * sign;

            if (!IsHalfTileCandidate(px, py, consumedCells) || roadStripIsVertical[px, py] != isVertical)
            {
                continue;
            }

            if (roadOffsetFromNearEdge[px, py] != partnerOffsetNear ||
                roadOffsetFromNearEdge[px, py] + roadOffsetFromFarEdge[px, py] + 1 != stripWidth)
            {
                continue;
            }

            partner = new Vector2Int(px, py);
            return true;
        }

        partner = Vector2Int.zero;
        return false;
    }

    // 回転はオフセットの反転規則ではなく、ワールド上の方向ベクトルから求める。
    // 既存の「offsetFar==0なら180度」の規則は、横向きの帯では歩道側が幾何的に逆を向く
    // （対称な横断歩道では見た目に出ないが、L型側溝を持つ非対称タイルでは破綻する）。
    // UnityのY軸回転θでは、ローカル(a, b)（XZ）がワールド(a·cosθ + b·sinθ, −a·sinθ + b·cosθ)に写る。
    // 半幅タイルはローカル−X端が歩道側：−x→0°、+z→90°、+x→180°、−z→270°。
    private static float GetHalfTileYaw(Vector2Int sidewalkDirection)
    {
        if (sidewalkDirection.x < 0) return 0f;
        if (sidewalkDirection.y > 0) return 90f;
        if (sidewalkDirection.x > 0) return 180f;
        return 270f;
    }

    // 四半分タイルはローカル(−X, −Z)の角が歩道の角：南西→0°、北西→90°、北東→180°、南東→270°。
    private static float GetQuarterTileYaw(Vector2Int corner)
    {
        if (corner.x < 0) return corner.y < 0 ? 0f : 90f;
        return corner.y > 0 ? 180f : 270f;
    }

    // 横断歩道は交差点（4×4）の外周の1行・1列にだけ生成される前提で、四半分タイルはこれに依存している
    // （横断歩道セルは四半分の対象から外すため、交差点の中に横断歩道があると2×2が組めず1マス版に落ちる）。
    // 前提が崩れた場合にすぐ気付けるよう、重なりがあれば1チャンクにつき1回だけ警告する。
    private void WarnIfCrosswalkOverlapsMajorIntersection()
    {
        int count = 0;
        Vector2Int first = Vector2Int.zero;

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (crosswalkMap[x, y] && isMajorIntersectionCell[x, y])
                {
                    if (count == 0)
                    {
                        first = new Vector2Int(x, y);
                    }

                    count++;
                }
            }
        }

        if (count > 0)
        {
            Debug.LogWarning($"[幹線タイル] Chunk({chunkCoord.x}, {chunkCoord.y}) 横断歩道が交差点セルに{count}マス重なっています（最初の位置 {first}）。横断歩道は交差点の外周にだけ生成される前提が崩れています。");
        }
    }

    // バーとして1本にまとめてよい横断歩道セルかどうか。
    // 種類の違う横断歩道を一続きとして数えてしまうと、連続長が期待値とずれてバーが作られなくなる。
    // 特に生活道路の横断歩道（歩道に挟まれた1マス）は1チャンクに1,400個以上あり、
    // 境界帯のすぐ隣に現れると境界帯のバーを壊すため、必ず除外する。
    private bool IsSameBandCrosswalk(int x, int y, bool isBoundary)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return false;
        }

        if (!crosswalkMap[x, y] || roadClassMap[x, y] != RoadClass.Major)
        {
            return false;
        }

        return IsBoundaryBandCell(x, y) == isBoundary;
    }

    // 横断歩道は道路帯を横切る1本の「バー」として生成されるため、1マスずつ敷くのではなく
    // 帯の全幅を1枚のプレファブでまたげる。ComputeMajorRoadTiles（幹線道路の半幅／四半分タイル）と同じ発想。
    // バーの長さが期待値と一致し、かつプレファブが設定されている場合だけ合体し、
    // それ以外は従来通り1マスずつ敷く（consumedCellsに入らなかったセルが1マス版として処理される）。
    // 診断用：バー化されなかった原因を「長さが期待値と違った」か「プレファブにメッシュが無かった」かで
    // 分離して数える。ログ出力時（LogTileClassificationCounts）にリセットしてから使う。
    private int crosswalkBarSkippedByLength;
    private int crosswalkBarSkippedByMesh;
    private bool crosswalkBarMeshFailureLogged;

    private void ComputeCrosswalkBars(out HashSet<Vector2Int> consumedCells, out List<MergedTile> bars)
    {
        consumedCells = new HashSet<Vector2Int>();
        bars = new List<MergedTile>();

        if (crosswalkBarPrefab == null && boundaryCrosswalkBarPrefab == null)
        {
            return;
        }

        int boundaryWidth = BoundaryRoadWidth;

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                // 生活道路の横断歩道（歩道に挟まれた1マス）はバーの対象外。幹線道路の帯を横切るものだけを扱う。
                if (!crosswalkMap[x, y] || roadClassMap[x, y] != RoadClass.Major)
                {
                    continue;
                }

                // バーは道路帯を横切る向きに伸びる。帯が南北なら東西方向、東西なら南北方向。
                bool isVertical = roadStripIsVertical[x, y];
                int stepX = isVertical ? 1 : 0;
                int stepY = isVertical ? 0 : 1;
                bool isBoundary = IsBoundaryBandCell(x, y);

                // 連続した並びの先頭セルからだけ処理する。
                if (IsSameBandCrosswalk(x - stepX, y - stepY, isBoundary))
                {
                    continue;
                }

                int runLength = 0;
                while (IsSameBandCrosswalk(x + stepX * runLength, y + stepY * runLength, isBoundary))
                {
                    runLength++;
                }

                bool lengthMatches = isBoundary ? runLength == boundaryWidth : runLength == majorRoadWidth;
                GameObject prefab = isBoundary
                    ? (lengthMatches ? boundaryCrosswalkBarPrefab : null)
                    : (lengthMatches ? crosswalkBarPrefab : null);

                if (logTileClassificationCounts)
                {
                    if (!lengthMatches)
                    {
                        crosswalkBarSkippedByLength++;
                    }
                    else if (!HasUsableMesh(prefab))
                    {
                        crosswalkBarSkippedByMesh++;

                        // 最初の1回だけ、失敗の実際の内訳をUnity自身のAPIから直接ログに出す。
                        // GUID/YAML比較では見えない「実行時に何が取得できているか」を確定させるため。
                        if (!crosswalkBarMeshFailureLogged)
                        {
                            crosswalkBarMeshFailureLogged = true;
                            MeshFilter mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
                            Debug.LogWarning(
                                $"[横断歩道バー診断] prefab={(prefab != null ? prefab.name : "null")} " +
                                $"prefab.activeSelf={(prefab != null ? prefab.activeSelf.ToString() : "-")} " +
                                $"MeshFilter見つかった={(mf != null)} " +
                                $"sharedMesh={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "null")} " +
                                $"sharedMesh頂点数={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount.ToString() : "-")}");
                        }
                    }
                }

                // 描画できないプレファブでセルを消費すると、1マス版も描かれず穴が空く。
                // AddCombineInstanceはメッシュが無い場合に黙って何もしないため、ここで先に弾く。
                if (!HasUsableMesh(prefab))
                {
                    continue;
                }

                // 帯を横切る方向の中心へ置く。実寸プレファブなのでスケールは掛けない。
                Vector3 localPosition = new Vector3(
                    (x + stepX * (runLength - 1) * 0.5f + 0.5f) * cellWidth,
                    0f,
                    (y + stepY * (runLength - 1) * 0.5f + 0.5f) * cellHeight);

                // 半幅版は歩道側と継ぎ目側で向きが逆になるため、L型側溝と同じ規約で180度反転させる。
                // 対称な形で作られていれば反転しても見た目は変わらないので無害。
                float yaw = isVertical ? 0f : 90f;
                if (isBoundary && roadOffsetFromFarEdge[x, y] == 0)
                {
                    yaw += 180f;
                }

                bars.Add(new MergedTile
                {
                    Prefab = prefab,
                    LocalPosition = localPosition,
                    Rotation = Quaternion.Euler(0f, yaw, 0f),
                });

                for (int i = 0; i < runLength; i++)
                {
                    consumedCells.Add(new Vector2Int(x + stepX * i, y + stepY * i));
                }
            }
        }
    }

    private GameObject GetRoadTileVariant(int x, int y, out Quaternion rotation)
    {
        if (roadClassMap[x, y] == RoadClass.Major)
        {
            // 幹線道路の1マス版バリエーション（内側/縁/チャンク境界/交差点）は廃止した。
            // 全幅（3m×12m）・半幅（3m×6m）の一体タイルへ移行する計画のため、ここでは無地のroadPrefabを使う。
            rotation = GetMajorRoadTileRotation(x, y, ClassifyMajorRoadTile(x, y));
            return roadPrefab;
        }
        else
        {
            RoadTileKind kind = ClassifyLocalRoadTile(x, y);
            rotation = GetLocalRoadTileRotation(x, y, kind);

            GameObject variant = kind == RoadTileKind.EdgeToSidewalk ? localRoadEdgePrefab : null;
            return variant != null ? variant : roadPrefab;
        }
    }

    private List<CombineInstance> GetOrCreateCombineList(Dictionary<GameObject, List<CombineInstance>> combineByPrefab, GameObject prefab)
    {
        if (!combineByPrefab.TryGetValue(prefab, out List<CombineInstance> list))
        {
            list = new List<CombineInstance>();
            combineByPrefab[prefab] = list;
        }

        return list;
    }

    private static bool HasUsableMesh(GameObject prefab)
    {
        if (prefab == null)
        {
            return false;
        }

        MeshFilter meshFilter = prefab.GetComponentInChildren<MeshFilter>();
        return meshFilter != null && meshFilter.sharedMesh != null;
    }

    // 地面タイルはすべて実寸（伸縮なし）で置く。セルの大きさ（cellWidth×cellHeight）に合わせて引き伸ばさず、
    // プレファブ自身のTransformスケールだけを反映する。納品仕様上は実寸・スケール=1が前提のため通常は無変換になるが、
    // 検証用にUnity上で手動作成したプレファブ（1m角のCubeをTransformのスケールで3mにしたもの等）も正しい大きさになる。
    // ルートの位置・回転は反映しない（配置位置と向きはグリッドから決める）。
    // 複数マテリアルのモデル（横断歩道のアスファルト＋白い縞など）に対応するため、サブメッシュ（＝マテリアル）ごとに
    // CombineInstanceを1つずつ作る。CombineInstanceは subMeshIndex（既定値0）で指定した1サブメッシュしか結合しないため、
    // 1つだけ作ると2つ目以降のサブメッシュが抜け落ちて穴になる。
    private void AddCombineInstance(List<CombineInstance> combineInstances, GameObject prefab, Vector3 localPosition, Quaternion rotation)
    {
        MeshFilter prefabMeshFilter = prefab.GetComponentInChildren<MeshFilter>();
        if (prefabMeshFilter == null || prefabMeshFilter.sharedMesh == null)
        {
            return;
        }

        Mesh mesh = prefabMeshFilter.sharedMesh;
        Matrix4x4 matrix = Matrix4x4.TRS(localPosition, rotation, prefab.transform.localScale);

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            combineInstances.Add(new CombineInstance
            {
                mesh = mesh,
                subMeshIndex = subMeshIndex,
                transform = matrix,
            });
        }
    }

    // combineInstancesをサブメッシュ（＝マテリアル）ごとに1つのMeshにまとめ、レンダリングと当たり判定の両方に同じMeshを使い回す。
    // サブメッシュ番号iのMeshには、プレファブのi番目のマテリアルを割り当てる（Unityのサブメッシュとマテリアルの対応と同じ）。
    // マテリアルごとに別オブジェクトにしている。1つのMeshに複数マテリアルを持たせるには「サブメッシュごとに結合→さらに結合」の
    // 2段階が必要で頂点のコピーが1回増える一方、描画呼び出しの数はどちらもマテリアルの数で変わらないため。
    // 単一マテリアルのモデルは、従来どおり1オブジェクト（名前もnameのまま）になる。
    private void CreateCombinedTileMesh(string name, GameObject prefab, List<CombineInstance> combineInstances)
    {
        if (prefab == null || combineInstances.Count == 0)
        {
            return;
        }

        MeshRenderer prefabRenderer = prefab.GetComponentInChildren<MeshRenderer>();
        if (prefabRenderer == null)
        {
            return;
        }

        Material[] materials = prefabRenderer.sharedMaterials;

        List<List<CombineInstance>> instancesBySubMesh = new List<List<CombineInstance>>();
        foreach (CombineInstance instance in combineInstances)
        {
            while (instancesBySubMesh.Count <= instance.subMeshIndex)
            {
                instancesBySubMesh.Add(new List<CombineInstance>());
            }

            instancesBySubMesh[instance.subMeshIndex].Add(instance);
        }

        for (int subMeshIndex = 0; subMeshIndex < instancesBySubMesh.Count; subMeshIndex++)
        {
            // マテリアルが足りないサブメッシュは、Unityでもプレファブ上で描画されないため同じく作らない。
            if (instancesBySubMesh[subMeshIndex].Count == 0 || subMeshIndex >= materials.Length)
            {
                continue;
            }

            // 1チャンク分（最大gridWidth×gridHeightセル）を結合すると65,535頂点を超え得るため、
            // 16bitインデックス（Unity既定）の上限を超えないようUInt32に切り替える。
            Mesh combinedMesh = new Mesh();
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combinedMesh.CombineMeshes(instancesBySubMesh[subMeshIndex].ToArray(), true, true);

            string objectName = instancesBySubMesh.Count == 1 ? name : $"{name}_{subMeshIndex}";
            GameObject combinedObject = new GameObject(objectName);
            combinedObject.transform.SetParent(groundParent.transform, false);

            MeshFilter meshFilter = combinedObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = combinedMesh;

            MeshRenderer meshRenderer = combinedObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = materials[subMeshIndex];

            MeshCollider meshCollider = combinedObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = combinedMesh;
        }
    }

    private Quaternion GetSidewalkRotation(int x, int y)
    {
        bool hasMajorLeft = x > 0 && roadClassMap[x - 1, y] == RoadClass.Major;
        bool hasMajorRight = x < gridWidth - 1 && roadClassMap[x + 1, y] == RoadClass.Major;
        bool hasMajorUp = y > 0 && roadClassMap[x, y - 1] == RoadClass.Major;
        bool hasMajorDown = y < gridHeight - 1 && roadClassMap[x, y + 1] == RoadClass.Major;

        bool leftOrRight = hasMajorLeft || hasMajorRight;
        bool upOrDown = hasMajorUp || hasMajorDown;

        if (leftOrRight && !upOrDown)
        {
            return Quaternion.Euler(0f, 90f, 0f);
        }

        return Quaternion.identity;
    }

    private struct LotBounds
    {
        public int minX, maxX, minY, maxY;
    }

    private struct BuildingPlacement
    {
        public int LocalX, LocalY, SizeCells;
    }

    // 大→中→小の順に試す（参考: https://ghoul-life.hatenablog.com/entry/2019/02/27/200305 のロジックを応用）。
    // 敷地は常に単一の矩形（SplitLotRecursiveが作る）なので、境界チェックのみで穴あき形状は考慮しない。
    // 小(1x1)は必ず置けるため、敷地全体が隙間なく埋まることが保証される。
    private static readonly int[] BuildingSizesLargestFirst = { 3, 2, 1 };

    private static List<BuildingPlacement> ComputeLotBuildingPlacements(int width, int height)
    {
        bool[,] occupied = new bool[width, height];
        List<BuildingPlacement> placements = new List<BuildingPlacement>();

        for (int ly = 0; ly < height; ly++)
        {
            for (int lx = 0; lx < width; lx++)
            {
                if (occupied[lx, ly])
                {
                    continue;
                }

                foreach (int size in BuildingSizesLargestFirst)
                {
                    if (lx + size > width || ly + size > height || !IsAreaFree(occupied, lx, ly, size))
                    {
                        continue;
                    }

                    MarkAreaOccupied(occupied, lx, ly, size);
                    placements.Add(new BuildingPlacement { LocalX = lx, LocalY = ly, SizeCells = size });
                    break;
                }
            }
        }

        return placements;
    }

    private static bool IsAreaFree(bool[,] occupied, int startX, int startY, int size)
    {
        for (int dy = 0; dy < size; dy++)
        {
            for (int dx = 0; dx < size; dx++)
            {
                if (occupied[startX + dx, startY + dy])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void MarkAreaOccupied(bool[,] occupied, int startX, int startY, int size)
    {
        for (int dy = 0; dy < size; dy++)
        {
            for (int dx = 0; dx < size; dx++)
            {
                occupied[startX + dx, startY + dy] = true;
            }
        }
    }

    private IEnumerator PlaceBuildingsRoutine()
    {
        // 以前の実装は「未処理の敷地IDを見つけるたびにグリッド全体を再走査」しており、
        // 敷地数×グリッド全セル数のオーダーになっていた（Profilerでこの関数のSelf時間が
        // 突出していた主因）。1回の全セル走査で全敷地のバウンディングボックスを同時に
        // 求めるよう修正し、O(グリッド全セル数)に削減した。
        UnityEngine.Profiling.Profiler.BeginSample("PlaceBuildings.ComputeLotBounds");
        Dictionary<int, LotBounds> lotBounds = new Dictionary<int, LotBounds>();
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (buildingMap[y, x] <= 0 || sidewalkMap[x, y] || trafficLightMap[x, y])
                {
                    continue;
                }

                int lotId = buildingMap[y, x];
                if (lotBounds.TryGetValue(lotId, out LotBounds bounds))
                {
                    bounds.minX = Mathf.Min(bounds.minX, x);
                    bounds.maxX = Mathf.Max(bounds.maxX, x);
                    bounds.minY = Mathf.Min(bounds.minY, y);
                    bounds.maxY = Mathf.Max(bounds.maxY, y);
                    lotBounds[lotId] = bounds;
                }
                else
                {
                    lotBounds[lotId] = new LotBounds { minX = x, maxX = x, minY = y, maxY = y };
                }
            }
        }
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        GameObject buildingsParent = new GameObject("Buildings");
        buildingsParent.transform.SetParent(groundParent.transform, false);

        UnityEngine.Profiling.Profiler.BeginSample("PlaceBuildings.Instantiate");
        int placedSinceYield = 0;
        // 敷地1個につき複数棟の建物が置かれるようになったため、破壊トラッキング（destroyedLotIds）は
        // SplitLotRecursiveの敷地IDではなく、ここで振る建物1棟ごとの一意なIDを使う。
        // 乱数はこのチャンク専用のrng（チャンク生成のシードで固定、他チャンク・他システムの影響を受けない）で、
        // lotBoundsの列挙順・敷地内の充填順も決定論的なため、再生成時も同じ建物に同じIDが振られる。
        int nextBuildingId = 1;
        foreach (KeyValuePair<int, LotBounds> kvp in lotBounds)
        {
            LotBounds bounds = kvp.Value;
            int width = bounds.maxX - bounds.minX + 1;
            int height = bounds.maxY - bounds.minY + 1;

            List<BuildingPlacement> placements = ComputeLotBuildingPlacements(width, height);
            foreach (BuildingPlacement placement in placements)
            {
                int buildingId = nextBuildingId++;

                Vector3 position = transform.position + new Vector3(
                    (bounds.minX + placement.LocalX + placement.SizeCells / 2.0f) * cellWidth,
                    0f,
                    (bounds.minY + placement.LocalY + placement.SizeCells / 2.0f) * cellHeight);

                float distance = Vector2.Distance(
                    new Vector2(position.x, position.z),
                    new Vector2(distanceOriginWorldPosition.x, distanceOriginWorldPosition.z));
                GameObject prefabToUse = billPrefab;
                if (raritySettings != null)
                {
                    BuildingRaritySettings.Phase phase = raritySettings.GetActivePhase(distance);
                    prefabToUse = raritySettings.Pick(phase, placement.SizeCells, billPrefab, rng);
                }

                // 建物の正面の向きをばらばらにするため、0/90/180/270度からランダムに回転させる。
                // 大中小はすべて正方形footprintのため、旧実装にあった回転時のwidth/height入れ替えは不要。
                int rotationSteps = RandomRange(0, 4);

                // 破壊済みの建物はチャンク再生成時に復活させない。
                // レア度と回転の抽選は、この判定より前に必ず行う。破壊済みの建物を抽選ごと飛ばすと
                // 乱数を使う回数が減り、以降の建物のモデルと向きが壊す前とずれてしまうため（基本設計.md参照）。
                if (destroyedLotIds.Contains(buildingId))
                {
                    continue;
                }

                if (prefabToUse != null)
                {
                    // プレファブは底面中心ピボット・実寸（伸縮なし）で作られている前提のため、スケールは触らない。
                    Quaternion rotation = Quaternion.Euler(0f, rotationSteps * 90f, 0f);

                    GameObject building = Instantiate(prefabToUse, position, rotation, buildingsParent.transform);
                    building.name = $"Building_{buildingId}";

                    BuildingInstance buildingInstance = building.AddComponent<BuildingInstance>();
                    buildingInstance.Initialize(chunkCoord, buildingId);

                    AddBuildingCollider(building, placement.SizeCells);
                }

                placedSinceYield++;
                if (placedSinceYield >= buildingsInstantiatedPerFrame)
                {
                    placedSinceYield = 0;
                    UnityEngine.Profiling.Profiler.EndSample();
                    yield return null;
                    UnityEngine.Profiling.Profiler.BeginSample("PlaceBuildings.Instantiate");
                }
            }
        }
        UnityEngine.Profiling.Profiler.EndSample();
    }

    // タスク6補足（基本設計.md参照）：CG班への当たり判定調整依頼を無くすため、コライダーはコード側で自動生成する。
    // 幅・奥行きはplacement.SizeCellsから確定サイズが分かるが、高さは建物モデルごとに異なり固定値では合わないため、
    // 実際にInstantiateしたモデルのRenderer.boundsから高さだけを実測して使う（幅・奥行きはモデルの見た目に関わらず
    // 敷地グリッドに正確に一致させたいため、実測値ではなくSizeCells側の確定値を使う）。
    private void AddBuildingCollider(GameObject building, int sizeCells)
    {
        Renderer[] renderers = building.GetComponentsInChildren<Renderer>();
        float height = 0f;
        foreach (Renderer renderer in renderers)
        {
            height = Mathf.Max(height, renderer.bounds.max.y - building.transform.position.y);
        }

        if (height <= 0f)
        {
            // レンダラーが見つからない（モデル未設定等）場合のフォールバック。見た目とズレる可能性はあるが、
            // コライダー自体が無い状態（当たり判定なし）よりは安全な既定値として1セル分の高さを使う。
            height = cellHeight;
        }

        BoxCollider collider = building.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, height / 2f, 0f);
        collider.size = new Vector3(sizeCells * cellWidth, height, sizeCells * cellHeight);
    }

    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public float CellWidth => cellWidth;
    public float CellHeight => cellHeight;

    public LandType GetCell(int x, int y)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return LandType.Empty;
        }

        return landMap[x, y];
    }

    // タスク12：指定したワールド座標から最も近い基幹道路（RoadClass.Major）セルを螺旋状に探索し、
    // そのセル中心のワールド座標を返す。プレイヤーの初期スポーン地点が建物の中にならないようにするため、
    // 「基幹道路の上」を安全な着地点として使う想定。同じ距離（同じ半径のリング）に複数の候補があれば
    // ランダムに1つを選ぶ。このチャンクの範囲内に基幹道路が1つも無い場合はfalseを返す
    // （外周道路が有効な既定設定では通常発生しない）。
    public bool TryFindNearestMajorRoadCell(Vector3 worldPosition, out Vector3 resultWorldPosition)
    {
        resultWorldPosition = Vector3.zero;

        if (roadClassMap == null)
        {
            return false;
        }

        Vector3 local = worldPosition - transform.position;
        int centerX = Mathf.Clamp(Mathf.FloorToInt(local.x / cellWidth), 0, gridWidth - 1);
        int centerY = Mathf.Clamp(Mathf.FloorToInt(local.z / cellHeight), 0, gridHeight - 1);

        int maxRadius = Mathf.Max(gridWidth, gridHeight);
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            CollectMajorRoadRingCandidates(centerX, centerY, radius, candidates);
            if (candidates.Count > 0)
            {
                // 生成後にPlayerSpawnerから同期的に呼ばれるスポーン位置の選択で、地形生成ではないため、
                // チャンク専用のrngではなく共有のUnityEngine.Randomを使う（rngを進めても生成結果には影響しないが、
                // 生成とは無関係な乱数消費を混ぜない方針）。
                Vector2Int chosen = candidates[Random.Range(0, candidates.Count)];
                resultWorldPosition = transform.position + new Vector3((chosen.x + 0.5f) * cellWidth, 0f, (chosen.y + 0.5f) * cellHeight);
                return true;
            }
        }

        return false;
    }

    // 中心セルから半径radiusの正方形リング上にある基幹道路セルだけをcandidatesに集める。
    // radius=0,1,2,...の順に呼び出せば、中心から近い順（同心円ではなく同心の正方形）に走査できる。
    private void CollectMajorRoadRingCandidates(int centerX, int centerY, int radius, List<Vector2Int> candidates)
    {
        candidates.Clear();

        if (radius == 0)
        {
            AddIfMajorRoadCell(centerX, centerY, candidates);
            return;
        }

        for (int dx = -radius; dx <= radius; dx++)
        {
            AddIfMajorRoadCell(centerX + dx, centerY - radius, candidates);
            AddIfMajorRoadCell(centerX + dx, centerY + radius, candidates);
        }

        for (int dy = -radius + 1; dy <= radius - 1; dy++)
        {
            AddIfMajorRoadCell(centerX - radius, centerY + dy, candidates);
            AddIfMajorRoadCell(centerX + radius, centerY + dy, candidates);
        }
    }

    private void AddIfMajorRoadCell(int x, int y, List<Vector2Int> candidates)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return;
        }

        if (roadClassMap[x, y] == RoadClass.Major)
        {
            candidates.Add(new Vector2Int(x, y));
        }
    }

    private void OnDrawGizmos()
    {
        if (landMap == null)
        {
            return;
        }

        Vector3 origin = transform.position;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector3 cellPosition = origin + new Vector3((x + 0.5f) * cellWidth, 0f, (y + 0.5f) * cellHeight);

                switch (landMap[x, y])
                {
                    case LandType.Lot:
                        Gizmos.color = Color.green;
                        break;
                    case LandType.Road:
                        Gizmos.color = Color.gray;
                        break;
                    case LandType.Building:
                        Gizmos.color = Color.red;
                        break;
                    default:
                        Gizmos.color = Color.clear;
                        break;
                }

                Gizmos.DrawWireCube(cellPosition, new Vector3(cellWidth, 0.05f, cellHeight));
            }
        }
    }
}

