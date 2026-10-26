using UnityEngine;

public interface IDamage
{
    void TakeDamage(float damageAmount);
}

public static class DamageTargetFilter
{
    public static bool IsBuilding(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            if (current.CompareTag("Building"))
                return true;
        }

        return false;
    }
}
