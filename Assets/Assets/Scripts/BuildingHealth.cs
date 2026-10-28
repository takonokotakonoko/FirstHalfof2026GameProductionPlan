using UnityEngine;

public enum BuildingSize
{
    Small,
    Medium,
    Large
}

public class BuildingHealth : MonoBehaviour, IDamage
{
    /// <summary>
    /// このスクリプトは建物のPrefabにアタッチする。
    /// 建物の体力、破壊時のSE、破壊時のポイント処理を行う。
    /// </summary>

    [Header("建物のサイズ")]
    [Tooltip("破壊したときに出てくる強化の種類がサイズで変わる（小：体力・移動・通常攻撃 / 中：すべて / 大：経験値・敵出現・スキル）")]
    [SerializeField] private BuildingSize buildingSize = BuildingSize.Small;

    [Header("建物の体力")]
    [SerializeField] private float maxBuildingHealth = 60f;
    private float buildingHealth;

    [Header("報酬ポイント")]
    [SerializeField] public int rewardPoint = 1;

    private PointManager pointManager;

    private bool destroyed = false;

    public BuildingSize Size => buildingSize;

    private void Awake()
    {
        gameObject.tag = "Building";

        if (pointManager == null)
        {
            pointManager = FindFirstObjectByType<PointManager>();
        }
    }

    private void Start()
    {
        buildingHealth = maxBuildingHealth;
    }

    public void TakeDamage(float amount)
    {
        if (destroyed)
            return;

        buildingHealth -= amount;

        Debug.Log(
            $"建物ダメージ: {amount} / 残りHP: {buildingHealth}"
        );

        if (buildingHealth <= 0)
        {
            destroyed = true;

            // ポイント加算
            if (pointManager != null)
            {
                pointManager.AddPoint(rewardPoint, buildingSize);
            }

            // 建物を崩壊させる
            BuildingCollapse collapse =
                GetComponent<BuildingCollapse>();

            if (collapse != null)
            {
                collapse.Collapse();
            }
            else
            {
                Debug.LogWarning(
                    "BuildingCollapseが見つかりません。"
                );
            }
        }
    }

    public void PlayAnimation()
    {
        // 現在はBuildingCollapseが崩壊処理を担当する
    }
}
