using UnityEngine;

[CreateAssetMenu(menuName = "BillGenerate/Building Rarity Settings")]
public class BuildingRaritySettings : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public GameObject prefab;

        [Tooltip("このワールド距離以上で出現候補になる。")]
        public float minDistance = 0f;

        [Min(0.01f)]
        public float weight = 1f;
    }

    public Entry[] entries;

    // distance以下のminDistanceを持つentriesから重み付き抽選する。該当なしならfallbackを返す。
    public GameObject Pick(float distance, GameObject fallback)
    {
        if (entries == null || entries.Length == 0)
        {
            return fallback;
        }

        float totalWeight = 0f;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry.prefab == null || entry.minDistance > distance)
            {
                continue;
            }

            totalWeight += entry.weight;
        }

        if (totalWeight <= 0f)
        {
            return fallback;
        }

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry.prefab == null || entry.minDistance > distance)
            {
                continue;
            }

            cumulative += entry.weight;
            if (roll <= cumulative)
            {
                return entry.prefab;
            }
        }

        return fallback;
    }
}
