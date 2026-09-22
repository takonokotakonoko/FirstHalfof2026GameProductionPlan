using UnityEngine;

// EnemyPhaseSettings（GenerateEnemySystem/Script/EnemyPhaseSettings.cs）と同じ考え方の
// フェーズ制。距離帯（フェーズ）ごとに使う建物群を明示的に区切り、
// GetActivePhaseで「該当する最も近いフェーズ」だけを使う（累積ではない）。
[CreateAssetMenu(menuName = "BillGenerate/Building Rarity Settings")]
public class BuildingRaritySettings : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [JpLabel("プレファブ")]
        public GameObject prefab;

        [Min(0.01f)]
        [JpLabel("重み")]
        public float weight = 1f;
    }

    [System.Serializable]
    public class Phase
    {
        [Tooltip("Inspector表示用のラベル。判定には使用しない。")]
        [JpLabel("フェーズ名")]
        public string phaseName;

        [Tooltip("このフェーズが有効になるワールド距離（distanceOriginWorldPosition基準）。")]
        [JpLabel("開始距離")]
        public float startDistance;

        [JpLabel("大サイズ群（3x3）")]
        public Entry[] largeEntries;

        [JpLabel("中サイズ群（2x2）")]
        public Entry[] mediumEntries;

        [JpLabel("小サイズ群（1x1）")]
        public Entry[] smallEntries;
    }

    [JpLabel("フェーズ一覧")]
    public Phase[] phases;

    // distance以下のstartDistanceを持つフェーズのうち、最も開始が遅い（＝最も近い）ものを返す。該当なしはnull。
    public Phase GetActivePhase(float distance)
    {
        if (phases == null || phases.Length == 0)
        {
            return null;
        }

        Phase active = null;
        for (int i = 0; i < phases.Length; i++)
        {
            Phase phase = phases[i];
            if (phase == null || phase.startDistance > distance)
            {
                continue;
            }

            if (active == null || phase.startDistance > active.startDistance)
            {
                active = phase;
            }
        }

        return active;
    }

    // 指定フェーズ内の、sizeCells（1=小,2=中,3=大）に対応する群から重み付き抽選する。該当なしはfallback。
    public GameObject Pick(Phase phase, int sizeCells, GameObject fallback)
    {
        if (phase == null)
        {
            return fallback;
        }

        Entry[] entries = sizeCells switch
        {
            3 => phase.largeEntries,
            2 => phase.mediumEntries,
            1 => phase.smallEntries,
            _ => null,
        };

        if (entries == null || entries.Length == 0)
        {
            return fallback;
        }

        float totalWeight = 0f;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].prefab == null)
            {
                continue;
            }

            totalWeight += entries[i].weight;
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
            if (entry.prefab == null)
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
