using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerDamageEffect : MonoBehaviour
{
    private const string DefaultDamageImagePrefabPath = "Assets/PlayerDamageEffect/Image.prefab";

    [Header("揺らすカメラとダメージUI")]
    [JpLabel("揺らすカメラ")]
    public GameObject CAMERA; // メインカメラをアタッチ
    [JpLabel("赤画面UI (Image)")]
    public GameObject aka;    // 赤い半透明のUI Imageをアタッチ（Prefabのままでも空欄でも自動配置）

    [Header("揺れの設定")]
    [Tooltip("ダメージを受けてから揺れが止まるまでの時間（秒）")]
    [Min(0.01f)]
    [JpLabel("揺れの時間（秒）")]
    public float shakeDuration = 0.5f;

    [Tooltip("カメラが左右に揺れる最大幅（大きいほど大きく揺れる）")]
    [Min(0f)]
    [JpLabel("揺れの量（幅）")]
    public float shakeAmount = 0.2f;

    [Tooltip("カメラが左右に往復する速さ")]
    [Min(0.01f)]
    [JpLabel("揺れの速さ")]
    public float shakeSpeed = 6f;

    private int flag = 0;
    private float shakeElapsed = 0f;
    private float shakeOffsetX = 0f;
    private int direction = 1;
    private Vector3 lastAppliedWorldOffset = Vector3.zero;
    private PlayerMove playerMove;
    private PlayerHealth playerHealth;

    private void Reset()
    {
        EnsureDefaultPrefabInEditor();
    }

    private void OnValidate()
    {
        EnsureDefaultPrefabInEditor();
    }

    void Awake()
    {
        playerMove = GetComponent<PlayerMove>();
        playerHealth = GetComponent<PlayerHealth>();
        EnsureCameraAssigned();
        EnsureDamageUiReady();
    }

    void OnEnable()
    {
        if (playerHealth == null)
            playerHealth = GetComponent<PlayerHealth>();

        if (playerHealth != null)
        {
            playerHealth.Damaged -= HandlePlayerDamaged;
            playerHealth.Damaged += HandlePlayerDamaged;
        }
    }

    void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.Damaged -= HandlePlayerDamaged;

        StopShake();
    }

    private void EnsureDefaultPrefabInEditor()
    {
#if UNITY_EDITOR
        if (aka == null)
        {
            aka = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultDamageImagePrefabPath);
        }
