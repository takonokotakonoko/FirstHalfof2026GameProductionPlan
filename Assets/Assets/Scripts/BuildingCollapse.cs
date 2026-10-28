using UnityEngine;

public class BuildingCollapse : MonoBehaviour
{
    [Header("崩壊設定")]
    [SerializeField] private float explosionForce = 100f;
    [SerializeField] private float explosionRadius = 5f;
    [SerializeField] private float upwardModifier = 0.2f;

    [Header("崩壊後")]
    [SerializeField] private GameObject buildingBody;
    [SerializeField] private Collider buildingCollider;
    [SerializeField] private float debrisLifetime = 3f;

    [Header("崩壊エフェクト")]
    [SerializeField] private ParticleSystem collapseEffect;

    [Header("崩壊効果音（SoundPlayerに登録した名前）")]
    [SerializeField] private string collapseSoundName = "buildingCollapse";

    private bool collapsed = false;
    private BoxCollider[] debrisColliders;

    public void Collapse()
    {
        if (collapsed)
            return;

        collapsed = true;
        SoundPlayer.PlayIfExists(collapseSoundName);

        if (collapseEffect != null)
        {
            ParticleSystem effect = Instantiate(collapseEffect, transform.position, transform.rotation);
            effect.Play();
            ParticleSystem.MainModule main = effect.main;
            Destroy(effect.gameObject, main.duration + main.startLifetime.constantMax);
        }

        // 建物本体のColliderを無効化
        if (buildingCollider != null)
        {
            buildingCollider.enabled = false;
        }

        // 建物本体を非表示
        if (buildingBody != null)
        {
            buildingBody.SetActive(false);
        }

        debrisColliders =
            GetComponentsInChildren<BoxCollider>(true);

        // 子のRigidbodyを取得
        Rigidbody[] rigidbodies =
            GetComponentsInChildren<Rigidbody>();

        foreach (Rigidbody rb in rigidbodies)
        {
            rb.isKinematic = false;

            rb.AddExplosionForce(
                explosionForce,
                transform.position,
                explosionRadius,
                upwardModifier,
                ForceMode.Impulse
            );

            // 破片を一定時間後に削除
            Destroy(rb.gameObject, debrisLifetime);
        }

        Invoke(nameof(DisableDebrisColliders), 0.1f);

        // 破片が消えた後、建物全体を削除
        Invoke(nameof(DestroyBuildingInstance), debrisLifetime);
    }

    private void DisableDebrisColliders()
    {
        foreach (BoxCollider debrisCollider in debrisColliders)
        {
            if (debrisCollider != null)
            {
                debrisCollider.enabled = false;
            }
        }
    }

    private void DestroyBuildingInstance()
    {
        BuildingInstance instance =
            GetComponent<BuildingInstance>();

        if (instance != null)
        {
            instance.DestroyBuilding();
        }
        else
        {
            Debug.LogWarning(
                "BuildingInstanceが見つかりません。"
            );
        }
    }
}