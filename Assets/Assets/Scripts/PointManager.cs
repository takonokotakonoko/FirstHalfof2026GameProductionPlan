using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class PointManager : MonoBehaviour
{
    [Serializable]
    private class BuffSettings
    {
        [InspectorName("有効")]
        [Tooltip("このバフを候補に出すかどうか")]
        public bool enabled = true;

        [InspectorName("出現開始時間（秒）")]
        [Tooltip("ゲーム開始からこの秒数が経過すると候補に出ます")]
        [Min(0f)] public float unlockAfterSeconds;

        [InspectorName("1回の増減量")]
        [Tooltip("1回選択したときの増減量。割合は 0.1 = 10% です")]
        public float effectAmount;

        [InspectorName("最小値")]
        [Tooltip("対象ステータスの最小値、または減少バフの下限")]
        public float minimumValue;

        [InspectorName("最大値")]
        [Tooltip("対象ステータスの最大値、または増加バフの上限")]
        public float maximumValue;
    }

    private sealed class BuffOption
    {
        public string Title;
        public string Description;
        public Func<bool> IsAvailable;
        public Action Apply;
    }

    [Header("ポイント関連")]
    [SerializeField, Min(1)] private int quotaPoint = 2;
    private int destroyPoint;

    [Header("最大体力増加")]
    [SerializeField] private BuffSettings maxHealthBuff = new BuffSettings
    {
        effectAmount = 20f,
        minimumValue = 1f,
        maximumValue = 9999f
    };

    [Header("移動速度上昇")]
    [SerializeField] private BuffSettings moveSpeedBuff = new BuffSettings
    {
        effectAmount = 0.1f,
        minimumValue = 0.1f,
        maximumValue = 4f
    };

    [Header("通常攻撃力上昇")]
    [SerializeField] private BuffSettings normalDamageBuff = new BuffSettings
    {
        effectAmount = 0.1f,
        minimumValue = 0f,
        maximumValue = 100f
    };

    [Header("必要経験値量の減少")]
    [SerializeField] private BuffSettings experienceRequirementBuff = new BuffSettings
    {
        unlockAfterSeconds = 2f,
        effectAmount = 1f,
        minimumValue = 2f,
        maximumValue = 999999f
    };

    [Header("敵の出現頻度の低下")]
    [SerializeField] private BuffSettings slowerEnemySpawnsBuff = new BuffSettings
    {
        unlockAfterSeconds = 2f,
        effectAmount = 1f,
        minimumValue = 1f,
        maximumValue = 60f
    };

    [Header("敵の出現頻度の上昇")]
    [SerializeField] private BuffSettings fasterEnemySpawnsBuff = new BuffSettings
    {
        unlockAfterSeconds = 2f,
        effectAmount = -1f,
        minimumValue = 1f,
        maximumValue = 60f
    };

    [Header("スキル攻撃力上昇")]
    [SerializeField] private BuffSettings skillDamageBuff = new BuffSettings
    {
        unlockAfterSeconds = 300f,
        effectAmount = 0.1f,
        minimumValue = 0f,
        maximumValue = 100f
    };

    [Header("スキルの前隙減少")]
    [SerializeField] private BuffSettings skillStartupBuff = new BuffSettings
    {
        unlockAfterSeconds = 300f,
        effectAmount = 0.1f,
        minimumValue = 0f,
        maximumValue = 0.9f
    };

    [Header("スキルの後隙減少")]
    [SerializeField] private BuffSettings skillRecoveryBuff = new BuffSettings
    {
        unlockAfterSeconds = 300f,
        effectAmount = 0.1f,
        minimumValue = 0f,
        maximumValue = 0.9f
    };

    [Header("スキル攻撃のクールタイム短縮")]
    [SerializeField] private BuffSettings skillCooldownBuff = new BuffSettings
    {
        unlockAfterSeconds = 300f,
        effectAmount = 0.05f,
        minimumValue = 0f,
        maximumValue = 0.5f
    };

    private PlayerHealth playerHealth;
    private PlayerMove playerMove;
    private PlayerAttack playerAttack;
    private LevelManager levelManager;
    private EnemySpawnManager enemySpawnManager;
    private GameObject selectionCanvas;
    private bool isSelecting;
    private float previousTimeScale = 1f;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisibility;

    private void Awake()
    {
        quotaPoint = Mathf.Max(1, quotaPoint);
        CacheGameSystems();
    }

    private void OnValidate()
    {
        quotaPoint = Mathf.Max(1, quotaPoint);
        ValidateBuffSettings(maxHealthBuff, 1f);
        ValidateBuffSettings(moveSpeedBuff, 0.01f);
        ValidateBuffSettings(normalDamageBuff);
        ValidateBuffSettings(experienceRequirementBuff, 1f);
        ValidateBuffSettings(slowerEnemySpawnsBuff, 0.01f);
        ValidateBuffSettings(fasterEnemySpawnsBuff, 0.01f);
        ValidateBuffSettings(skillDamageBuff);
        ValidateBuffSettings(skillStartupBuff);
        ValidateBuffSettings(skillRecoveryBuff);
        ValidateBuffSettings(skillCooldownBuff);
    }

    private static void ValidateBuffSettings(BuffSettings settings, float minimumAllowedValue = 0f)
    {
        if (settings == null)
            return;

        settings.unlockAfterSeconds = Mathf.Max(0f, settings.unlockAfterSeconds);
        settings.minimumValue = Mathf.Max(minimumAllowedValue, settings.minimumValue);
        settings.maximumValue = Mathf.Max(settings.minimumValue, settings.maximumValue);
    }

    private void CacheGameSystems()
    {
        playerHealth = FindAnyObjectByType<PlayerHealth>();
        playerMove = FindAnyObjectByType<PlayerMove>();
        playerAttack = FindAnyObjectByType<PlayerAttack>();
        levelManager = FindAnyObjectByType<LevelManager>();
        enemySpawnManager = FindAnyObjectByType<EnemySpawnManager>();
    }

    public void AddPoint(int point)
    {
        if (point <= 0)
        {
            Debug.LogWarning($"PointManager: 加算ポイントは1以上を指定してください（指定値: {point}）。", this);
            return;
        }

        destroyPoint += point;
        if (!isSelecting && destroyPoint >= quotaPoint)
        {
            ToRewardPhase();
        }
    }

    public void ToRewardPhase()
    {
        if (isSelecting)
            return;

        CacheGameSystems();
        List<BuffOption> availableBuffs = BuildAvailableBuffs();
        if (availableBuffs.Count == 0)
        {
            Debug.LogWarning("PointManager: 選択可能なバフがありません。", this);
            return;
        }

        List<BuffOption> choices = PickThreeRandomBuffs(availableBuffs);
        OpenSelectionScreen(choices);
        if (destroyPoint >= quotaPoint)
            destroyPoint -= quotaPoint;
    }

    private List<BuffOption> BuildAvailableBuffs()
    {
        float elapsedSeconds = Time.timeSinceLevelLoad;

        var buffs = new List<BuffOption>
        {
            new BuffOption
            {
                Title = "最大体力増加",
                Description = $"最大体力と現在体力が +{maxHealthBuff.effectAmount:0.#}",
                IsAvailable = () => IsUnlocked(maxHealthBuff, elapsedSeconds)
                    && playerHealth != null
                    && playerHealth.CanIncreaseMaxHealth(maxHealthBuff.maximumValue),
                Apply = () => playerHealth.AddMaxHealth(
                    maxHealthBuff.effectAmount,
                    maxHealthBuff.minimumValue,
                    maxHealthBuff.maximumValue)
            },
            new BuffOption
            {
                Title = "移動速度上昇",
                Description = $"移動速度 +{moveSpeedBuff.effectAmount * 100f:0.#}%（最大 {moveSpeedBuff.maximumValue:0.##} m/s）",
                IsAvailable = () => IsUnlocked(moveSpeedBuff, elapsedSeconds)
                    && playerMove != null
                    && playerMove.CanIncreaseMoveSpeedUpTo(moveSpeedBuff.maximumValue),
                Apply = () => playerMove.IncreaseMoveSpeed(
                    moveSpeedBuff.effectAmount,
                    moveSpeedBuff.minimumValue,
                    moveSpeedBuff.maximumValue)
            },
            new BuffOption
            {
                Title = "通常攻撃力上昇",
                Description = $"通常攻撃のダメージ +{normalDamageBuff.effectAmount * 100f:0.#}%",
                IsAvailable = () => IsUnlocked(normalDamageBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.CanIncreaseNormalDamage(normalDamageBuff.maximumValue),
                Apply = () => playerAttack.IncreaseNormalDamage(
                    normalDamageBuff.effectAmount,
                    normalDamageBuff.minimumValue,
                    normalDamageBuff.maximumValue)
            },
            new BuffOption
            {
                Title = "必要経験値量の減少",
                Description = $"次のレベルに必要な経験値 -{Mathf.RoundToInt(experienceRequirementBuff.effectAmount)}（最小 {Mathf.RoundToInt(experienceRequirementBuff.minimumValue)}）",
                IsAvailable = () => IsUnlocked(experienceRequirementBuff, elapsedSeconds)
                    && Mathf.RoundToInt(experienceRequirementBuff.effectAmount) > 0
                    && levelManager != null
                    && levelManager.CanReduceExpRequirementTo(Mathf.RoundToInt(experienceRequirementBuff.minimumValue)),
                Apply = () => levelManager.ReduceExpRequirement(
                    Mathf.RoundToInt(experienceRequirementBuff.effectAmount),
                    Mathf.RoundToInt(experienceRequirementBuff.minimumValue),
                    Mathf.RoundToInt(experienceRequirementBuff.maximumValue))
            },
            new BuffOption
            {
                Title = "敵の出現頻度の低下",
                Description = $"敵の出現間隔 +{slowerEnemySpawnsBuff.effectAmount:0.##} 秒（最大 {slowerEnemySpawnsBuff.maximumValue:0.##} 秒）",
                IsAvailable = () => IsUnlocked(slowerEnemySpawnsBuff, elapsedSeconds)
                    && enemySpawnManager != null
                    && enemySpawnManager.CanAdjustSpawnInterval(
                        slowerEnemySpawnsBuff.effectAmount,
                        slowerEnemySpawnsBuff.minimumValue,
                        slowerEnemySpawnsBuff.maximumValue),
                Apply = () => enemySpawnManager.AdjustSpawnInterval(
                    slowerEnemySpawnsBuff.effectAmount,
                    slowerEnemySpawnsBuff.minimumValue,
                    slowerEnemySpawnsBuff.maximumValue)
            },
            new BuffOption
            {
                Title = "敵の出現頻度の上昇",
                Description = $"敵の出現間隔 {fasterEnemySpawnsBuff.effectAmount:0.##} 秒（最小 {fasterEnemySpawnsBuff.minimumValue:0.##} 秒）",
                IsAvailable = () => IsUnlocked(fasterEnemySpawnsBuff, elapsedSeconds, true)
                    && enemySpawnManager != null
                    && enemySpawnManager.CanAdjustSpawnInterval(
                        fasterEnemySpawnsBuff.effectAmount,
                        fasterEnemySpawnsBuff.minimumValue,
                        fasterEnemySpawnsBuff.maximumValue),
                Apply = () => enemySpawnManager.AdjustSpawnInterval(
                    fasterEnemySpawnsBuff.effectAmount,
                    fasterEnemySpawnsBuff.minimumValue,
                    fasterEnemySpawnsBuff.maximumValue)
            },
            new BuffOption
            {
                Title = "スキル攻撃力上昇",
                Description = $"スキル攻撃のダメージ +{skillDamageBuff.effectAmount * 100f:0.#}%",
                IsAvailable = () => IsUnlocked(skillDamageBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.CanIncreaseSkillDamage(skillDamageBuff.maximumValue),
                Apply = () => playerAttack.IncreaseSkillDamage(
                    skillDamageBuff.effectAmount,
                    skillDamageBuff.minimumValue,
                    skillDamageBuff.maximumValue)
            },
            new BuffOption
            {
                Title = "スキルの前隙減少",
                Description = $"スキルの攻撃開始が {skillStartupBuff.effectAmount * 100f:0.#}% 速くなる（最大 {skillStartupBuff.maximumValue * 100f:0.#}%）",
                IsAvailable = () => IsUnlocked(skillStartupBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.CanReduceSkillStartupUpTo(skillStartupBuff.maximumValue),
                Apply = () => playerAttack.ReduceSkillStartup(
                    skillStartupBuff.effectAmount,
                    skillStartupBuff.minimumValue,
                    skillStartupBuff.maximumValue)
            },
            new BuffOption
            {
                Title = "スキルの後隙減少",
                Description = $"スキル後の硬直が {skillRecoveryBuff.effectAmount * 100f:0.#}% 短くなる（最大 {skillRecoveryBuff.maximumValue * 100f:0.#}%）",
                IsAvailable = () => IsUnlocked(skillRecoveryBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.CanReduceSkillRecoveryUpTo(skillRecoveryBuff.maximumValue),
                Apply = () => playerAttack.ReduceSkillRecovery(
                    skillRecoveryBuff.effectAmount,
                    skillRecoveryBuff.minimumValue,
                    skillRecoveryBuff.maximumValue)
            },
            new BuffOption
            {
                Title = "スキル攻撃の CD 短縮",
                Description = $"スキルのクールタイム -{skillCooldownBuff.effectAmount * 100f:0.#}%（最大 {skillCooldownBuff.maximumValue * 100f:0.#}%）",
                IsAvailable = () => IsUnlocked(skillCooldownBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.CanReduceSkillCooldownUpTo(skillCooldownBuff.maximumValue),
                Apply = () => playerAttack.ReduceSkillCooldown(
                    skillCooldownBuff.effectAmount,
                    skillCooldownBuff.minimumValue,
                    skillCooldownBuff.maximumValue)
            }
        };

        buffs.RemoveAll(buff => buff == null || !buff.IsAvailable());
        return buffs;
    }

    private static bool IsUnlocked(BuffSettings settings, float elapsedSeconds, bool negativeAmount = false)
    {
        return settings != null
            && settings.enabled
            && (negativeAmount ? settings.effectAmount < 0f : settings.effectAmount > 0f)
            && elapsedSeconds >= settings.unlockAfterSeconds;
    }

    private List<BuffOption> PickThreeRandomBuffs(List<BuffOption> availableBuffs)
    {
        for (int i = availableBuffs.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            (availableBuffs[i], availableBuffs[swapIndex]) = (availableBuffs[swapIndex], availableBuffs[i]);
        }

        int choiceCount = Mathf.Min(3, availableBuffs.Count);
        return availableBuffs.GetRange(0, choiceCount);
    }

    private void OpenSelectionScreen(List<BuffOption> choices)
    {
        previousTimeScale = Time.timeScale;
        previousCursorLockState = Cursor.lockState;
        previousCursorVisibility = Cursor.visible;
        isSelecting = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureEventSystem();
        selectionCanvas = BuildSelectionCanvas(choices);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        var eventSystemObject = new GameObject("Buff Selection EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        InputSystemUIInputModule inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
        inputModule.AssignDefaultActions();
    }

    private GameObject BuildSelectionCanvas(List<BuffOption> choices)
    {
        var canvasObject = new GameObject("Buff Selection Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image dimmer = CreateImage(canvasObject.transform, "Background", new Color(0f, 0f, 0f, 0.78f));
        StretchToParent(dimmer.rectTransform);

        Image panel = CreateImage(canvasObject.transform, "Panel", new Color(0.055f, 0.075f, 0.13f, 0.98f));
        SetCenteredRect(panel.rectTransform, new Vector2(1120f, 540f), Vector2.zero);

        CreateText(panel.transform, "Header", "強化を選択", 46, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.05f, 0.79f), new Vector2(0.95f, 0.96f), Color.white);
        CreateText(panel.transform, "Subtitle", "建物破壊報酬 - カードを1つ選んでください", 22, FontStyle.Normal,
            TextAnchor.MiddleCenter, new Vector2(0.05f, 0.69f), new Vector2(0.95f, 0.81f), new Color(0.72f, 0.79f, 0.9f));

        const float cardWidth = 300f;
        const float cardHeight = 320f;
        const float cardSpacing = 32f;
        float firstCardX = -(choices.Count * cardWidth + (choices.Count - 1) * cardSpacing) * 0.5f + cardWidth * 0.5f;

        for (int i = 0; i < choices.Count; i++)
        {
            BuffOption choice = choices[i];
            GameObject card = CreateCard(panel.transform, choice, i);
            SetCenteredRect(card.GetComponent<RectTransform>(), new Vector2(cardWidth, cardHeight),
                new Vector2(firstCardX + i * (cardWidth + cardSpacing), -65f));
        }

        return canvasObject;
    }

    private GameObject CreateCard(Transform parent, BuffOption choice, int index)
    {
        Color[] cardColors =
        {
            new Color(0.12f, 0.3f, 0.48f, 1f),
            new Color(0.18f, 0.3f, 0.42f, 1f),
            new Color(0.27f, 0.24f, 0.43f, 1f)
        };

        var card = new GameObject($"Buff Card {index + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(parent, false);
        Image image = card.GetComponent<Image>();
        image.color = cardColors[index % cardColors.Length];
        Button button = card.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f),
            pressedColor = new Color(0.78f, 0.82f, 0.92f, 1f),
            selectedColor = Color.white,
            disabledColor = Color.gray,
            colorMultiplier = 1f,
            fadeDuration = 0.12f
        };
        button.onClick.AddListener(() => ChooseBuff(choice));

        CreateText(card.transform, "Number", $"0{index + 1}", 20, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.95f), new Color(0.69f, 0.87f, 1f));
        CreateText(card.transform, "Title", choice.Title, 27, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.08f, 0.49f), new Vector2(0.92f, 0.82f), Color.white);
        CreateText(card.transform, "Description", choice.Description, 19, FontStyle.Normal, TextAnchor.MiddleCenter,
            new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.48f), new Color(0.82f, 0.88f, 0.96f));
        CreateText(card.transform, "Select", "選択する", 19, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.08f, 0.035f), new Vector2(0.92f, 0.15f), new Color(0.55f, 0.83f, 1f));
        return card;
    }

    private static Image CreateImage(Transform parent, string objectName, Color color)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text CreateText(
        Transform parent,
        string objectName,
        string content,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(8f, 4f);
        rect.offsetMax = new Vector2(-8f, -4f);

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 size, Vector2 anchoredPosition)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
    }

    private void ChooseBuff(BuffOption choice)
    {
        choice.Apply();
        Destroy(selectionCanvas);
        selectionCanvas = null;
        isSelecting = false;
        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisibility;

        if (destroyPoint >= quotaPoint)
            ToRewardPhase();
    }

    private void OnDisable()
    {
        if (!isSelecting)
            return;

        if (selectionCanvas != null)
            Destroy(selectionCanvas);

        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisibility;
        isSelecting = false;
    }
}
