using System.Collections.Generic;
using UnityEngine;

// 敵の種類（プレハブ）ごとに非アクティブな個体を保持するプール。
// EnemySpawnManagerから使われるヘルパークラスで、MonoBehaviourではない。
public class EnemyPool
{
    private readonly Dictionary<GameObject, Stack<GameObject>> pooledByPrefab = new Dictionary<GameObject, Stack<GameObject>>();
    private readonly Transform parent;

    public EnemyPool(Transform parent)
    {
        this.parent = parent;
    }

    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (pooledByPrefab.TryGetValue(prefab, out Stack<GameObject> stack) && stack.Count > 0)
        {
            GameObject pooledInstance = stack.Pop();
            pooledInstance.transform.SetPositionAndRotation(position, rotation);
            pooledInstance.SetActive(true);
            return pooledInstance;
        }

        return Object.Instantiate(prefab, position, rotation, parent);
    }

    // keepInPool: 呼び出し側（EnemySpawnManager）が「このプレハブは現在のフェーズでまだ出現し得るか」を
    // 判定して渡す。falseの場合は保持分に戻さず即Destroyし、フェーズ切り替え後に
    // 使われない在庫が時間差で溜まり直す（リバウンドする）ことを防ぐ。
    public void Release(GameObject prefab, GameObject instance, bool keepInPool)
    {
        instance.SetActive(false);

        if (!keepInPool)
        {
            Object.Destroy(instance);
            return;
        }

        if (!pooledByPrefab.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>();
            pooledByPrefab[prefab] = stack;
        }

        stack.Push(instance);
    }

    // prefabの非アクティブな保持分を即時Destroyする。
    // アクティブな個体はこのプールの管理対象外（非アクティブなものだけを保持している）ため影響しない。
    public void ClearPoolFor(GameObject prefab)
    {
        if (!pooledByPrefab.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            return;
        }

        int clearedCount = stack.Count;

        while (stack.Count > 0)
        {
            GameObject pooledInstance = stack.Pop();
            if (pooledInstance != null)
            {
                Object.Destroy(pooledInstance);
            }
        }

        pooledByPrefab.Remove(prefab);

        if (clearedCount > 0)
        {
            Debug.Log($"EnemyPool: {prefab.name}の非表示在庫を{clearedCount}体破棄しました（フェーズ切り替えに伴う在庫クリア）");
        }
    }
}
