using UnityEngine;

public class DontDestroyObject : MonoBehaviour
{
    [SerializeField]
    private string objectID;

    private static readonly System.Collections.Generic.Dictionary<string, DontDestroyObject> instances
        = new System.Collections.Generic.Dictionary<string, DontDestroyObject>();

    private void Awake()
    {
        // IDが未設定ならオブジェクト名を使用
        if (string.IsNullOrEmpty(objectID))
        {
            objectID = gameObject.name;
        }

        // すでに同じIDのオブジェクトが存在する
        if (instances.TryGetValue(objectID, out DontDestroyObject existing))
        {
            // 自分以外なら、新しく生成された方を削除
            if (existing != this)
            {
                Destroy(gameObject);
                return;
            }
        }
        else
        {
            instances.Add(objectID, this);
        }

        // 親オブジェクトごとシーンをまたいで保持
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        // 自分が登録されている場合のみ削除
        if (instances.TryGetValue(objectID, out DontDestroyObject existing))
        {
            if (existing == this)
            {
                instances.Remove(objectID);
            }
        }
    }
}