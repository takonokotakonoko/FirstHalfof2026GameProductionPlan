using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 画面右上に円形のミニマップを表示する。
// プレイヤー上空の正射影カメラで街を撮影してRenderTextureに描き、円形マスクしたRawImageに表示する。
// 敵（EnemyMove）とプレイヤーはUIアイコンとして上から重ねる。
// カメラ・RenderTexture・Canvas・円形スプライトはすべて実行時に生成するため、
// シーンにはこのコンポーネントを1つ置くだけで動作する（プレファブや画像アセットは不要）。
public class MinimapController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("マップの中心にするプレイヤー。未設定なら\"Player\"タグのオブジェクトを自動で探す。")]
    [SerializeField] [JpLabel("プレイヤー")] private Transform player;
    [Tooltip("マップの向きを合わせるカメラ。未設定ならCamera.mainを使う。")]
    [SerializeField] [JpLabel("向きの基準カメラ")] private Camera referenceCamera;

    [Header("撮影")]
    [Tooltip("ミニマップの中心から縁までに映るワールド上の距離（m）。")]
    [SerializeField] [JpLabel("表示半径（m）")] private float mapRadiusMeters = 60f;
    [Tooltip("プレイヤーからミニマップカメラまでの高さ（m）。最も高い建物より高くすること。")]
    [SerializeField] [JpLabel("カメラ高さ（m）")] private float cameraHeight = 200f;
    [Tooltip("RenderTextureの解像度（正方形）。上げるほど精細になるが描画負荷が増える。")]
    [SerializeField] [JpLabel("テクスチャ解像度")] private int textureResolution = 256;
    [Tooltip("テクスチャ解像度にこの倍率を掛けた解像度で描き、ミップマップで縮小表示する。横断歩道の縞のような1ピクセルより細い模様が平均化され、ちらつきが減る。描画負荷の大半はドローコールなので、2倍程度ならほぼ変わらない。")]
    [Range(1, 4)]
    [SerializeField] [JpLabel("スーパーサンプリング倍率")] private int supersampling = 2;
    [Tooltip("ミニマップカメラが描画するレイヤー。")]
    [SerializeField] [JpLabel("描画レイヤー")] private LayerMask cullingMask = ~(1 << 5); // UIレイヤー以外
    [SerializeField] [JpLabel("背景色")] private Color backgroundColor = new Color(0.12f, 0.14f, 0.16f, 1f);
    [Tooltip("trueなら基準カメラの向きにマップを回転させる（画面上方向＝カメラの正面）。falseなら北（+Z）が常に上。")]
    [SerializeField] [JpLabel("カメラの向きに追従")] private bool rotateWithCamera = true;

    [Header("表示")]
    [Tooltip("ミニマップの直径（Canvas基準解像度1920x1080上のピクセル）。")]
    [SerializeField] [JpLabel("直径（px）")] private float mapDiameter = 260f;
    [Tooltip("画面右上の角からの余白（px）。")]
    [SerializeField] [JpLabel("画面端からの余白（px）")] private float screenMargin = 24f;
    [SerializeField] [JpLabel("枠の太さ（px）")] private float frameThickness = 5f;
    [SerializeField] [JpLabel("枠の色")] private Color frameColor = new Color(0.9f, 0.9f, 0.9f, 1f);

    [Header("アイコン")]
    [SerializeField] [JpLabel("プレイヤーアイコン色")] private Color playerIconColor = new Color(0.3f, 0.85f, 1f, 1f);
    [SerializeField] [JpLabel("プレイヤーアイコンサイズ（px）")] private float playerIconSize = 22f;
    [SerializeField] [JpLabel("敵アイコン色")] private Color enemyIconColor = new Color(1f, 0.25f, 0.2f, 1f);
    [SerializeField] [JpLabel("敵アイコンサイズ（px）")] private float enemyIconSize = 11f;
    [Tooltip("表示半径より遠い敵を、方向が分かるようにマップの縁に貼り付けて表示する。falseなら非表示。")]
    [SerializeField] [JpLabel("範囲外の敵を縁に表示")] private bool clampEnemiesToEdge = true;
    [Tooltip("シーン内の敵を探し直す間隔（秒）。敵はプール運用で増減するため定期的に再取得する。")]
    [SerializeField] [JpLabel("敵の再検索間隔（秒）")] private float enemyRefreshInterval = 0.25f;

    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;
    // ピクセルスナップの端数をuvRectでずらす際、テクスチャの外側（Clampで縁が引き伸ばされる部分）が
    // 見えないよう、表示範囲より少し広く撮影しておく余白（テクセル数）。
    private const int OverscanTexels = 2;

    private Camera minimapCamera;
    private RenderTexture renderTexture;
    private RectTransform mapRotator;
    private RawImage mapImage;
    private Texture2D circleTexture;
    private Texture2D arrowTexture;
    private Sprite circleSprite;
    private Sprite arrowSprite;
    private GameObject canvasObject;
    private RectTransform enemyIconRoot;
    private RectTransform playerIcon;
    private float innerRadiusPixels;

    private EnemyMove[] enemies = new EnemyMove[0];
    private readonly List<RectTransform> enemyIcons = new List<RectTransform>();
    private float enemyRefreshTimer;

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        if (referenceCamera == null)
        {
            referenceCamera = Camera.main;
        }

        if (player == null)
        {
            Debug.LogError("MinimapController: プレイヤーが見つかりません（\"Player\"タグを確認してください）。ミニマップは表示されません。");
            enabled = false;
            return;
        }

        CreateSprites();
        CreateMinimapCamera();
        CreateUI();
    }

    private void OnDestroy()
    {
        if (minimapCamera != null)
        {
            minimapCamera.targetTexture = null;
            Destroy(minimapCamera.gameObject);
        }

        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (canvasObject != null) Destroy(canvasObject);
        if (circleSprite != null) Destroy(circleSprite);
        if (arrowSprite != null) Destroy(arrowSprite);
        if (circleTexture != null) Destroy(circleTexture);
        if (arrowTexture != null) Destroy(arrowTexture);
    }

    // PlayerMoveがUpdateでメインカメラを動かした後に追従させるため、LateUpdateで処理する。
    private void LateUpdate()
    {
        if (player == null || minimapCamera == null)
        {
            return;
        }

        float mapYaw = GetMapYaw();
        Vector3 center = player.position;

        // ちらつき対策：カメラ自体は回さず常に北（+Z）を上にして撮影し、回転はUI側（mapRotator）で行う。
        // カメラを回すとピクセル格子がワールドに対して毎フレーム回り、細い縞の白/灰の判定が入れ替わってちらつく。
        // さらに位置を1テクセル単位にスナップしてピクセル格子をワールドに固定し、移動中に縞が這って見えるのを防ぐ。
        int renderResolution = renderTexture.width;
        float renderRadius = mapRadiusMeters * renderResolution / (renderResolution - OverscanTexels * 2f);
        float texelSize = renderRadius * 2f / renderResolution;
        float snappedX = Mathf.Floor(center.x / texelSize) * texelSize;
        float snappedZ = Mathf.Floor(center.z / texelSize) * texelSize;

        minimapCamera.transform.SetPositionAndRotation(
            new Vector3(snappedX, center.y + cameraHeight, snappedZ),
            Quaternion.Euler(90f, 0f, 0f));
        minimapCamera.orthographicSize = renderRadius;

        // スナップで切り捨てた端数（1テクセル未満）だけUVをずらし、プレイヤーを表示の中心に戻す。
        // 端数をそのまま捨てるとマップが1テクセルずつカクついて動いてしまう。
        float uvSize = mapRadiusMeters / renderRadius;
        float uvOffsetX = (center.x - snappedX) / (renderRadius * 2f);
        float uvOffsetY = (center.z - snappedZ) / (renderRadius * 2f);
        mapImage.uvRect = new Rect(0.5f - uvSize * 0.5f + uvOffsetX, 0.5f - uvSize * 0.5f + uvOffsetY, uvSize, uvSize);

        // 円は正方形に内接しているため、正方形のマップ画像をどれだけ回しても円形マスクの内側は常に覆われる。
        // UIのZ回転は反時計回りが正。カメラが東（Yaw=90）を向いたら、画像の東側が上に来るよう+90度回す。
        mapRotator.localRotation = Quaternion.Euler(0f, 0f, mapYaw);

        // プレイヤーの向き（ワールドのYaw）をマップの向き基準に直してアイコンを回す。
        // UIのZ回転は反時計回りが正なので符号を反転する。
        float playerYaw = player.eulerAngles.y;
        playerIcon.localRotation = Quaternion.Euler(0f, 0f, -(playerYaw - mapYaw));

        enemyRefreshTimer -= Time.deltaTime;
        if (enemyRefreshTimer <= 0f)
        {
            enemyRefreshTimer = enemyRefreshInterval;
            enemies = FindObjectsByType<EnemyMove>(FindObjectsSortMode.None);
        }

        UpdateEnemyIcons(center);
    }

    private float GetMapYaw()
    {
        if (!rotateWithCamera)
        {
            return 0f;
        }

        Transform basis = referenceCamera != null ? referenceCamera.transform : player;
        return basis.eulerAngles.y;
    }

    // 敵アイコンはmapRotatorの子なので、北が上のワールド座標のまま置けば地図と一緒に回転する。
    private void UpdateEnemyIcons(Vector3 center)
    {
        float pixelsPerMeter = innerRadiusPixels / mapRadiusMeters;
        float maxIconDistance = innerRadiusPixels - enemyIconSize * 0.5f;

        int visibleCount = 0;
        foreach (EnemyMove enemy in enemies)
        {
            // 前回の再検索以降にDestroyされた敵はUnityのnull判定でtrueになる。
            if (enemy == null || !enemy.isActiveAndEnabled)
            {
                continue;
            }

            Vector3 offset = enemy.transform.position - center;
            Vector2 uiPosition = new Vector2(offset.x, offset.z) * pixelsPerMeter;

            if (uiPosition.magnitude > maxIconDistance)
            {
                if (!clampEnemiesToEdge)
                {
                    continue;
                }

                uiPosition = uiPosition.normalized * maxIconDistance;
            }

            RectTransform icon = GetEnemyIcon(visibleCount);
            icon.anchoredPosition = uiPosition;
            icon.gameObject.SetActive(true);
            visibleCount++;
        }

        for (int i = visibleCount; i < enemyIcons.Count; i++)
        {
            enemyIcons[i].gameObject.SetActive(false);
        }
    }

    private RectTransform GetEnemyIcon(int index)
    {
        while (enemyIcons.Count <= index)
        {
            Image image = CreateImage("EnemyIcon", enemyIconRoot, circleSprite, enemyIconColor);
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = Vector2.one * enemyIconSize;
            enemyIcons.Add(image.rectTransform);
        }

        return enemyIcons[index];
    }

    private void CreateMinimapCamera()
    {
        int renderResolution = textureResolution * Mathf.Max(1, supersampling);
        renderTexture = new RenderTexture(renderResolution, renderResolution, 16)
        {
            name = "MinimapRenderTexture",
            wrapMode = TextureWrapMode.Clamp,
            // 高解像度で描いた結果を縮小表示するとき、ミップマップで平均化された色を使わせる。
            // ミップマップなしだと縮小時に間引きサンプリングになり、スーパーサンプリングの効果が出ない。
            useMipMap = supersampling > 1,
            autoGenerateMips = supersampling > 1,
            filterMode = supersampling > 1 ? FilterMode.Trilinear : FilterMode.Bilinear
        };
        renderTexture.Create();

        GameObject cameraObject = new GameObject("MinimapCamera");
        cameraObject.transform.SetParent(transform, false);

        minimapCamera = cameraObject.AddComponent<Camera>();
        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = mapRadiusMeters;
        minimapCamera.nearClipPlane = 0.3f;
        // プレイヤーより低い位置（坂や地下）も映るよう、高さに少し余裕を持たせる。
        minimapCamera.farClipPlane = cameraHeight + 50f;
        minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = backgroundColor;
        minimapCamera.cullingMask = cullingMask;
        minimapCamera.targetTexture = renderTexture;
        minimapCamera.allowHDR = false;
        minimapCamera.allowMSAA = false;

        UniversalAdditionalCameraData cameraData = minimapCamera.GetUniversalAdditionalCameraData();
        cameraData.renderShadows = false;
        cameraData.renderPostProcessing = false;
        // DeferredFogはVolumeスタックからパラメータを読むため、Volumeを一切参照させないことで
        // 上空からの俯瞰が霧で塗りつぶされるのを防ぐ。
        cameraData.volumeLayerMask = 0;
    }

    private void CreateUI()
    {
        canvasObject = new GameObject("MinimapCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;

        // 右上に固定するルート。
        GameObject rootObject = new GameObject("Minimap", typeof(RectTransform));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(canvasObject.transform, false);
        root.anchorMin = Vector2.one;
        root.anchorMax = Vector2.one;
        root.pivot = Vector2.one;
        root.anchoredPosition = new Vector2(-screenMargin, -screenMargin);
        root.sizeDelta = Vector2.one * mapDiameter;

        Image frame = CreateImage("Frame", root, circleSprite, frameColor);
        Stretch(frame.rectTransform, 0f);

        float innerDiameter = mapDiameter - frameThickness * 2f;
        innerRadiusPixels = innerDiameter * 0.5f;

        // 円形スプライトの形でステンシルマスクをかけ、四角いRenderTextureと敵アイコンを円に切り抜く。
        Image maskImage = CreateImage("Mask", root, circleSprite, Color.white);
        Stretch(maskImage.rectTransform, frameThickness);
        Mask mask = maskImage.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // 地図画像と敵アイコンをまとめて回転させる親。
        GameObject rotatorObject = new GameObject("MapRotator", typeof(RectTransform));
        mapRotator = rotatorObject.GetComponent<RectTransform>();
        mapRotator.SetParent(maskImage.rectTransform, false);
        Stretch(mapRotator, 0f);

        GameObject mapImageObject = new GameObject("MapImage", typeof(RectTransform));
        mapImage = mapImageObject.AddComponent<RawImage>();
        mapImage.rectTransform.SetParent(mapRotator, false);
        mapImage.texture = renderTexture;
        mapImage.raycastTarget = false;
        Stretch(mapImage.rectTransform, 0f);

        GameObject enemyRootObject = new GameObject("EnemyIcons", typeof(RectTransform));
        enemyIconRoot = enemyRootObject.GetComponent<RectTransform>();
        enemyIconRoot.SetParent(mapRotator, false);
        Stretch(enemyIconRoot, 0f);

        Image playerImage = CreateImage("PlayerIcon", root, arrowSprite, playerIconColor);
        playerIcon = playerImage.rectTransform;
        playerIcon.anchorMin = playerIcon.anchorMax = new Vector2(0.5f, 0.5f);
        playerIcon.anchoredPosition = Vector2.zero;
        playerIcon.sizeDelta = Vector2.one * playerIconSize;

        frame.raycastTarget = false;
        maskImage.raycastTarget = false;
        playerImage.raycastTarget = false;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform));
        Image image = imageObject.AddComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.sprite = sprite;
        image.color = color;
        return image;
    }

    private static void Stretch(RectTransform rectTransform, float inset)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.one * inset;
        rectTransform.offsetMax = Vector2.one * -inset;
    }

    // 画像アセット（＝.metaが必要）を増やさないよう、円と矢印のスプライトをコードで描く。
    private void CreateSprites()
    {
        const int circleSize = 128;
        circleTexture = new Texture2D(circleSize, circleSize, TextureFormat.RGBA32, false)
        {
            name = "MinimapCircle",
            wrapMode = TextureWrapMode.Clamp
        };
        float radius = circleSize * 0.5f;
        Color[] circlePixels = new Color[circleSize * circleSize];
        for (int y = 0; y < circleSize; y++)
        {
            for (int x = 0; x < circleSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                // 縁の1pxをなめらかにしてジャギーを抑える。
                float alpha = Mathf.Clamp01(radius - distance);
                circlePixels[y * circleSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        circleTexture.SetPixels(circlePixels);
        circleTexture.Apply();
        circleSprite = Sprite.Create(circleTexture, new Rect(0, 0, circleSize, circleSize), new Vector2(0.5f, 0.5f));

        // 上向きの矢印（先端が上の三角形の底辺を少しえぐった形）。
        const int arrowSize = 64;
        arrowTexture = new Texture2D(arrowSize, arrowSize, TextureFormat.RGBA32, false)
        {
            name = "MinimapArrow",
            wrapMode = TextureWrapMode.Clamp
        };
        Vector2 tip = new Vector2(arrowSize * 0.5f, arrowSize * 0.95f);
        Vector2 left = new Vector2(arrowSize * 0.1f, arrowSize * 0.05f);
        Vector2 notch = new Vector2(arrowSize * 0.5f, arrowSize * 0.3f);
        Vector2 right = new Vector2(arrowSize * 0.9f, arrowSize * 0.05f);
        Color[] arrowPixels = new Color[arrowSize * arrowSize];
        for (int y = 0; y < arrowSize; y++)
        {
            for (int x = 0; x < arrowSize; x++)
            {
                Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = IsInsideTriangle(point, tip, left, notch) || IsInsideTriangle(point, tip, notch, right);
                arrowPixels[y * arrowSize + x] = inside ? Color.white : Color.clear;
            }
        }
        arrowTexture.SetPixels(arrowPixels);
        arrowTexture.Apply();
        arrowSprite = Sprite.Create(arrowTexture, new Rect(0, 0, arrowSize, arrowSize), new Vector2(0.5f, 0.5f));
    }

    private static bool IsInsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b);
        float d2 = Cross(p, b, c);
        float d3 = Cross(p, c, a);
        bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNegative && hasPositive);
    }

    private static float Cross(Vector2 p, Vector2 a, Vector2 b)
    {
        return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