#endif
    }

    private void EnsureCameraAssigned()
    {
        if (CAMERA != null)
            return;

        if (playerMove == null)
            playerMove = GetComponent<PlayerMove>();

        if (playerMove != null && playerMove.mainCamera != null)
        {
            CAMERA = playerMove.mainCamera.gameObject;
            return;
        }

        if (Camera.main != null)
        {
            CAMERA = Camera.main.gameObject;
        }
    }

    /// <summary>
    /// aka が未設定、または Project 内の Prefab アセット直接参照になっている場合でも、
    /// シーン内の Canvas 配下に実体化（Instantiate）して画面全体に赤エフェクトが表示されるようにする。
    /// </summary>
    private void EnsureDamageUiReady()
    {
        EnsureDefaultPrefabInEditor();

        Canvas canvas = FindTargetCanvas();

        if (aka != null)
        {
            // ProjectウィンドウのPrefab（シーン上に実体がないアセット）が割り当てられている場合はCanvas配下に生成する
            if (!aka.scene.IsValid())
            {
                GameObject instance = Instantiate(aka, canvas.transform, false);
                instance.name = "DamageRedOverlay";
                ConfigureOverlayRectTransform(instance);
                instance.SetActive(false);
                aka = instance;
                return;
            }

            // シーン上のオブジェクトだが Canvas の子になっていない場合は Canvas 配下へ移動する
            if (aka.GetComponentInParent<Canvas>() == null)
            {
                aka.transform.SetParent(canvas.transform, false);
                ConfigureOverlayRectTransform(aka);
            }
            return;
        }

        // aka が完全に未設定の場合でもフォールバックとして赤い半透明Imageを生成する
        GameObject fallbackOverlay = new GameObject("DamageRedOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fallbackOverlay.transform.SetParent(canvas.transform, false);
        Image img = fallbackOverlay.GetComponent<Image>();
        img.color = new Color(1f, 0f, 0f, 0.29411766f);
        img.raycastTarget = false;
        ConfigureOverlayRectTransform(fallbackOverlay);
        fallbackOverlay.SetActive(false);
        aka = fallbackOverlay;
    }

    private static Canvas FindTargetCanvas()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Canvas c in canvases)
        {
            if (c != null && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return c;
            }
        }

        if (canvases.Length > 0 && canvases[0] != null)
        {
            return canvases[0];
        }

        GameObject canvasObj = new GameObject("DamageEffectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas newCanvas = canvasObj.GetComponent<Canvas>();
        newCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        return newCanvas;
    }

    private static void ConfigureOverlayRectTransform(GameObject uiObject)
    {
        if (uiObject.TryGetComponent<RectTransform>(out var rect))
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        if (uiObject.TryGetComponent<Image>(out var image))
        {
            image.raycastTarget = false;
        }
    }

    private void HandlePlayerDamaged(float damageAmount)
    {
        StartShake();
    }

    // 3D用のトリガー衝突判定
    void OnTriggerEnter(Collider other)
    {
        // 敵の索敵範囲（SearchZone）に入っただけではダメージ揺れを開始しない
        if (other != null && other.name == "SearchZone")
            return;

        // 自分自身や自分の子オブジェクトのコライダーは無視
        if (other != null && other.transform.IsChildOf(transform))
            return;

        StartShake();
    }

    public void StartShake()
    {
        EnsureDamageUiReady();

        shakeElapsed = 0f;
        if (aka != null && !aka.activeSelf)
        {
            aka.SetActive(true); // 画面を赤くする
        }

        if (flag == 0)
        {
            flag = 1;
            shakeOffsetX = 0f;
            direction = 1;
        }
    }

    private void StopShake()
    {
        if (CAMERA != null && !IsCameraControlledByPlayerMove())
        {
            CAMERA.transform.position -= lastAppliedWorldOffset;
        }

        lastAppliedWorldOffset = Vector3.zero;
        shakeOffsetX = 0f;
        shakeElapsed = 0f;
        flag = 0;

        if (aka != null && aka.scene.IsValid())
            aka.SetActive(false); // 赤い画面を消す
    }

    private bool IsCameraControlledByPlayerMove()
    {
        return playerMove != null
            && playerMove.isActiveAndEnabled
            && playerMove.mainCamera != null
            && playerMove.mainCamera.gameObject == CAMERA;
    }

    void LateUpdate()
    {
        if (flag == 0)
            return;

        EnsureCameraAssigned();
        if (CAMERA == null)
        {
            StopShake();
            return;
        }

        // PlayerMove がカメラ座標を毎フレーム更新していない場合は、前フレームの揺れオフセットを一旦戻す
        if (!IsCameraControlledByPlayerMove())
        {
            CAMERA.transform.position -= lastAppliedWorldOffset;
        }
        lastAppliedWorldOffset = Vector3.zero;

        shakeElapsed += Time.deltaTime;
        if (shakeElapsed >= shakeDuration)
        {
            StopShake();
            return;
        }

        // カメラの基準位置からの相対オフセットで左右に揺らす（ワールドX座標に依存しない）
        if (shakeAmount <= 0f)
            return;

        shakeOffsetX += direction * shakeSpeed * Time.deltaTime;
        if (direction > 0 && shakeOffsetX >= shakeAmount)
        {
            shakeOffsetX = shakeAmount;
            direction = -1;
        }
        else if (direction < 0 && shakeOffsetX <= -shakeAmount)
        {
            shakeOffsetX = -shakeAmount;
            direction = 1;
        }

        lastAppliedWorldOffset = CAMERA.transform.right * shakeOffsetX;
        CAMERA.transform.position += lastAppliedWorldOffset;
    }
}
