using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspectorで登録したGameObjectのリストから
/// ランダムに1つだけアクティブにし、残りはすべて非アクティブにするスクリプトです。
///
/// 処理はStart()で1回だけ行います。
/// このGameObjectを非アクティブ→アクティブにしても、もう一度は実行されません。
/// </summary>
public class DeactivateOneOnEnable : MonoBehaviour
{
    // =========================================================
    // Inspector設定
    // =========================================================

    [Header("対象設定")]
    [Tooltip("この中からランダムに1つだけアクティブにし、残りは非アクティブにします")]
    [SerializeField]
    private List<GameObject> targetObjects = new List<GameObject>();


    // =========================================================
    // 最初に1回だけ行う処理
    // =========================================================

    private void Start()
    {
        // -------------------------
        // 選べる候補を集める
        // -------------------------

        // 未設定（None）の欄は除く
        List<GameObject> candidates = targetObjects.FindAll(
            x => x != null
        );


        // 候補がない場合は何もしない
        if (candidates.Count == 0)
        {
            Debug.LogWarning(
                "DeactivateOneOnEnable：対象のGameObjectが設定されていません。"
            );

            return;
        }


        // -------------------------
        // ランダムに1つだけアクティブにする
        // -------------------------

        int index = Random.Range(0, candidates.Count);

        for (int i = 0; i < candidates.Count; i++)
        {
            // 選ばれたものだけtrue、それ以外はfalse
            candidates[i].SetActive(i == index);
        }
    }
}
