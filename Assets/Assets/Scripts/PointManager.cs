using UnityEngine;

public class PointManager : MonoBehaviour
{
    /// <summary>
    /// PointManager.csは報酬フェーズに入るための条件を満たすかどうかを常に監視する。
    /// </summary>
    /// 
    [Header("ポイント関連")]
    private int destroyPoint = 0;   //  建物を破壊したとき、ここにポイントがたまる
    public int quotaPoint = 2;  //  destroyPointがquotaPoint以上になると、報酬フェーズに突入
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        //  ゲーム中に生成されたBuildingHealth.csがアタッチされたオブジェクトを発見し、その情報(?)を取得する
        BuildingHealth[] buildings = FindObjectsByType<BuildingHealth>(FindObjectsSortMode.None);


    }

    public void AddPoint(int point)
    {
        destroyPoint += point;

        if(destroyPoint >= quotaPoint)
        {
            ToRewardPhase();
            destroyPoint = 0;   //  ポイントの初期化
        }
    }

    public void ToRewardPhase()
    {
        ///  処理内容は未定
        /// 敵の体力が少なくなるとか、攻撃範囲が広がるとかあるかな
        Debug.Log("報酬フェーズに入りました。");
    }
}
