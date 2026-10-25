using UnityEngine;

public class BuildingHealth : MonoBehaviour, IDamage
{
    /// <summary>
    /// このスクリプトは建物のPrefabにアタッチする。
    /// 建物の体力、破壊時のSE、破壊時のポイント処理を行う。
    /// </summary>

    [Header("建物の体力")]
    [SerializeField] private float maxBuildingHealth = 100f;
    private float buildingHealth;

    [Header("報酬ポイント")]
    [SerializeField] public int rewardPoint = 1;

    [Header("破壊時のSE")]
    public SoundPlayer collapseSound;

    private PointManager pointManager;

    private bool destroyed = false;

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
                pointManager.AddPoint(rewardPoint);
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