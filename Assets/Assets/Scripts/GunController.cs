using UnityEngine;

public class GunController : MonoBehaviour
{
    private PlayerAttack playerAttack;
    private Quaternion initialLocalRotation;

    private void Awake()
    {
        initialLocalRotation = transform.localRotation;
        playerAttack = GetComponentInParent<PlayerAttack>();

        if (playerAttack == null)
            Debug.LogWarning("GunController requires a PlayerAttack component on this object or a parent.", this);
    }

    public void OnGunActionEvent(int actionIndex)
    {
        if (playerAttack == null)
            return;

        playerAttack.OnActionEvent(actionIndex);
    }

    public void OnGunAnimationFinished()
    {
        transform.localRotation = initialLocalRotation;

        if (playerAttack == null)
            return;

        playerAttack.OnAttackAnimationFinished();
    }
}
