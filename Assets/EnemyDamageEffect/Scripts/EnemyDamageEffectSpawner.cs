using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 敵がダメージを受けたときに、ダメージ用パーティクル（EnemyDamagedEffect）を発生させる。
/// 敵（EnemyHealth が付いているオブジェクト）にアタッチして使用する。
/// エフェクトは敵の子にせず独立して生成するため、敵が倒されて Destroy されても最後まで再生される。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public class EnemyDamageEffectSpawner : MonoBehaviour
{
    private const string DefaultEffectPrefabPath = "Assets/EnemyDamageEffect/EnemyDamagedEffect.prefab";

    [Header("ダメージ時に発生させるエフェクト")]
    [SerializeField] private GameObject effectPrefab;

    [Header("エフェクトを削除するまでの秒数")]
    [SerializeField] private float lifeTime = 1f;

    [Header("発生位置の補正（コライダー中心からのオフセット）")]
    [SerializeField] private Vector3 spawnOffset = Vector3.zero;

    private EnemyHealth enemyHealth;
    private Collider enemyCollider;

    private void Reset()
    {
        EnsureDefaultEffectPrefab();
    }

    private void OnValidate()
    {
        EnsureDefaultEffectPrefab();
    }

    private void Awake()
    {
        EnsureDefaultEffectPrefab();

        enemyHealth = GetComponent<EnemyHealth>();
        enemyCollider = GetComponent<Collider>();

        if (effectPrefab == null)
            Debug.LogWarning("EnemyDamageEffectSpawner: effectPrefab が設定されていません。", this);
    }

    private void OnEnable()
    {
        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (enemyHealth != null)
        {
            enemyHealth.Damaged -= HandleDamaged;
            enemyHealth.Damaged += HandleDamaged;
        }
    }

    private void OnDisable()
    {
        if (enemyHealth != null)
            enemyHealth.Damaged -= HandleDamaged;
    }

    private void HandleDamaged(float damageAmount)
    {
        if (effectPrefab == null)
            return;

        Quaternion rotation = effectPrefab.transform.rotation;
        GameObject effect = Instantiate(effectPrefab, GetSpawnPosition(), rotation);

        if (effect.TryGetComponent<ParticleSystem>(out var ps) && !ps.isPlaying)
        {
            ps.Play(true);
        }

        Destroy(effect, lifeTime);
    }

    private Vector3 GetSpawnPosition()
    {
        // コライダーがあれば見た目の中心付近から発生させる
        Vector3 basePosition = enemyCollider != null
            ? enemyCollider.bounds.center
            : transform.position;

        return basePosition + spawnOffset;
    }

    private void EnsureDefaultEffectPrefab()
    {
#if UNITY_EDITOR
        if (effectPrefab == null)
        {
            effectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultEffectPrefabPath);
        }
#endif
    }
}
