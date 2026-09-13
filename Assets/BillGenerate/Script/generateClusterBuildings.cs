using UnityEngine;
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
    [SerializeField] private int gridWidth = 20;
    [Tooltip("チャンク生成時は「1チャンクあたりの縦方向マス数」として扱われる。")]
    [SerializeField] private int gridHeight = 20;

    [Header("Cell Size")]
    [SerializeField] private float cellWidth = 3f;
    [SerializeField] private float cellHeight = 3f;

    [Header("Road Generation")]
    [SerializeField] private RoadGenerationMode roadGenerationMode = RoadGenerationMode.RecursiveSubdivision;
    [SerializeField] private int majorRoadMinInterval = 8;
    [SerializeField] private int majorRoadMaxInterval = 14;
    [SerializeField] private int majorRoadWidth = 3;
    [SerializeField] private bool generateOuterBoundaryRoads = true;
    [SerializeField] private int randomSeed = 0;
    [Tooltip("RecursiveSubdivisionモードでのみ使用。区画が分割可能でもこの確率で分割を打ち切り、大きめの街区として確定する。")]
    [SerializeField] [Range(0f, 1f)] private float recursiveSplitStopChance = 0.15f;

    [Header("Global Lattice Variance")]
    [Tooltip("GlobalLatticeモード専用。ワールド座標のノイズで格子の粗密を変化させる強さ。0だとジッター幅が一定になる。")]
    [SerializeField] [Range(0f, 1f)] private float latticeVarianceStrength = 0.5f;
    [Tooltip("GlobalLatticeモード専用。ノイズが低いセルの道路を間引いて隣と合体させ、大きめの街区を作る最大確率。")]
    [SerializeField] [Range(0f, 0.9f)] private float latticeBlockMergeChance = 0.25f;
    [Tooltip("GlobalLatticeモード専用。粗密ノイズのスケール。小さいほど粗密の切り替わりが緩やかになる。")]
    [SerializeField] private float latticeNoiseFrequency = 0.15f;
    [Tooltip("GlobalLatticeモード専用。街区合体のムラ（クラスター）を作る低周波ノイズのスケール。latticeNoiseFrequencyより小さい値にすると、広い範囲でまとまって大きい街区が生まれるエリアができる。")]
    [SerializeField] private float latticeClusterFrequency = 0.02f;
    [Tooltip("GlobalLatticeモード専用。クラスターノイズが合体確率に与える影響の強さ。0でクラスター化なし（latticeBlockMergeChanceが一様に効く従来通り）。1に近いほど「合体しまくるエリア」と「全く合体しないエリア」の差がはっきり出る。")]
    [SerializeField] [Range(0f, 1f)] private float latticeClusterStrength = 0.6f;

    [Header("Building Generation")]
    [SerializeField] private int minBuildingSize = 1;
    [SerializeField] private int maxBuildingSize = 4;
    [SerializeField] private int buildingPaddingWidth = 1;
    [Tooltip("街区を敷地に再帰分割する際、まだ分割可能でもこの確率で打ち切り、大きめの敷地として確定する。")]
    [SerializeField] [Range(0f, 1f)] private float lotSplitStopChance = 0.35f;
    [Tooltip("パフォーマンス課題2対策：建物Instantiateを1フレームあたりこの数だけ処理してyieldする。小さいほど1フレームの負荷は下がるが、チャンク1個の生成完了までのフレーム数は伸びる。")]
    [SerializeField] private int buildingsInstantiatedPerFrame = 40;

    [Header("Guardrail Generation")]
    [SerializeField] private bool enablePeriodicPortals = false;
    [SerializeField] private int guardrailPortalInterval = 8;
    [SerializeField] private int portalMinSpacing = 3;
    [SerializeField] private bool useManualPortalSeeds = false;
    [SerializeField] private Vector2Int[] manualPortalSeeds;

    [Header("Prefabs")]
    [SerializeField] private GameObject sitePrefab;
    [SerializeField] private GameObject roadPrefab;
    [SerializeField] private GameObject trafficLightsPrefab;
    [SerializeField] private GameObject guardrailPrefab;
    [SerializeField] private GameObject billPrefab;
    [SerializeField] private GameObject crosswalkPrefab;

    [Header("Road Tile Variants (Major, タスク10)")]
    [Tooltip("未設定ならroadPrefabにフォールバックする。対向車線側（帯の中央寄り）のタイル。センターライン用。")]
    [SerializeField] private GameObject majorRoadInteriorPrefab;
    [Tooltip("未設定ならroadPrefabにフォールバックする。歩道側（敷地に接する縁）のタイル。L型側溝用。")]
    [SerializeField] private GameObject majorRoadEdgePrefab;
    [Tooltip("未設定ならroadPrefabにフォールバックする。チャンク境界側（外周道路帯）のタイル。")]
    [SerializeField] private GameObject majorRoadChunkBoundaryPrefab;
    [Tooltip("未設定ならroadPrefabにフォールバックする。交差点タイル（現状は十字路のみ想定）。")]
    [SerializeField] private GameObject majorRoadIntersectionPrefab;

    [Header("Road Tile Variants (Local, タスク10)")]
    [Tooltip("未設定ならroadPrefabにフォールバックする。歩道側（敷地に接する縁）のタイル。L型側溝用。")]
    [SerializeField] private GameObject localRoadEdgePrefab;

    [Header("Building Rarity")]
    [Tooltip("未設定の場合は常にbillPrefabを使用する。設定すると距離に応じて抽選されたプレファブを使用する。")]
    [SerializeField] private BuildingRaritySettings raritySettings;
    [Tooltip("レア度抽選の距離を測る基準点（通常はマップのスタート地点＝ワールド原点）。")]
    [SerializeField] private Vector3 distanceOriginWorldPosition = Vector3.zero;

    [Header("Chunk Streaming")]
    [Tooltip("falseにするとAwakeで自動生成しない。ChunkManagerがGenerateChunkを明示的に呼び出す運用で使用する。")]
    [SerializeField] private bool autoGenerateOnAwake = true;

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
    private bool[,] guardrailMap;
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
        guardrailPortalInterval = Mathf.Max(1, guardrailPortalInterval);
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
        landMap = new LandType[gridWidth, gridHeight];
        buildingMap = new int[gridHeight, gridWidth];
        roadClassMap = new RoadClass[gridWidth, gridHeight];
        guardrailMap = new bool[gridWidth, gridHeight];
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
                guardrailMap[x, y] = false;
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

        UnityEngine.Profiling.Profiler.BeginSample("GenerateGuardrailLayout");
        GenerateGuardrailLayout();
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("GenerateCrosswalksAndTrafficLights");
        GenerateCrosswalksAndTrafficLights();
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        yield return BuildGroundTilesRoutine();

        Debug.Log($"City grid generated: {gridWidth} x {gridHeight}. Lots and buildings placed.");
        onComplete?.Invoke();
    }

    private void GenerateMajorRoads()
    {
        if (randomSeed != 0)
        {
            Random.InitState(randomSeed);
        }

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
                // オフセットはClassifyMajorRoadTileでEdgeToChunkBoundaryとして無条件に扱われるため使われない。
                bool isVertical = isLeftEdge || isRightEdge;
                MarkMajorRoadStripCell(x, y, isVertical, 0, boundaryWidth);
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
    private int MacroCellJitterRange => Mathf.Max(0, (majorRoadMaxInterval - majorRoadMinInterval) / 2);

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

        // ノイズが高いセルほどジッター幅を広げ、低いセルほど規則正しくする。
        // latticeVarianceStrength=0なら常にbaseJitterRangeのまま（従来通り）。
        float noise = SampleLatticeNoise(isVertical, cellIndex);
        float scale = Mathf.Lerp(1f - latticeVarianceStrength, 1f + latticeVarianceStrength, noise);
        int jitterRange = Mathf.Clamp(Mathf.RoundToInt(baseJitterRange * scale), 0, baseJitterRange * 2);

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
        else if (!mustSplitX && !mustSplitY && Random.value < recursiveSplitStopChance)
        {
            // まだ分割可能でも、一定確率で打ち切って大きめの街区として確定する。
            return;
        }
        else if (canSplitX && canSplitY)
        {
            // 長辺側を優先的に割る。ほぼ正方形の場合は五分五分でランダムに決める。
            splitVertical = width != height ? width > height : Random.value < 0.5f;
        }
        else
        {
            splitVertical = canSplitX;
        }

        if (splitVertical)
        {
            int splitX = Random.Range(x1 + majorRoadMinInterval, x2 - majorRoadMinInterval - majorRoadWidth + 1);
            PaintRoadStrip(true, splitX, majorRoadWidth, y1, y2);
            SplitBlockRecursive(x1, y1, splitX, y2);
            SplitBlockRecursive(splitX + majorRoadWidth, y1, x2, y2);
        }
        else
        {
            int splitY = Random.Range(y1 + majorRoadMinInterval, y2 - majorRoadMinInterval - majorRoadWidth + 1);
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

        int currentPos = start + Random.Range(minInterval, maxInterval + 1);
        while (currentPos < end)
        {
            positions.Add(currentPos);
            currentPos += Random.Range(minInterval, maxInterval + 1);
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

        // チャンクの最外周セル（外周道路帯）は対岸＝隣接チャンク側の状態を参照できないため、
        // 帯のオフセット判定を行わず一律チャンク境界として扱う。
        if (x == 0 || x == gridWidth - 1 || y == 0 || y == gridHeight - 1)
        {
            return RoadTileKind.EdgeToChunkBoundary;
        }

        if (isMajorIntersectionCell[x, y] || HasAdjacentDifferentAxisMajorRoad(x, y))
        {
            return RoadTileKind.Intersection;
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

    // Local道路（生活道路）タイルの回転角を求める。既存のGetGuardrailRotationと同じ考え方で、
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
    // 接している辺の数だけガードレールに1マスずつ奪われるため、その分だけ最小サイズを底上げして、
    // ガードレールの奥に必ずminBuildingSize分の建物用セルが残るようにする。
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
        else if (!mustSplitX && !mustSplitY && Random.value < lotSplitStopChance)
        {
            // まだ分割可能でも、一定確率で打ち切って1つの敷地として確定する。
            PaintMapRectLot(lotId, x1, y1, x2, y2);
            lotId++;
            return;
        }
        else if (canSplitX && canSplitY)
        {
            splitVertical = width != height ? width > height : Random.value < 0.5f;
        }
        else
        {
            splitVertical = canSplitX;
        }

        if (splitVertical)
        {
            int splitX = Random.Range(x1 + minSizeXMinSide, x2 - minSizeXMaxSide - buildingPaddingWidth + 1);
            SplitLotRecursive(x1, y1, splitX, y2, touchesXMin, false, touchesYMin, touchesYMax, ref lotId);
            SplitLotRecursive(splitX + buildingPaddingWidth, y1, x2, y2, false, touchesXMax, touchesYMin, touchesYMax, ref lotId);
        }
        else
        {
            int splitY = Random.Range(y1 + minSizeYMinSide, y2 - minSizeYMaxSide - buildingPaddingWidth + 1);
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

    // 街区の角（大道路2方向に接するセル）を信号機に変更し、対岸が信号機/ガードレールなら
    // その間の大道路セルを横断歩道に変える。さらにガードレールに挟まれた生活道路も横断歩道にする。
    private void GenerateCrosswalksAndTrafficLights()
    {
        MarkBlockCornersAsTrafficLights();
        GenerateCrosswalksAtTrafficLights();
        ConvertGuardrailSandwichedLocalRoadsToCrosswalks();
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
                    guardrailMap[x, y] = false;
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

        // 信号機セルをすべて先に列挙してから処理する。ループ中に対岸をガードレール→信号機に
        // 書き換えることがあるため、列挙と同時に処理すると別の判定に影響してしまう。
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

                // その方向の大道路帯を対岸に向かって歩き、途中でグリッド端（＝チャンクの継ぎ目）に
                // 突き当たるかどうかを先に調べる。外周道路（PaintOuterBoundaryRoads）は必ずグリッド端に
                // 接するが、内部の幹線道路はsafetyGapにより端に接しないため、この判定で
                // 「チャンク境界の外周道路」と「チャンク内部で完結する幹線道路」を区別できる。
                int edgeX = roadX;
                int edgeY = roadY;
                bool reachedGridEdge = false;

                while (true)
                {
                    int nx = edgeX + dir.x;
                    int ny = edgeY + dir.y;

                    if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                    {
                        reachedGridEdge = true;
                        break;
                    }

                    if (roadClassMap[nx, ny] != RoadClass.Major)
                    {
                        break;
                    }

                    edgeX = nx;
                    edgeY = ny;
                }

                if (reachedGridEdge)
                {
                    // 対岸の信号機は隣接チャンク側のグリッドにあり、このチャンク単独では参照できない。
                    // 対岸探索は諦め、このチャンクが担当する外周道路帯（半分）をそのまま横断歩道にする。
                    // こうしないと、チャンクの継ぎ目には横断歩道が一切生成されなくなってしまう。
                    int px = roadX;
                    int py = roadY;

                    while (true)
                    {
                        crosswalkMap[px, py] = true;

                        if (px == edgeX && py == edgeY)
                        {
                            break;
                        }

                        px += dir.x;
                        py += dir.y;
                    }

                    continue;
                }

                int targetX = cell.x + dir.x * crossDistance;
                int targetY = cell.y + dir.y * crossDistance;

                if (targetX < 0 || targetX >= gridWidth || targetY < 0 || targetY >= gridHeight)
                {
                    continue;
                }

                // 対岸が既に信号機（＝本当の交差点の角）のときだけ横断歩道を生成する。
                // 対岸がガードレールのまま（＝街区の直線区間の途中）の場合は何もしない。
                if (!trafficLightMap[targetX, targetY])
                {
                    continue;
                }

                for (int step = 1; step <= majorRoadWidth; step++)
                {
                    int px = cell.x + dir.x * step;
                    int py = cell.y + dir.y * step;

                    if (roadClassMap[px, py] == RoadClass.Major)
                    {
                        crosswalkMap[px, py] = true;
                    }
                }
            }
        }
    }

    private void ConvertGuardrailSandwichedLocalRoadsToCrosswalks()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (roadClassMap[x, y] != RoadClass.Local)
                {
                    continue;
                }

                bool sandwichedHorizontally = x > 0 && x < gridWidth - 1 && guardrailMap[x - 1, y] && guardrailMap[x + 1, y];
                bool sandwichedVertically = y > 0 && y < gridHeight - 1 && guardrailMap[x, y - 1] && guardrailMap[x, y + 1];

                if (sandwichedHorizontally || sandwichedVertically)
                {
                    crosswalkMap[x, y] = true;
                }
            }
        }
    }

    private void GenerateGuardrailLayout()
    {
        ComputeLocalRoadDistanceFromMajor();

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                guardrailMap[x, y] = false;
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

                guardrailMap[x, y] = true;
            }
        }

        // Mandatory portal cells are always kept open, even if previous rules marked them as guardrail.
        EnforceMandatoryPortalOverride();

        EnsureEachInteriorLocalComponentHasOpenPortal();
        MarkBuildingCellsAdjacentToMajorRoadsAsGuardrails();
        EnforceMajorRoadBlockCornerTrafficLights();

        // 信号機セルはガードレール対象から除外する。
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (trafficLightMap[x, y])
                {
                    guardrailMap[x, y] = false;
                }
            }
        }
    }

    private void EnforceMajorRoadBlockCornerTrafficLights()
    {
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (!IsMajorRoadBlockCornerCell(x, y))
                {
                    continue;
                }

                trafficLightMap[x, y] = true;
                guardrailMap[x, y] = false;
            }
        }
    }

    private bool IsMajorRoadBlockCornerCell(int x, int y)
    {
        if (roadClassMap[x, y] != RoadClass.Local)
        {
            return false;
        }

        return IsAdjacentToMajorRoad(x, y) && IsLocalCornerCell(x, y);
    }

    private void MarkBuildingCellsAdjacentToMajorRoadsAsGuardrails()
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
                    guardrailMap[x, y] = true;
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
                if (axisValue % guardrailPortalInterval == 0)
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
                guardrailMap[x, y] = false;
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
                    if (!guardrailMap[edge.x, edge.y])
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

                guardrailMap[selected.x, selected.y] = false;
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

    private bool IsLocalCornerCell(int x, int y)
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

        if (localConnectionCount != 2)
        {
            return false;
        }

        bool horizontal = hasLocalLeft && hasLocalRight;
        bool vertical = hasLocalUp && hasLocalDown;
        bool isCorner = !horizontal && !vertical;
        return isCorner;
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

        // 敷地・道路・横断歩道は個別に触る対象ではないため、チャンク単位でメッシュ結合しDraw Call数を
        // 削減する（パフォーマンス課題1対策）。信号機・ガードレールは、将来同じマスに「破壊可能な信号機/
        // ガードレール」を設置する可能性がある（BuildingInstanceと同じパターンを流用する想定）ため、
        // 結合せず個別GameObjectのまま残す。結合してしまうと1本だけ個別に破壊することができなくなるため。
        List<CombineInstance> siteCombine = new List<CombineInstance>();
        // タスク10：道路タイルはセルごとに使うプレファブ（センターライン/L型側溝等）が変わり得るため、
        // プレファブ種類ごとにCombineInstanceリストを持つ辞書で管理する。
        Dictionary<GameObject, List<CombineInstance>> roadCombineByPrefab = new Dictionary<GameObject, List<CombineInstance>>();
        List<CombineInstance> crosswalkCombine = new List<CombineInstance>();

        UnityEngine.Profiling.Profiler.BeginSample("BuildGroundTiles.CollectCombineInstances");
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (guardrailMap[x, y])
                {
                    continue;
                }

                Vector3 localPosition = new Vector3((x + 0.5f) * cellWidth, 0f, (y + 0.5f) * cellHeight);
                Vector3 scale = new Vector3(cellWidth, 1f, cellHeight);

                if (trafficLightMap[x, y] && trafficLightsPrefab != null)
                {
                    GameObject tile = Instantiate(trafficLightsPrefab, transform.position + localPosition, Quaternion.identity, groundParent.transform);
                    tile.name = $"TrafficLight_{x}_{y}";
                    tile.transform.localScale = scale;
                    continue;
                }

                Matrix4x4 matrix = Matrix4x4.TRS(localPosition, Quaternion.identity, scale);

                if (crosswalkMap[x, y] && crosswalkPrefab != null)
                {
                    AddCombineInstance(crosswalkCombine, crosswalkPrefab, matrix);
                }
                else if (landMap[x, y] == LandType.Road)
                {
                    GameObject roadVariantPrefab = GetRoadTileVariant(x, y, out Quaternion roadRotation);
                    Matrix4x4 roadMatrix = Matrix4x4.TRS(localPosition, roadRotation, scale);
                    AddCombineInstance(GetOrCreateCombineList(roadCombineByPrefab, roadVariantPrefab), roadVariantPrefab, roadMatrix);
                }
                else
                {
                    AddCombineInstance(siteCombine, sitePrefab, matrix);
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
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        UnityEngine.Profiling.Profiler.BeginSample("PlaceGuardrails");
        PlaceGuardrails();
        UnityEngine.Profiling.Profiler.EndSample();
        yield return null;

        yield return PlaceBuildingsRoutine();
    }

    // セルの分類（RoadTileKind）から、実際に使う道路タイルのプレファブと回転を決める。
    // 対応するバリエーションが未設定（null）の場合は既存のroadPrefabにフォールバックするため、
    // テクスチャ・プレファブが未整備のままでも従来通りの見た目で動作する。
    private GameObject GetRoadTileVariant(int x, int y, out Quaternion rotation)
    {
        if (roadClassMap[x, y] == RoadClass.Major)
        {
            RoadTileKind kind = ClassifyMajorRoadTile(x, y);
            rotation = GetMajorRoadTileRotation(x, y, kind);

            GameObject variant = kind switch
            {
                RoadTileKind.Interior => majorRoadInteriorPrefab,
                RoadTileKind.EdgeToSidewalk => majorRoadEdgePrefab,
                RoadTileKind.EdgeToChunkBoundary => majorRoadChunkBoundaryPrefab,
                RoadTileKind.Intersection => majorRoadIntersectionPrefab,
                _ => null,
            };

            return variant != null ? variant : roadPrefab;
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

    private void AddCombineInstance(List<CombineInstance> combineInstances, GameObject prefab, Matrix4x4 matrix)
    {
        MeshFilter prefabMeshFilter = prefab.GetComponentInChildren<MeshFilter>();
        if (prefabMeshFilter == null || prefabMeshFilter.sharedMesh == null)
        {
            return;
        }

        combineInstances.Add(new CombineInstance
        {
            mesh = prefabMeshFilter.sharedMesh,
            transform = matrix,
        });
    }

    // combineInstancesを1つのMeshにまとめ、レンダリングと当たり判定の両方に同じMeshを使い回す。
    // 敷地・道路・横断歩道はcellWidth×cellHeightの単純な板（Unity組み込みCubeメッシュ）のみを想定しており、
    // マルチマテリアル・複数サブメッシュのprefabに差し替えた場合はmergeSubMeshes=trueにより見た目が崩れる点に注意。
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

        // 1チャンク分（最大gridWidth×gridHeightセル）を結合すると65,535頂点を超え得るため、
        // 16bitインデックス（Unity既定）の上限を超えないようUInt32に切り替える。
        Mesh combinedMesh = new Mesh();
        combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);

        GameObject combinedObject = new GameObject(name);
        combinedObject.transform.SetParent(groundParent.transform, false);

        MeshFilter meshFilter = combinedObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = combinedMesh;

        MeshRenderer meshRenderer = combinedObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = prefabRenderer.sharedMaterial;

        MeshCollider meshCollider = combinedObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = combinedMesh;
    }

    private void PlaceGuardrails()
    {
        if (guardrailPrefab == null)
        {
            Debug.LogWarning("guardrailPrefab is not assigned. Guardrail placement is skipped.");
            return;
        }

        GameObject guardrailParent = new GameObject("Guardrails");
        guardrailParent.transform.SetParent(groundParent.transform, false);

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (!guardrailMap[x, y])
                {
                    continue;
                }

                if (trafficLightMap[x, y])
                {
                    continue;
                }

                Vector3 position = transform.position + new Vector3((x + 0.5f) * cellWidth, 0f, (y + 0.5f) * cellHeight);
                Quaternion rotation = GetGuardrailRotation(x, y);
                GameObject guardrail = Instantiate(guardrailPrefab, position, rotation, guardrailParent.transform);
                guardrail.name = $"Guardrail_{x}_{y}";
            }
        }
    }

    private Quaternion GetGuardrailRotation(int x, int y)
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
                if (buildingMap[y, x] <= 0 || guardrailMap[x, y] || trafficLightMap[x, y])
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
        foreach (KeyValuePair<int, LotBounds> kvp in lotBounds)
        {
            int lotId = kvp.Key;

            // 破壊済みの建物はチャンク再生成時に復活させない。
            if (destroyedLotIds.Contains(lotId))
            {
                continue;
            }

            LotBounds bounds = kvp.Value;
            int width = bounds.maxX - bounds.minX + 1;
            int height = bounds.maxY - bounds.minY + 1;
            Vector3 position = transform.position + new Vector3((bounds.minX + width / 2.0f) * cellWidth, 0.5f, (bounds.minY + height / 2.0f) * cellHeight);

            float distance = Vector2.Distance(
                new Vector2(position.x, position.z),
                new Vector2(distanceOriginWorldPosition.x, distanceOriginWorldPosition.z));
            GameObject prefabToUse = raritySettings != null ? raritySettings.Pick(distance, billPrefab) : billPrefab;

            if (prefabToUse != null)
            {
                // 建物の正面の向きをばらばらにするため、0/90/180/270度からランダムに回転させる。
                // 90度・270度回転時はワールドのX/Z軸が入れ替わるため、スケールに割り当てるwidth/heightも
                // 入れ替えることで、どの回転角でも敷地の矩形からはみ出さないようにする。
                int rotationSteps = Random.Range(0, 4);
                Quaternion rotation = Quaternion.Euler(0f, rotationSteps * 90f, 0f);
                bool axisSwapped = rotationSteps % 2 == 1;

                GameObject building = Instantiate(prefabToUse, position, rotation, buildingsParent.transform);
                building.name = $"Building_{lotId}";
                float originalHeightScale = prefabToUse.transform.localScale.y;
                float scaleX = (axisSwapped ? height : width) * cellWidth;
                float scaleZ = (axisSwapped ? width : height) * cellHeight;
                building.transform.localScale = new Vector3(scaleX, originalHeightScale, scaleZ);

                BuildingInstance buildingInstance = building.AddComponent<BuildingInstance>();
                buildingInstance.Initialize(chunkCoord, lotId);
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
        UnityEngine.Profiling.Profiler.EndSample();
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

