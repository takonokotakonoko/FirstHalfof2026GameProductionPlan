using UnityEngine;

public class PunchController : MonoBehaviour
{
    private Animator animator;
    private PlayerAttack playerAttack;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        playerAttack = GetComponentInParent<PlayerAttack>();

        if (playerAttack == null)
        {
            Debug.LogWarning("PunchController: êeÇ‹ÇΩÇÕè„à Ç… PlayerAttack Ç™å©Ç¬Ç©ÇËÇ‹ÇπÇÒÅBAnimationEvent Ç©ÇÁçUåÇÇåƒÇ◊Ç‹ÇπÇÒÅB", this);
        }
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            TriggerPunchLeft();
        }
        else if (Input.GetMouseButtonDown(1))
        {
            TriggerPunchRight();
        }
    }

    public void TriggerPunchLeft()
    {
        if (animator == null)
            return;

        animator.SetTrigger("PunchLeft");
    }

    public void TriggerPunchRight()
    {
        if (animator == null)
            return;

        animator.SetTrigger("PunchRight");
    }

    public void OnPunchActionEvent(int actionIndex)
    {
        if (playerAttack == null)
            return;

        playerAttack.OnActionEvent(actionIndex);
    }

    public void OnPunchAnimationFinished()
    {
        if (playerAttack == null)
            return;

        playerAttack.OnAttackAnimationFinished();
    }
}