using UnityEngine;
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
    }

    [Header("Grid")]
    [SerializeField] private int gridWidth = 20;
    [SerializeField] private int gridHeight = 20;

    [Header("Cell Size")]
    [SerializeField] private float cellWidth = 1f;
    [SerializeField] private float cellHeight = 1f;

    [Header("Road Generation")]
    [SerializeField] private RoadGenerationMode roadGenerationMode = RoadGenerationMode.RecursiveSubdivision;
    [SerializeField] private int majorRoadMinInterval = 8;
    [SerializeField] private int majorRoadMaxInterval = 14;
    [SerializeField] private int majorRoadWidth = 3;
    [SerializeField] private int outerRoadWidth = 2;
    [SerializeField] private bool generateOuterBoundaryRoads = true;
    [SerializeField] private int randomSeed = 0;
    [Tooltip("RecursiveSubdivisionモードでのみ使用。区画が分割可能でもこの確率で分割を打ち切り、大きめの街区として確定する。")]
    [SerializeField] [Range(0f, 1f)] private float recursiveSplitStopChance = 0.15f;

    [Header("Building Generation")]
    [SerializeField] private int minBuildingSize = 1;
    [SerializeField] private int maxBuildingSize = 4;
    [SerializeField] private int buildingPaddingWidth = 1;
    [Tooltip("街区を敷地に再帰分割する際、まだ分割可能でもこの確率で打ち切り、大きめの敷地として確定する。")]
    [SerializeField] [Range(0f, 1f)] private float lotSplitStopChance = 0.35f;

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

    private static readonly Vector2Int[] FourDirections =
    {
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(0, 1),
    };

    private LandType[,] landMap;
    private int[,] buildingMap;
    private RoadClass[,] roadClassMap;
    private bool[,] guardrailMap;
    private bool[,] trafficLightMap;
    private bool[,] crosswalkMap;
    private bool[,] portalMap;
    private int[,] localRoadDistanceFromMajor;
    private GameObject groundParent;

    private void Reset()
    {
        AutoConfigureCellSizeFromPrefab();
    }

    private void OnValidate()
    {
        gridWidth = Mathf.Max(1, gridWidth);
        gridHeight = Mathf.Max(1, gridHeight);
        majorRoadMinInterval = Mathf.Max(1, majorRoadMinInterval);
        majorRoadMaxInterval = Mathf.Max(majorRoadMinInterval, majorRoadMaxInterval);
        majorRoadWidth = Mathf.Max(1, majorRoadWidth);
        outerRoadWidth = Mathf.Max(1, outerRoadWidth);
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
        GenerateLand();
    }

    public void GenerateLand()
    {
        landMap = new LandType[gridWidth, gridHeight];
        buildingMap = new int[gridHeight, gridWidth];
        roadClassMap = new RoadClass[gridWidth, gridHeight];
        guardrailMap = new bool[gridWidth, gridHeight];
        trafficLightMap = new bool[gridWidth, gridHeight];
        crosswalkMap = new bool[gridWidth, gridHeight];
        portalMap = new bool[gridWidth, gridHeight];
        localRoadDistanceFromMajor = new int[gridWidth, gridHeight];

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
            }
        }

        GenerateMajorRoads();
        GenerateLots();
        ConvertRemainingLotGapsToLocalRoads();
        GenerateGuardrailLayout();
        GenerateCrosswalksAndTrafficLights();
        BuildGroundTiles();
        Debug.Log($"City grid generated: {gridWidth} x {gridHeight}. Lots and buildings placed.");
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
            case RoadGenerationMode.Grid:
            default:
                GenerateMajorRoadsGrid();
                break;
        }
    }

    private void PaintOuterBoundaryRoads()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                bool isLeftEdge = x < outerRoadWidth;
                bool isRightEdge = x >= gridWidth - outerRoadWidth;
                bool isBottomEdge = y < outerRoadWidth;
                bool isTopEdge = y >= gridHeight - outerRoadWidth;

                if (isLeftEdge || isRightEdge || isBottomEdge || isTopEdge)
                {
                    MarkRoadCell(x, y, RoadClass.Major);
                }
            }
        }
    }

    private void GenerateMajorRoadsGrid()
    {
        List<int> majorRoadXPositions = GenerateRandomRoadPositions(outerRoadWidth, gridWidth - outerRoadWidth, majorRoadMinInterval, majorRoadMaxInterval);
        List<int> majorRoadYPositions = GenerateRandomRoadPositions(outerRoadWidth, gridHeight - outerRoadWidth, majorRoadMinInterval, majorRoadMaxInterval);

        PaintRoadLinesByPositions(true, majorRoadXPositions, majorRoadWidth);
        PaintRoadLinesByPositions(false, majorRoadYPositions, majorRoadWidth);
    }

    // 敷地全体を1つの区画とみなし、道路1本で2分割→できた区画をそれぞれ独立した乱数でさらに分割…を繰り返す。
    // 区画ごとに分割位置・分割回数が異なるため、街区の大きさ・形が場所ごとにバラバラになる。
    private void GenerateMajorRoadsRecursive()
    {
        int x1 = generateOuterBoundaryRoads ? outerRoadWidth : 0;
        int y1 = generateOuterBoundaryRoads ? outerRoadWidth : 0;
        int x2 = generateOuterBoundaryRoads ? gridWidth - outerRoadWidth : gridWidth;
        int y2 = generateOuterBoundaryRoads ? gridHeight - outerRoadWidth : gridHeight;

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
                    MarkRoadCell(position + offset, y, RoadClass.Major);
                }
            }
        }
        else
        {
            for (int x = rangeStart; x < rangeEnd; x++)
            {
                for (int offset = 0; offset < width; offset++)
                {
                    MarkRoadCell(x, position + offset, RoadClass.Major);
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
                            MarkRoadCell(targetX, y, RoadClass.Major);
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
                            MarkRoadCell(x, targetY, RoadClass.Major);
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

    private void BuildGroundTiles()
    {
        if (sitePrefab == null)
        {
            Debug.LogWarning("sitePrefab is not assigned. Please assign Assets/Prefab/ground/site.prefab.");
            return;
        }

        if (roadPrefab == null)
        {
            Debug.LogWarning("roadPrefab is not assigned. Please assign Assets/Prefab/ground/road.prefab.");
            return;
        }

        if (groundParent != null)
        {
            DestroyImmediate(groundParent);
        }

        groundParent = new GameObject("GroundTiles");
        groundParent.transform.SetParent(transform, false);

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (guardrailMap[x, y])
                {
                    continue;
                }

                Vector3 position = transform.position + new Vector3((x + 0.5f) * cellWidth, 0f, (y + 0.5f) * cellHeight);
                GameObject prefabToUse = sitePrefab;

                string tileName;

                if (trafficLightMap[x, y] && trafficLightsPrefab != null)
                {
                    prefabToUse = trafficLightsPrefab;
                    tileName = "TrafficLight";
                }
                else if (crosswalkMap[x, y] && crosswalkPrefab != null)
                {
                    prefabToUse = crosswalkPrefab;
                    tileName = "Crosswalk";
                }
                else if (landMap[x, y] == LandType.Road)
                {
                    prefabToUse = roadPrefab;
                    tileName = "Road";
                }
                else
                {
                    tileName = buildingMap[y, x] > 0 ? $"Lot_{buildingMap[y, x]}" : "Lot";
                }

                GameObject tile = Instantiate(prefabToUse, position, Quaternion.identity, groundParent.transform);
                tile.name = $"{tileName}_{x}_{y}";
                tile.transform.localScale = new Vector3(cellWidth, 1f, cellHeight);
            }
        }

        PlaceGuardrails();
        PlaceBuildings();
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

    private void PlaceBuildings()
    {
        Dictionary<int, (Vector3 pos, int width, int height)> lotData = new Dictionary<int, (Vector3, int, int)>();

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (buildingMap[y, x] > 0 && !guardrailMap[x, y] && !trafficLightMap[x, y])
                {
                    int lotId = buildingMap[y, x];
                    if (!lotData.ContainsKey(lotId))
                    {
                        int minX = x, maxX = x;
                        int minY = y, maxY = y;

                        for (int yy = 0; yy < gridHeight; yy++)
                        {
                            for (int xx = 0; xx < gridWidth; xx++)
                            {
                                if (buildingMap[yy, xx] == lotId && !guardrailMap[xx, yy] && !trafficLightMap[xx, yy])
                                {
                                    minX = Mathf.Min(minX, xx);
                                    maxX = Mathf.Max(maxX, xx);
                                    minY = Mathf.Min(minY, yy);
                                    maxY = Mathf.Max(maxY, yy);
                                }
                            }
                        }

                        int width = maxX - minX + 1;
                        int height = maxY - minY + 1;
                        Vector3 lotCenterPos = transform.position + new Vector3((minX + width / 2.0f) * cellWidth, 0.5f, (minY + height / 2.0f) * cellHeight);

                        lotData[lotId] = (lotCenterPos, width, height);
                    }
                }
            }
        }

        GameObject buildingsParent = new GameObject("Buildings");
        buildingsParent.transform.SetParent(groundParent.transform, false);

        foreach (var kvp in lotData)
        {
            int lotId = kvp.Key;
            Vector3 position = kvp.Value.pos;
            int width = kvp.Value.width;
            int height = kvp.Value.height;

            if (billPrefab != null)
            {
                GameObject building = Instantiate(billPrefab, position, Quaternion.identity, buildingsParent.transform);
                building.name = $"Building_{lotId}";
                building.transform.localScale = new Vector3(width * cellWidth, 1f, height * cellHeight);
            }
        }
    }

    public LandType GetCell(int x, int y)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return LandType.Empty;
        }

        return landMap[x, y];
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

