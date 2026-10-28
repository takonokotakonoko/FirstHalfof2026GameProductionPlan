using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class PointManager : MonoBehaviour
{
    // 強化が出てくる建物のサイズ（複数選択可）
    [Flags]
    public enum BuildingSizeMask
    {
        None = 0,
        Small = 1,  // 1 << BuildingSize.Small
        Medium = 2, // 1 << BuildingSize.Medium
        Large = 4   // 1 << BuildingSize.Large
    }

    public enum TimeRewardType
    {
        [InspectorName("スキル攻撃解放")] UnlockSkill,
        [InspectorName("全能力n倍")] MultiplyAllStats
    }

    [Serializable]
    private class TimeReward
    {
        [InspectorName("経過時間（秒）")]
        [Tooltip("ゲーム開始からこの秒数が経過したら、報酬の画面を表示して受け取らせます")]
        [Min(0f)] public float afterSeconds;

        [InspectorName("報酬の種類")]
        public TimeRewardType type;

        [InspectorName("倍率")]
        [Tooltip("「全能力n倍」の n。攻撃力・最大体力・移動速度に掛けます")]
        [Min(0.01f)] public float multiplier = 1.5f;

        [InspectorName("アイコン")]
        public Texture2D icon;
    }

    [Serializable]
    private class BuffSettings
    {
        [InspectorName("有効")]
        [Tooltip("このバフを候補に出すかどうか")]
        public bool enabled = true;

        [InspectorName("出てくる建物")]
        [Tooltip("このサイズの建物を壊したときだけ候補に出ます")]
        public BuildingSizeMask sources = BuildingSizeMask.Small | BuildingSizeMask.Medium | BuildingSizeMask.Large;

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

        [InspectorName("アイコン")]
        [Tooltip("選択カードに表示するアイコン。未設定の場合はアイコンなしで表示します")]
        public Texture2D icon;
    }

    private sealed class BuffOption
    {
        public string Title;
        public string Description;
        public Texture2D Icon;
        public Func<bool> IsAvailable;
        public Action Apply;
    }

    [Header("ポイント関連")]
    [SerializeField, Min(1)] private int quotaPoint = 2;
    private int destroyPoint;

    [Header("UIフォント")]
    [Tooltip("選択画面全体で使うフォント。未設定の場合は Unity 標準フォント（LegacyRuntime）を使います")]
    [SerializeField] private Font uiFont;

    [Tooltip("見出し・カードタイトルなど太字部分に使うフォント。未設定の場合は上の UIフォント を使います")]
    [SerializeField] private Font headingFont;

    [Header("効果音（SoundPlayerに登録した名前）")]
    [Tooltip("強化の選択画面を開いたときに鳴らす音")]
    [SerializeField] private string selectionOpenSoundName = "rewardOpen";

    [Tooltip("強化を選んだときに鳴らす音")]
    [SerializeField] private string buffSelectSoundName = "decision";

    [Header("時間経過による報酬")]
    [Tooltip("経過時間の早い順に登録してください。時間になると画面を表示して受け取らせます")]
    [SerializeField] private List<TimeReward> timeRewards = new List<TimeReward>
    {
        new TimeReward { afterSeconds = 300f, type = TimeRewardType.UnlockSkill },
        new TimeReward { afterSeconds = 600f, type = TimeRewardType.MultiplyAllStats, multiplier = 1.5f }
    };

    [Tooltip("「全能力n倍」で移動速度を上げるときの上限（m/s）")]
    [SerializeField, Min(0.1f)] private float allStatsMaxMoveSpeed = 4f;

    [Header("最大体力増加")]
    [SerializeField] private BuffSettings maxHealthBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Small | BuildingSizeMask.Medium,
        effectAmount = 20f,
        minimumValue = 1f,
        maximumValue = 9999f
    };

    [Header("移動速度上昇")]
    [SerializeField] private BuffSettings moveSpeedBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Small | BuildingSizeMask.Medium,
        effectAmount = 0.1f,
        minimumValue = 0.1f,
        maximumValue = 4f
    };

    [Header("通常攻撃の参照攻撃力上昇")]
    [SerializeField] private BuffSettings normalDamageBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Small | BuildingSizeMask.Medium,
        effectAmount = 0.05f,
        minimumValue = 0f,
        maximumValue = 100f
    };

    [Header("必要経験値量の減少")]
    [SerializeField] private BuffSettings experienceRequirementBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Medium | BuildingSizeMask.Large,
        effectAmount = 1f,
        minimumValue = 2f,
        maximumValue = 999999f
    };

    [Header("敵の出現頻度の低下")]
    [SerializeField] private BuffSettings slowerEnemySpawnsBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Medium | BuildingSizeMask.Large,
        effectAmount = 1f,
        minimumValue = 1f,
        maximumValue = 60f
    };

    [Header("敵の出現頻度の上昇")]
    [SerializeField] private BuffSettings fasterEnemySpawnsBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Medium | BuildingSizeMask.Large,
        effectAmount = -1f,
        minimumValue = 1f,
        maximumValue = 60f
    };

    [Header("スキルの参照攻撃力上昇")]
    [SerializeField] private BuffSettings skillDamageBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Medium | BuildingSizeMask.Large,
        unlockAfterSeconds = 300f,
        effectAmount = 0.1f,
        minimumValue = 0f,
        maximumValue = 100f
    };

    [Header("スキルの前隙減少")]
    [SerializeField] private BuffSettings skillStartupBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Medium | BuildingSizeMask.Large,
        unlockAfterSeconds = 300f,
        effectAmount = 0.1f,
        minimumValue = 0f,
        maximumValue = 0.9f
    };

    [Header("スキルの後隙減少")]
    [SerializeField] private BuffSettings skillRecoveryBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Medium | BuildingSizeMask.Large,
        unlockAfterSeconds = 300f,
        effectAmount = 0.1f,
        minimumValue = 0f,
        maximumValue = 0.9f
    };

    [Header("スキル攻撃のクールタイム短縮")]
    [SerializeField] private BuffSettings skillCooldownBuff = new BuffSettings
    {
        sources = BuildingSizeMask.Medium | BuildingSizeMask.Large,
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
    private Font builtinFont;

    // 最後に壊した建物のサイズ（強化の候補の絞り込みに使う）
    private BuildingSizeMask lastDestroyedSize = BuildingSizeMask.Small | BuildingSizeMask.Medium | BuildingSizeMask.Large;

    // 次に受け取る時間経過報酬の番号
    private int nextTimeRewardIndex;

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

    private void Update()
    {
        if (isSelecting || timeRewards == null || nextTimeRewardIndex >= timeRewards.Count)
            return;

        TimeReward reward = timeRewards[nextTimeRewardIndex];
        if (reward == null)
        {
            nextTimeRewardIndex++;
            return;
        }

        if (Time.timeSinceLevelLoad < reward.afterSeconds)
            return;

        nextTimeRewardIndex++;
        OpenTimeReward(reward);
    }

    private void OpenTimeReward(TimeReward reward)
    {
        CacheGameSystems();

        BuffOption option = reward.type == TimeRewardType.UnlockSkill
            ? new BuffOption
            {
                Title = "スキル攻撃解放",
                Description = "スキル攻撃（右クリック）が使えるようになる",
                Icon = reward.icon,
                Apply = () =>
                {
                    if (playerAttack != null)
                        playerAttack.UnlockSkill();
                }
            }
            : new BuffOption
            {
                Title = $"全能力{reward.multiplier:0.##}倍",
                Description = $"攻撃力・最大体力・移動速度が {reward.multiplier:0.##}倍（移動速度は最大 {allStatsMaxMoveSpeed:0.##} m/s）",
                Icon = reward.icon,
                Apply = () =>
                {
                    if (playerHealth != null)
                        playerHealth.MultiplyPower(reward.multiplier);
                    if (playerMove != null)
                        playerMove.MultiplyMoveSpeed(reward.multiplier, allStatsMaxMoveSpeed);
                }
            };

        OpenSelectionScreen(new List<BuffOption> { option }, "時間経過報酬", "時間経過報酬 - カードを選んで受け取ってください");
    }

    public void AddPoint(int point)
    {
        AddPoint(point, BuildingSizeMask.Small | BuildingSizeMask.Medium | BuildingSizeMask.Large);
    }

    public void AddPoint(int point, BuildingSize destroyedSize)
    {
        AddPoint(point, (BuildingSizeMask)(1 << (int)destroyedSize));
    }

    private void AddPoint(int point, BuildingSizeMask destroyedSize)
    {
        if (point <= 0)
        {
            Debug.LogWarning($"PointManager: 加算ポイントは1以上を指定してください（指定値: {point}）。", this);
            return;
        }

        lastDestroyedSize = destroyedSize;
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
        OpenSelectionScreen(choices, "強化を選択", "建物破壊報酬 - カードを1つ選んでください");
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
                Icon = maxHealthBuff.icon,
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
                Icon = moveSpeedBuff.icon,
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
                Title = "通常攻撃の参照攻撃力上昇",
                Description = $"通常攻撃の攻撃力参照率 +{normalDamageBuff.effectAmount * 100f:0.#}%",
                Icon = normalDamageBuff.icon,
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
                Icon = experienceRequirementBuff.icon,
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
                Icon = slowerEnemySpawnsBuff.icon,
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
                Icon = fasterEnemySpawnsBuff.icon,
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
                Title = "スキルの参照攻撃力上昇",
                Description = $"スキル攻撃の攻撃力参照率 +{skillDamageBuff.effectAmount * 100f:0.#}%",
                Icon = skillDamageBuff.icon,
                IsAvailable = () => IsUnlocked(skillDamageBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.IsSkillUnlocked
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
                Icon = skillStartupBuff.icon,
                IsAvailable = () => IsUnlocked(skillStartupBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.IsSkillUnlocked
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
                Icon = skillRecoveryBuff.icon,
                IsAvailable = () => IsUnlocked(skillRecoveryBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.IsSkillUnlocked
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
                Icon = skillCooldownBuff.icon,
                IsAvailable = () => IsUnlocked(skillCooldownBuff, elapsedSeconds)
                    && playerAttack != null
                    && playerAttack.IsSkillUnlocked
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

    private bool IsUnlocked(BuffSettings settings, float elapsedSeconds, bool negativeAmount = false)
    {
        return settings != null
            && settings.enabled
            && (settings.sources & lastDestroyedSize) != 0
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

    private void OpenSelectionScreen(List<BuffOption> choices, string header, string subtitle)
    {
        previousTimeScale = Time.timeScale;
        previousCursorLockState = Cursor.lockState;
        previousCursorVisibility = Cursor.visible;
        isSelecting = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureEventSystem();
        selectionCanvas = BuildSelectionCanvas(choices, header, subtitle);
        SoundPlayer.PlayIfExists(selectionOpenSoundName);
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

    private Font GetBodyFont()
    {
        if (uiFont != null)
            return uiFont;

        if (builtinFont == null)
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return builtinFont;
    }

    private Font GetHeadingFont()
    {
        return headingFont != null ? headingFont : GetBodyFont();
    }

    private GameObject BuildSelectionCanvas(List<BuffOption> choices, string header, string subtitle)
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

        CreateText(panel.transform, "Header", header, GetHeadingFont(), 46, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.05f, 0.79f), new Vector2(0.95f, 0.96f), Color.white);
        CreateText(panel.transform, "Subtitle", subtitle, GetBodyFont(), 22, FontStyle.Normal,
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

        Font bodyFont = GetBodyFont();
        Font titleFont = GetHeadingFont();

        // アイコンがある強化だけ、タイトルの上にアイコンを表示し、文字をその分下げる
        bool hasIcon = choice.Icon != null;
        if (hasIcon)
            CreateIcon(card.transform, choice.Icon, new Vector2(0.5f, 0.69f), 80f);

        CreateText(card.transform, "Number", $"0{index + 1}", titleFont, 20, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.95f), new Color(0.69f, 0.87f, 1f));
        CreateText(card.transform, "Title", choice.Title, titleFont, 27, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.08f, hasIcon ? 0.4f : 0.49f), new Vector2(0.92f, hasIcon ? 0.57f : 0.82f), Color.white);
        CreateText(card.transform, "Description", choice.Description, bodyFont, 19, FontStyle.Normal, TextAnchor.MiddleCenter,
            new Vector2(0.1f, 0.15f), new Vector2(0.9f, hasIcon ? 0.41f : 0.48f), new Color(0.82f, 0.88f, 0.96f));
        CreateText(card.transform, "Select", "選択する", titleFont, 19, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.08f, 0.035f), new Vector2(0.92f, 0.15f), new Color(0.55f, 0.83f, 1f));
        return card;
    }

    private static RawImage CreateIcon(Transform parent, Texture2D texture, Vector2 anchor, float maxSize)
    {
        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
        iconObject.transform.SetParent(parent, false);
        RawImage icon = iconObject.GetComponent<RawImage>();
        icon.texture = texture;
        icon.raycastTarget = false;

        // 縦横比を保ったまま maxSize の正方形に収める
        float aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
        Vector2 size = aspect >= 1f
            ? new Vector2(maxSize, maxSize / aspect)
            : new Vector2(maxSize * aspect, maxSize);

        RectTransform rect = icon.rectTransform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        return icon;
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
        Font font,
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
        text.font = font;
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
        SoundPlayer.PlayIfExists(buffSelectSoundName);
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