using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerAttack : MonoBehaviour
{
    public enum WeaponType
    {
        Fist,
        Sword,
        Gun,
        Bomb,
        GreatSword,
        Dice
    }

    public enum AttackMode
    {
        Normal,
        Skill
    }

    [System.Serializable]
    public class AttackProfile
    {
        public AttackMode mode;
        public float range = 1.5f;
        public float radius = 0.8f;
        public float damage = 20f;
        public float offset = 0.6f;
        public float cooldown = 0.35f;
        public Color gizmoColor = Color.red;
    }

    [System.Serializable]
    public class AttackVisuals
    {
        public GameObject weaponObject;
        public ParticleSystem attackEffect;
        public GameObject projectilePrefab;
        public Transform effectSpawnPoint;
        public float showTime = 0.15f;
    }

    [System.Serializable]
    public class WeaponProfile
    {
        public WeaponType weaponType;
        public AttackProfile normalAttack;
        public AttackProfile skillAttack;
        public AttackVisuals visuals;
    }

    [Header("使える武器は1つのみ")]
    [SerializeField] private List<WeaponType> usableWeapons = new List<WeaponType>
    {
        WeaponType.Sword
    };

    [Header("武器ごとの通常攻撃 / スキル攻撃")]
    [SerializeField] private List<WeaponProfile> weaponProfiles = new List<WeaponProfile>
    {
        new WeaponProfile
        {
            weaponType = WeaponType.Fist,
            normalAttack = new AttackProfile { mode = AttackMode.Normal, range = 1.6f, radius = 0.8f, damage = 20f, offset = 0.6f, cooldown = 0.35f, gizmoColor = Color.red },
            skillAttack = new AttackProfile { mode = AttackMode.Skill, range = 2.2f, radius = 1.2f, damage = 40f, offset = 0.8f, cooldown = 0.8f, gizmoColor = Color.yellow }
        },
        new WeaponProfile
        {
            weaponType = WeaponType.Sword,
            normalAttack = new AttackProfile { mode = AttackMode.Normal, range = 2.8f, radius = 1.0f, damage = 35f, offset = 1.0f, cooldown = 0.5f, gizmoColor = Color.magenta },
            skillAttack = new AttackProfile { mode = AttackMode.Skill, range = 4.5f, radius = 1.5f, damage = 80f, offset = 1.5f, cooldown = 1.2f, gizmoColor = Color.cyan }
        },
        new WeaponProfile
        {
            weaponType = WeaponType.Gun,
            normalAttack = new AttackProfile { mode = AttackMode.Normal, range = 7.0f, radius = 0.5f, damage = 25f, offset = 1.5f, cooldown = 0.45f, gizmoColor = Color.cyan },
            skillAttack = new AttackProfile { mode = AttackMode.Skill, range = 9.0f, radius = 0.7f, damage = 60f, offset = 2.0f, cooldown = 1.5f, gizmoColor = Color.blue }
        },
        new WeaponProfile
        {
            weaponType = WeaponType.Bomb,
            normalAttack = new AttackProfile { mode = AttackMode.Normal, range = 3.5f, radius = 1.5f, damage = 45f, offset = 1.2f, cooldown = 0.75f, gizmoColor = Color.yellow },
            skillAttack = new AttackProfile { mode = AttackMode.Skill, range = 5.0f, radius = 2.2f, damage = 90f, offset = 1.6f, cooldown = 2.0f, gizmoColor = Color.white }
        },
        new WeaponProfile
        {
            weaponType = WeaponType.GreatSword,
            normalAttack = new AttackProfile { mode = AttackMode.Normal, range = 2.5f, radius = 1.5f, damage = 40f, offset = 1.2f, cooldown = 0.6f, gizmoColor = Color.white },
            skillAttack = new AttackProfile { mode = AttackMode.Skill, range = 5.0f, radius = 0.6f, damage = 75f, offset = 1.8f, cooldown = 1.3f, gizmoColor = new Color(0.9f, 0.9f, 1.0f) }
        },
        new WeaponProfile
        {
            weaponType = WeaponType.Dice,
            normalAttack = new AttackProfile { mode = AttackMode.Normal, range = 3.0f, radius = 0.8f, damage = 30f, offset = 1.1f, cooldown = 0.5f, gizmoColor = new Color(0.6f, 0.2f, 0.8f) },
            skillAttack = new AttackProfile { mode = AttackMode.Skill, range = 4.0f, radius = 1.2f, damage = 50f, offset = 1.4f, cooldown = 1.5f, gizmoColor = new Color(0.8f, 0.4f, 1.0f) }
        }
    };

    [Header("攻撃のオブジェクトやエフェクト")]
    [SerializeField] private AttackVisuals swordVisuals;
    [SerializeField] private AttackVisuals gunVisuals;
    [SerializeField] private AttackVisuals bombVisuals;
    [SerializeField] private AttackVisuals greatSwordVisuals;
    [SerializeField] private AttackVisuals diceVisuals;

    [Header("攻撃の高さ")]
    [SerializeField] private float attackHeight = 1.1f;

    [Header("攻撃のクールタイム表記")]
    public TextMeshProUGUI normal_Cooldown;
    public TextMeshProUGUI skill_Cooldown;

    [Header("円形クールタイムゲージ")]
    [SerializeField] private Image normalCooldownGauge;
    [SerializeField] private Image skillCooldownGauge;

    private Dictionary<WeaponType, WeaponProfile> weaponMap;
    private float normalReadyTime;
    private float skillReadyTime;

    private void Awake()
    {
        BuildWeaponMap();
        ApplyUsableWeaponStates();
        ConfigureCooldownGauge(normalCooldownGauge);
        ConfigureCooldownGauge(skillCooldownGauge);
        UpdateCooldownDisplay();
    }

    private void OnValidate()
    {
        if (usableWeapons == null)
            usableWeapons = new List<WeaponType>();

        if (usableWeapons.Count > 1)
            usableWeapons = new List<WeaponType>(usableWeapons.GetRange(0, 1));

        var unique = new HashSet<WeaponType>();
        for (int i = usableWeapons.Count - 1; i >= 0; i--)
        {
            if (!unique.Add(usableWeapons[i]))
                usableWeapons.RemoveAt(i);
        }

        BuildWeaponMap();
        ApplyUsableWeaponStates();
    }

    private void BuildWeaponMap()
    {
        weaponMap = new Dictionary<WeaponType, WeaponProfile>();
        foreach (var weapon in weaponProfiles)
        {
            if (weapon == null)
                continue;

            if (!weaponMap.ContainsKey(weapon.weaponType))
                weaponMap[weapon.weaponType] = weapon;
        }
    }

    public bool IsUsableWeapon(WeaponType weaponType)
    {
        return usableWeapons != null && usableWeapons.Contains(weaponType);
    }

    private void ApplyUsableWeaponStates()
    {
        if (weaponMap == null)
            return;

        foreach (var weapon in weaponMap.Values)
        {
            if (weapon == null || weapon.visuals == null || weapon.visuals.weaponObject == null)
                continue;

            weapon.visuals.weaponObject.SetActive(IsUsableWeapon(weapon.weaponType));
        }
    }

    private void Update()
    {
        UpdateCooldownDisplay();

        if (Input.GetMouseButtonDown(0) && Time.time >= normalReadyTime)
        {
            var profile = GetCurrentWeaponNormalAttack();
            if (profile != null)
                PerformAttack(profile, AttackMode.Normal);
        }
        else if (Input.GetMouseButtonDown(1) && Time.time >= skillReadyTime)
        {
            var profile = GetCurrentWeaponSkillAttack();
            if (profile != null)
                PerformAttack(profile, AttackMode.Skill);
        }

        if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
        {
            DrawHeldAttackRange();
        }
    }

    private WeaponProfile GetCurrentWeaponProfile()
    {
        if (usableWeapons == null || usableWeapons.Count == 0)
            return null;

        if (weaponMap == null || !weaponMap.TryGetValue(usableWeapons[0], out var weapon))
            return null;

        return weapon;
    }

    private AttackProfile GetCurrentWeaponNormalAttack()
    {
        var weapon = GetCurrentWeaponProfile();
        return weapon != null ? weapon.normalAttack : null;
    }

    private AttackProfile GetCurrentWeaponSkillAttack()
    {
        var weapon = GetCurrentWeaponProfile();
        return weapon != null ? weapon.skillAttack : null;
    }

    private void PerformAttack(AttackProfile profile, AttackMode attackMode)
    {
        if (profile == null)
            return;

        if (attackMode == AttackMode.Normal)
            normalReadyTime = Time.time + profile.cooldown;
        else
            skillReadyTime = Time.time + profile.cooldown;

        PlayAttackVisual();

        Vector3 attackCenter = transform.position + transform.forward * profile.offset + Vector3.up * attackHeight;
        Vector3 halfExtents = new Vector3(profile.radius, attackHeight, profile.range * 0.5f);

        Collider[] hitColliders = Physics.OverlapBox(
            attackCenter,
            halfExtents,
            transform.rotation,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        int damageCount = 0;
        foreach (Collider hit in hitColliders)
        {
            if (hit.gameObject == gameObject)
                continue;

            if (hit.TryGetComponent<IDamage>(out var damageable))
            {
                damageable.TakeDamage(profile.damage);
                damageCount++;
            }
        }

        Debug.Log($"{profile.mode} 攻撃: {damageCount}体にダメージ {profile.damage}");
    }

    private void UpdateCooldownDisplay()
    {
        AttackProfile normalAttack = GetCurrentWeaponNormalAttack();
        AttackProfile skillAttack = GetCurrentWeaponSkillAttack();

        UpdateCooldownLabel(normal_Cooldown, normalReadyTime);
        UpdateCooldownLabel(skill_Cooldown, skillReadyTime);
        UpdateCooldownGauge(normalCooldownGauge, normalReadyTime, normalAttack);
        UpdateCooldownGauge(skillCooldownGauge, skillReadyTime, skillAttack);
    }

    private void UpdateCooldownLabel(TextMeshProUGUI label, float readyTime)
    {
        if (label == null)
            return;

        float remainingTime = Mathf.Max(0f, readyTime - Time.time);
        label.text = remainingTime > 0f
            ? remainingTime.ToString("F1")
            : "Ready";
    }

    private void ConfigureCooldownGauge(Image gauge)
    {
        if (gauge == null)
            return;

        gauge.type = Image.Type.Filled;
        gauge.fillMethod = Image.FillMethod.Radial360;
        gauge.fillOrigin = (int)Image.Origin360.Bottom;
        gauge.fillClockwise = true;
        gauge.fillAmount = 1f;
    }

    private void UpdateCooldownGauge(Image gauge, float readyTime, AttackProfile attack)
    {
        if (gauge == null)
            return;

        if (attack == null || attack.cooldown <= 0f)
        {
            gauge.fillAmount = 1f;
            return;
        }

        float remainingTime = Mathf.Max(0f, readyTime - Time.time);
        gauge.fillAmount = Mathf.Clamp01(1f - remainingTime / attack.cooldown);
    }

    private void PlayAttackVisual()
    {
        var weapon = GetCurrentWeaponProfile();
        if (weapon == null || weapon.visuals == null)
            return;

        var visuals = weapon.visuals;

        if (visuals.weaponObject != null)
        {
            visuals.weaponObject.SetActive(false);
            StartCoroutine(DisableWeaponObject(visuals.weaponObject, visuals.showTime));
        }

        if (visuals.attackEffect != null)
        {
            visuals.attackEffect.transform.position = GetEffectSpawnPosition(visuals);
            visuals.attackEffect.transform.rotation = transform.rotation;
            visuals.attackEffect.Play();
        }

        if (visuals.projectilePrefab != null)
        {
            Vector3 spawnPosition = GetEffectSpawnPosition(visuals);
            GameObject projectile = Instantiate(visuals.projectilePrefab, spawnPosition, transform.rotation);
            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = transform.forward * 12f;
            }
        }
    }

    private Vector3 GetEffectSpawnPosition(AttackVisuals visuals)
    {
        if (visuals == null)
            return transform.position + transform.forward * 1.5f + Vector3.up * 1.0f;

        if (visuals.effectSpawnPoint != null)
            return visuals.effectSpawnPoint.position;

        return transform.position + transform.forward * 1.5f + Vector3.up * 1.0f;
    }

    private IEnumerator DisableWeaponObject(GameObject weaponObject, float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        if (weaponObject != null)
            weaponObject.SetActive(true);
    }

    private void DrawHeldAttackRange()
    {
        var weapon = GetCurrentWeaponProfile();
        if (weapon == null)
            return;

        AttackProfile activeProfile = Input.GetMouseButton(0)
            ? weapon.normalAttack
            : weapon.skillAttack;

        if (activeProfile == null)
            return;

        Vector3 center = transform.position + transform.forward * activeProfile.offset + Vector3.up * attackHeight;
        Vector3 size = new Vector3(activeProfile.radius * 2f, attackHeight * 2f, activeProfile.range);
        AttackRangeVisualizer.DrawBoxRange(center, size, transform.rotation, activeProfile.gizmoColor, 0.05f);
    }
}
