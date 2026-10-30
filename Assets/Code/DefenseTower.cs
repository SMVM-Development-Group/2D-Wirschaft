using UnityEngine;

public class DefenseTower : MonoBehaviour
{
    private const int RangeCircleSegments = 96;
    private const float TargetScanInterval = 0.3f;

    private BuildingInstance building;
    private LineRenderer rangeCircle;
    private float lastAttackTime;
    private float targetScanTimer;
    private Transform cachedTarget;

    private void Awake()
    {
        building = GetComponent<BuildingInstance>();
        CreateRangeCircle();
        SetRangeVisible(false);
    }

    private void Update()
    {
        if (building == null || building.data == null || !building.data.isDefenseTower) return;
        if (!building.IsConstructed()) return;
        if (building.GetOperatingWorkerCount() < building.data.workersNeeded) return;
        if (!building.IsCurrentlyWorkTime()) return;
        if (Time.time < lastAttackTime + building.data.towerAttackCooldown) return;

        // Zielsuche gedrosselt: alle 0.3s scannen statt jeden Frame alle entities anfassen
        targetScanTimer -= Time.deltaTime;
        if (targetScanTimer <= 0f)
        {
            targetScanTimer = TargetScanInterval;
            cachedTarget = FindTarget();
        }

        if (cachedTarget == null) return;

        // Ziel inzwischen verschwunden / außer Reichweite → verfallen lassen
        if (!cachedTarget.gameObject.activeInHierarchy ||
            Vector2.Distance(transform.position, cachedTarget.position) > building.data.towerRange)
        {
            cachedTarget = null;
            return;
        }

        ShootTarget(cachedTarget);
        lastAttackTime = Time.time;
    }

    private Transform FindTarget()
    {
        float range = building.data.towerRange;
        Transform closestTarget = null;
        float closestDistance = float.MaxValue;

        foreach (Ship ship in Ship.ActiveShips)
        {
            if (ship == null || !ship.gameObject.activeInHierarchy || ship.isLocal == building.isLocal) continue;
            TryPickClosest(ship.transform, range, ref closestTarget, ref closestDistance);
        }

        foreach (PlayerCombatHealth player in PlayerCombatHealth.All)
        {
            if (player == null || !player.gameObject.activeInHierarchy || !player.IsHostileTo(building.isLocal)) continue;
            TryPickClosest(player.transform, range, ref closestTarget, ref closestDistance);
        }

        foreach (Soldier soldier in Soldier.ActiveSoldiers)
        {
            if (soldier == null || !soldier.gameObject.activeInHierarchy) continue;
            bool isEnemy = building.isLocal ? soldier.team == Team.Enemy : soldier.team == Team.Player;
            if (!isEnemy) continue;
            TryPickClosest(soldier.transform, range, ref closestTarget, ref closestDistance);
        }

        return closestTarget;
    }

    private void TryPickClosest(Transform candidate, float range, ref Transform closestTarget, ref float closestDistance)
    {
        float distance = Vector2.Distance(transform.position, candidate.position);
        if (distance > range || distance >= closestDistance) return;

        closestDistance = distance;
        closestTarget = candidate;
    }

    private void ShootTarget(Transform target)
    {
        Vector3 start = transform.position + new Vector3(0f, 0.45f, 0f);
        Vector3 end = target.position;
        ArrowProjectile.Spawn(start, end);

        int damage = building.data.towerDamage;

        Ship ship = target.GetComponent<Ship>();
        if (ship != null)
        {
            ship.TakeDamage(damage);
            return;
        }

        PlayerCombatHealth player = target.GetComponent<PlayerCombatHealth>();
        if (player != null)
        {
            player.TakeDamage(damage);
            return;
        }

        Soldier soldier = target.GetComponent<Soldier>();
        if (soldier != null)
        {
            // Über NetCombat routen: Der BESITZER des Soldaten entscheidet über HP (Authority-Prinzip).
            NetCombat.ApplySoldierDamage(soldier, damage);
        }
    }

    public void SetRangeVisible(bool visible)
    {
        if (rangeCircle == null)
        {
            CreateRangeCircle();
        }

        if (rangeCircle != null)
        {
            rangeCircle.enabled = visible;
            if (visible)
            {
                UpdateRangeCircle();
            }
        }
    }

    private void CreateRangeCircle()
    {
        if (rangeCircle != null) return;

        GameObject circleObject = new GameObject("TowerAttackRange");
        circleObject.transform.SetParent(transform, false);
        circleObject.transform.localPosition = new Vector3(0f, 0f, -0.08f);

        rangeCircle = circleObject.AddComponent<LineRenderer>();
        rangeCircle.material = new Material(Shader.Find("Sprites/Default"));
        rangeCircle.useWorldSpace = true;
        rangeCircle.loop = true;
        rangeCircle.positionCount = RangeCircleSegments;
        rangeCircle.startWidth = 0.08f;
        rangeCircle.endWidth = 0.08f;
        rangeCircle.sortingOrder = 30;
        rangeCircle.startColor = new Color(1f, 0.72f, 0.18f, 0.9f);
        rangeCircle.endColor = rangeCircle.startColor;
    }

    private void UpdateRangeCircle()
    {
        if (rangeCircle == null || building == null || building.data == null) return;

        float radius = Mathf.Max(0.1f, building.data.towerRange);
        for (int i = 0; i < RangeCircleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / RangeCircleSegments;
            Vector3 center = transform.position;
            rangeCircle.SetPosition(i, new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius,
                center.z - 0.08f));
        }
    }

    private void OnDisable()
    {
        if (rangeCircle != null)
        {
            rangeCircle.enabled = false;
        }
    }
}
