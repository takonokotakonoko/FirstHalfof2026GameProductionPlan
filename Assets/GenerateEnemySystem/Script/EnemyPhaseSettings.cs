using UnityEngine;

[CreateAssetMenu(menuName = "GenerateEnemySystem/Enemy Phase Settings")]
public class EnemyPhaseSettings : ScriptableObject
{
    [System.Serializable]
    public class EnemyTypeEntry
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

        [Tooltip("このフェーズが開始する経過時間（秒）。ゲーム開始からの絶対時間で、前のフェーズからの相対時間ではない。")]
        [JpLabel("開始時刻（秒）")]
        public float startTimeSeconds;

        [Tooltip("このフェーズでの敵の生成間隔（秒）。短いほど速く湧く。")]
        [Min(0.01f)]
        [JpLabel("生成間隔（秒）")]
        public float spawnIntervalSeconds = 2f;

        [Tooltip("このフェーズで生成対象範囲内に同時に存在できる敵の総数の上限。")]
        [Min(0)]
        [JpLabel("同時出現数上限")]
        public int maxActiveEnemies = 20;

        [Tooltip("このフェーズが始まった瞬間、前のフェーズにしか出現しない種類のアクティブな敵を強制的に削除するか。falseの場合は距離超過・確率的な間引きで自然に退場するまで残る。")]
        [JpLabel("開始時に前フェーズの敵を強制削除")]
        public bool clearPreviousPhaseEnemiesOnEnter;

        [JpLabel("敵タイプ一覧")]
        public EnemyTypeEntry[] enemyTypes;
    }

    [JpLabel("フェーズ一覧")]
    public Phase[] phases;

    // 経過時間以下のstartTimeSecondsを持つフェーズのうち、最も開始が遅いものを返す。該当なしはnull。
    public Phase GetActivePhase(float elapsedSeconds)
    {
        if (phases == null || phases.Length == 0)
        {
            return null;
        }

        Phase active = null;
        for (int i = 0; i < phases.Length; i++)
        {
            Phase phase = phases[i];
            if (phase == null || phase.startTimeSeconds > elapsedSeconds)
            {
                continue;
            }

            if (active == null || phase.startTimeSeconds > active.startTimeSeconds)
            {
                active = phase;
            }
        }

        return active;
    }

    // フェーズ内のenemyTypesから重み付き抽選する。該当なしはnull。
    public GameObject PickEnemyPrefab(Phase phase)
    {
        if (phase == null || phase.enemyTypes == null || phase.enemyTypes.Length == 0)
        {
            return null;
        }

        float totalWeight = 0f;
        for (int i = 0; i < phase.enemyTypes.Length; i++)
        {
            EnemyTypeEntry entry = phase.enemyTypes[i];
            if (entry.prefab == null)
            {
                continue;
            }

            totalWeight += entry.weight;
        }

        if (totalWeight <= 0f)
        {
            return null;
        }

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < phase.enemyTypes.Length; i++)
        {
            EnemyTypeEntry entry = phase.enemyTypes[i];
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

        return null;
    }

    // prefabが指定フェーズのenemyTypesに含まれるか。
    public static bool ContainsPrefab(Phase phase, GameObject prefab)
    {
        if (phase == null || phase.enemyTypes == null)
        {
            return false;
        }

        for (int i = 0; i < phase.enemyTypes.Length; i++)
        {
            if (phase.enemyTypes[i].prefab == prefab)
            {
                return true;
            }
        }

        return false;
    }
}
