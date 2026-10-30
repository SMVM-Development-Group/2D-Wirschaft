using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public enum Team
{
    Player,
    Enemy
}

public enum SoldierType
{
    Spear,
    Shield,
    Sword,
    Bow
}

public enum WeaponMaterial
{
    Wood,
    Stone,
    Gold,
    Iron
}

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public class Soldier : MonoBehaviour
{
    private const float ArriveDistance = 0.08f;
    private const float AggroPadding = 4f;
    private const int MaxPathIterations = 12000;

    public static readonly List<Soldier> ActiveSoldiers = new List<Soldier>();

    public static int nextNetIdValue = 1;
    public int netId;

    [Header("Welcher Soldat ist das?")]
    public SoldierType soldierType;

    [Header("Material der Ausrustung")]
    public WeaponMaterial weaponMaterial = WeaponMaterial.Stone;

    [Header("Team Einstellungen")]
    public Team team = Team.Player;
    public int ownerActorNumber;

    [Header("Bilder / Icons")]
    public Sprite spearSprite;
    public Sprite shieldSprite;
    public Sprite swordSprite;
    public Sprite bowSprite;

    [Header("Aktuelle Stats")]
    public float maxHealth = 100f;
    public float shield;
    public float damage;
    public float attackRange;
    public float attackCooldown;
    public float moveSpeed = 1.5f;

    private float currentHealth;
    private float lastAttackTime;
    private bool isSelected;
    private Color ownerColor = Color.white;

    private SpriteRenderer spriteRenderer;
    private TextMesh nameText;
    private TextMesh healthText;
    private LineRenderer circleRenderer;
    private LineRenderer patrolRenderer;

    private readonly List<Vector2> currentPath = new List<Vector2>();
    private int currentPathIndex;
    private bool hasMoveOrder;
    private bool attackMoveEnabled;
    private Soldier attackTarget;
    private BuildingInstance attackBuildingTarget;

    private bool hasPatrolOrder;
    private Vector2 patrolPointA;
    private Vector2 patrolPointB;
    private bool patrolTowardsB;
    private bool hasReportedEnemyContact;

    private float syncTimer;
    private Vector3 lastSyncPos;
    private const float SyncInterval = 0.2f;
    private const float SyncMoveThreshold = 0.5f;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spearSprite == null) spearSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/speersoldat.png");
        if (shieldSprite == null) shieldSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/schildsoldat.png");
        if (swordSprite == null) swordSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/schwertkämpfer.png");
        if (bowSprite == null) bowSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/bogensoldat.png");

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.sprite = GetSpriteForType();
        }
    }
#endif

    public bool IsOwnedByLocalPlayer => team == Team.Player;
    public bool IsSelected => isSelected;
    public Vector2 Position2D => transform.position;

    private void OnEnable()
    {
        if (!ActiveSoldiers.Contains(this))
        {
            ActiveSoldiers.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveSoldiers.Remove(this);
    }

    private void Awake()
    {
        if (netId == 0) netId = nextNetIdValue++;
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sortingOrder = 21;

        if (spearSprite == null) spearSprite = Resources.Load<Sprite>("Textures/speersoldat");
        if (shieldSprite == null) shieldSprite = Resources.Load<Sprite>("Textures/schildsoldat");
        if (swordSprite == null) swordSprite = Resources.Load<Sprite>("Textures/schwertkämpfer");
        if (bowSprite == null) bowSprite = Resources.Load<Sprite>("Textures/bogensoldat");

        if (ownerActorNumber == 0 && team == Team.Player && PhotonNetwork.LocalPlayer != null)
        {
            ownerActorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
        }

        ApplyStatsForType();
    }

    private void Start()
    {
        ApplyWeaponMaterialMultiplier();
        currentHealth = maxHealth;
        ownerColor = PlayerColorUtility.GetColorForActor(ownerActorNumber, team == Team.Player);

        SetupVisuals();
        ApplyOwnerTint();
        UpdateHealthText();
        SetSelected(false);
    }

    private void Update()
    {
        CleanupTarget();

        Vector2Int currentGrid = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        if (!BuildingManager.IsWalkable(currentGrid))
        {
            Vector2 snapped = SnapToNearestPassable(transform.position);
            Vector2Int snappedGrid = new Vector2Int(Mathf.RoundToInt(snapped.x), Mathf.RoundToInt(snapped.y));
            if (snappedGrid != currentGrid && BuildingManager.IsWalkable(snappedGrid))
            {
                transform.position = new Vector3(snapped.x, snapped.y, transform.position.z);
                if (hasMoveOrder) SetPathTo(currentPath.Count > 0 ? currentPath[currentPath.Count - 1] : transform.position);
            }
            return;
        }

        if (attackTarget == null)
        {
            attackTarget = FindPreferredEnemy();
        }

        // Check for enemy buildings in range
        if (attackBuildingTarget == null || !attackBuildingTarget.gameObject.activeInHierarchy)
        {
            attackBuildingTarget = FindEnemyBuildingInRange(attackRange);
        }

        if (attackBuildingTarget != null)
        {
            float distance = Vector2.Distance(transform.position, attackBuildingTarget.transform.position);
            if (distance <= attackRange)
            {
                if (Time.time >= lastAttackTime + attackCooldown)
                {
                    AttackBuilding(attackBuildingTarget);
                    lastAttackTime = Time.time;
                }
                return;
            }
            else if (distance <= attackRange + AggroPadding)
            {
                MoveTowards(attackBuildingTarget.transform.position);
                return;
            }
        }

        if (attackTarget != null)
        {
            if (!hasReportedEnemyContact && team == Team.Player)
            {
                hasReportedEnemyContact = true;
                NotificationManager.Instance?.Notify("soldier_enemy_found", "Ein Soldat hat einen Feind gefunden.", 6f);
            }

            float distance = Vector2.Distance(transform.position, attackTarget.transform.position);
            if (distance <= attackRange)
            {
                if (Time.time >= lastAttackTime + attackCooldown)
                {
                    Attack(attackTarget);
                    lastAttackTime = Time.time;
                }
                return;
            }

            if (ShouldChaseEnemy(distance))
            {
                MoveTowards(attackTarget.transform.position);
                return;
            }
        }

        attackTarget = null;
        attackBuildingTarget = null;
        hasReportedEnemyContact = false;
        FollowOrders();
        BroadcastPosition();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;

        if (circleRenderer != null)
        {
            circleRenderer.enabled = selected;
        }

        if (nameText != null)
        {
            nameText.color = selected ? Color.Lerp(ownerColor, Color.white, 0.35f) : Color.white;
        }
    }

    public void IssueMoveOrder(Vector2 destination)
    {
        hasPatrolOrder = false;
        if (patrolRenderer != null) patrolRenderer.enabled = false;
        attackMoveEnabled = false;
        attackTarget = null;
        attackBuildingTarget = null;
        SetPathTo(destination);
    }

    public void IssueAttackMoveOrder(Vector2 destination)
    {
        hasPatrolOrder = false;
        if (patrolRenderer != null) patrolRenderer.enabled = false;
        attackMoveEnabled = true;
        attackTarget = null;
        attackBuildingTarget = null;
        SetPathTo(destination);
    }

    public void IssueObserveOrder(Vector2 pointA, Vector2 pointB)
    {
        attackMoveEnabled = true;
        hasPatrolOrder = true;
        patrolPointA = SnapToNearestPassable(pointA);
        patrolPointB = SnapToNearestPassable(pointB);
        patrolTowardsB = true;
        UpdatePatrolRenderer();
        SetPathTo(patrolPointB);
    }

    public void TakeDamage(float amount)
    {
        if (shield > 0f)
        {
            shield -= amount;
            if (shield < 0f)
            {
                currentHealth += shield;
                shield = 0f;
            }
        }
        else
        {
            currentHealth -= amount;
        }

        UpdateHealthText();

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    public void SetTeam(Team newTeam, int newOwnerActorNumber = 0)
    {
        team = newTeam;

        if (newOwnerActorNumber > 0)
        {
            ownerActorNumber = newOwnerActorNumber;
        }
        else if (team == Team.Player && ownerActorNumber == 0 && PhotonNetwork.LocalPlayer != null)
        {
            ownerActorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
        }
        else if (team == Team.Enemy)
        {
            ownerActorNumber = 0;
        }

        ownerColor = PlayerColorUtility.GetColorForActor(ownerActorNumber, team == Team.Player);
        ApplyOwnerTint();

        if (circleRenderer != null)
        {
            circleRenderer.startColor = new Color(ownerColor.r, ownerColor.g, ownerColor.b, 0.45f);
            circleRenderer.endColor = circleRenderer.startColor;
        }

        if (patrolRenderer != null)
        {
            patrolRenderer.startColor = new Color(ownerColor.r, ownerColor.g, ownerColor.b, 0.7f);
            patrolRenderer.endColor = patrolRenderer.startColor;
        }
    }

    private void ApplyStatsForType()
    {
        spriteRenderer.sprite = GetSpriteForType();

        switch (soldierType)
        {
            case SoldierType.Spear:
                maxHealth = 100f;
                damage = 5f;
                attackRange = 1.5f;
                attackCooldown = 2.5f;
                shield = 0f;
                break;
            case SoldierType.Shield:
                maxHealth = 150f;
                damage = 3f;
                attackRange = 1f;
                attackCooldown = 2.5f;
                shield = 30f;
                break;
            case SoldierType.Sword:
                maxHealth = 100f;
                damage = 6f;
                attackRange = 1.2f;
                attackCooldown = 2f;
                shield = 0f;
                break;
            case SoldierType.Bow:
                maxHealth = 80f;
                damage = 8f;
                attackRange = 15f;
                attackCooldown = 3f;
                shield = 0f;
                break;
        }
    }

    private Sprite GetSpriteForType()
    {
        switch (soldierType)
        {
            case SoldierType.Spear: return spearSprite;
            case SoldierType.Shield: return shieldSprite;
            case SoldierType.Sword: return swordSprite;
            case SoldierType.Bow: return bowSprite;
            default: return spearSprite;
        }
    }

    private void ApplyWeaponMaterialMultiplier()
    {
        float multiplier = 1f;
        switch (weaponMaterial)
        {
            case WeaponMaterial.Wood: multiplier = 0.6f; break;
            case WeaponMaterial.Stone: multiplier = 1f; break;
            case WeaponMaterial.Gold: multiplier = 1.2f; break;
            case WeaponMaterial.Iron: multiplier = 1.4f; break;
        }

        maxHealth *= multiplier;
        damage *= multiplier;
        shield *= multiplier;
    }

    private void SetupVisuals()
    {
        GameObject nameObj = new GameObject("NameTag");
        nameObj.transform.SetParent(transform);
        nameObj.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        nameText = nameObj.AddComponent<TextMesh>();
        nameText.text = soldierType.ToString();
        nameText.characterSize = 0.1f;
        nameText.fontSize = 40;
        nameText.anchor = TextAnchor.MiddleCenter;
        nameText.alignment = TextAlignment.Center;
        nameText.GetComponent<MeshRenderer>().sortingOrder = 10;

        GameObject hpObj = new GameObject("HealthTag");
        hpObj.transform.SetParent(transform);
        hpObj.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        healthText = hpObj.AddComponent<TextMesh>();
        healthText.characterSize = 0.1f;
        healthText.fontSize = 35;
        healthText.anchor = TextAnchor.MiddleCenter;
        healthText.alignment = TextAlignment.Center;
        healthText.GetComponent<MeshRenderer>().sortingOrder = 10;

        circleRenderer = GetComponent<LineRenderer>();
        if (circleRenderer == null)
        {
            circleRenderer = gameObject.AddComponent<LineRenderer>();
        }

        circleRenderer.startWidth = 0.1f;
        circleRenderer.endWidth = 0.1f;
        circleRenderer.useWorldSpace = false;
        circleRenderer.loop = true;
        circleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        circleRenderer.startColor = new Color(ownerColor.r, ownerColor.g, ownerColor.b, 0.45f);
        circleRenderer.endColor = circleRenderer.startColor;
        circleRenderer.sortingOrder = 5;
        circleRenderer.positionCount = 50;
        for (int i = 0; i < 50; i++)
        {
            float angle = i * Mathf.PI * 2f / 50f;
            circleRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * attackRange, Mathf.Sin(angle) * attackRange, 0f));
        }

        Transform patrolLineTransform = transform.Find("PatrolRouteLine");
        if (patrolLineTransform == null)
        {
            GameObject patrolLineObject = new GameObject("PatrolRouteLine");
            patrolLineObject.transform.SetParent(transform, false);
            patrolLineObject.transform.localPosition = Vector3.zero;
            patrolRenderer = patrolLineObject.AddComponent<LineRenderer>();
        }
        else
        {
            patrolRenderer = patrolLineTransform.GetComponent<LineRenderer>();
            if (patrolRenderer == null)
            {
                patrolRenderer = patrolLineTransform.gameObject.AddComponent<LineRenderer>();
            }
        }

        patrolRenderer.startWidth = 0.08f;
        patrolRenderer.endWidth = 0.08f;
        patrolRenderer.useWorldSpace = true;
        patrolRenderer.loop = false;
        patrolRenderer.positionCount = 2;
        patrolRenderer.material = new Material(Shader.Find("Sprites/Default"));
        patrolRenderer.startColor = new Color(ownerColor.r, ownerColor.g, ownerColor.b, 0.7f);
        patrolRenderer.endColor = patrolRenderer.startColor;
        patrolRenderer.sortingOrder = 4;
        patrolRenderer.enabled = false;
    }

    private void ApplyOwnerTint()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.Lerp(Color.white, ownerColor, 0.35f);
        }
    }

    private void UpdateHealthText()
    {
        if (healthText == null)
        {
            return;
        }

        if (shield > 0f)
        {
            healthText.text = $"HP: {Mathf.CeilToInt(currentHealth)} | Schild: {Mathf.CeilToInt(shield)}";
            healthText.color = Color.cyan;
        }
        else
        {
            healthText.text = $"HP: {Mathf.CeilToInt(currentHealth)}";
            healthText.color = currentHealth > maxHealth * 0.5f
                ? Color.green
                : currentHealth > maxHealth * 0.25f ? Color.yellow : Color.red;
        }
    }

    private void CleanupTarget()
    {
        if (attackTarget != null)
        {
            if (!attackTarget.gameObject.activeInHierarchy || attackTarget.currentHealth <= 0f)
            {
                attackTarget = null;
            }
        }

        if (attackBuildingTarget != null)
        {
            if (!attackBuildingTarget.gameObject.activeInHierarchy || attackBuildingTarget.currentHP <= 0)
            {
                attackBuildingTarget = null;
            }
        }
    }

    private Soldier FindPreferredEnemy()
    {
        float searchRange = attackMoveEnabled || hasPatrolOrder ? attackRange + AggroPadding : attackRange;
        float closestDistance = float.MaxValue;
        Soldier closest = null;

        for (int i = 0; i < ActiveSoldiers.Count; i++)
        {
            Soldier other = ActiveSoldiers[i];
            if (other == null || other == this || !IsHostileTo(other))
            {
                continue;
            }

            float distance = Vector2.Distance(transform.position, other.transform.position);
            if (distance <= searchRange && distance < closestDistance)
            {
                closestDistance = distance;
                closest = other;
            }
        }

        return closest;
    }

    /// <summary>Finde feindliche Gebäude in Reichweite.</summary>
    private BuildingInstance FindEnemyBuildingInRange(float range)
    {
        BuildingInstance[] buildings = FindObjectsByType<BuildingInstance>();
        BuildingInstance closest = null;
        float closestDist = float.MaxValue;

        foreach (var b in buildings)
        {
            if (b == null || !b.gameObject.activeInHierarchy) continue;

            bool isEnemy = false;
            if (team == Team.Player && !b.isLocal)
                isEnemy = true;
            else if (team == Team.Enemy && b.isLocal)
                isEnemy = true;

            if (!isEnemy) continue;

            float distance = Vector2.Distance(transform.position, b.transform.position);
            if (distance <= range && distance < closestDist)
            {
                closestDist = distance;
                closest = b;
            }
        }

        return closest;
    }

    private bool IsHostileTo(Soldier other)
    {
        if (other == null)
        {
            return false;
        }

        if (ownerActorNumber > 0 && other.ownerActorNumber > 0)
        {
            return ownerActorNumber != other.ownerActorNumber;
        }

        return team != other.team;
    }

    private bool ShouldChaseEnemy(float distance)
    {
        if (attackTarget == null)
        {
            return false;
        }

        if (hasPatrolOrder || attackMoveEnabled)
        {
            return distance <= attackRange + AggroPadding;
        }

        return false;
    }

    private void FollowOrders()
    {
        if (hasMoveOrder)
        {
            if (currentPathIndex < currentPath.Count)
            {
                Vector2 waypoint = currentPath[currentPathIndex];
                MoveTowards(waypoint);
                if (Vector2.Distance(transform.position, waypoint) <= ArriveDistance)
                {
                    currentPathIndex++;
                }
            }
            else
            {
                hasMoveOrder = false;
                if (hasPatrolOrder)
                {
                    patrolTowardsB = !patrolTowardsB;
                    Vector2 nextPoint = patrolTowardsB ? patrolPointB : patrolPointA;
                    SetPathTo(nextPoint);
                }
            }
        }
    }

    private void MoveTowards(Vector2 targetPos)
    {
        Vector2 next = Vector2.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        Vector2Int nextGrid = new Vector2Int(Mathf.RoundToInt(next.x), Mathf.RoundToInt(next.y));
        if (!BuildingManager.IsWalkable(nextGrid)) return;
        transform.position = new Vector3(next.x, next.y, transform.position.z);
    }

    private void Attack(Soldier enemySoldier)
    {
        if (enemySoldier == null)
        {
            return;
        }

        if (soldierType == SoldierType.Bow)
        {
            ArrowProjectile.Spawn(transform.position + new Vector3(0f, 0.1f, 0f), enemySoldier.transform.position);
        }

        SpawnHitEffect(enemySoldier.transform.position);
        enemySoldier.TakeDamage(damage);
    }

    private void AttackBuilding(BuildingInstance building)
    {
        if (building == null) return;

        if (soldierType == SoldierType.Bow)
        {
            ArrowProjectile.Spawn(transform.position + new Vector3(0f, 0.1f, 0f), building.transform.position);
        }

        SpawnHitEffect(building.transform.position);

        int damageToDeal = Mathf.RoundToInt(damage);
        building.TakeDamage(damageToDeal);

        if (team == Team.Player)
        {
            NotificationManager.Instance?.Notify("building_under_attack",
                "Dein Gebäude wird angegriffen!", 3f);
        }
    }

    private void SpawnHitEffect(Vector3 position)
    {
        GameObject pObj = new GameObject("HitEffect");
        pObj.transform.position = position;
        ParticleSystem ps = pObj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = 0.3f;
        main.startSpeed = 2f;
        main.startSize = 0.3f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.maxParticles = 10;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        var burst = new ParticleSystem.Burst(0f, 6);
        emission.SetBursts(new ParticleSystem.Burst[] { burst });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f;

        var renderer = pObj.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Play();
    }

    private void Die()
    {
        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.NotifySoldierDestroyed(this);
        }

        Destroy(gameObject);
    }

    private void BroadcastPosition()
    {
        if (!PhotonNetwork.InRoom) return;
        if (team != Team.Player) return;

        Vector3 pos = transform.position;
        if (Vector3.Distance(pos, lastSyncPos) < SyncMoveThreshold) return;

        syncTimer += Time.deltaTime;
        if (syncTimer < SyncInterval) return;
        syncTimer = 0f;
        lastSyncPos = pos;

        object[] data = new object[] { netId, pos.x, pos.y };
        PhotonNetwork.RaiseEvent(15, data,
            new Photon.Realtime.RaiseEventOptions { Receivers = Photon.Realtime.ReceiverGroup.Others },
            new ExitGames.Client.Photon.SendOptions { Reliability = false });
    }

    private void UpdatePatrolRenderer()
    {
        if (patrolRenderer == null)
        {
            return;
        }

        patrolRenderer.enabled = false;
        if (!hasPatrolOrder)
        {
            return;
        }

        patrolRenderer.SetPosition(0, new Vector3(patrolPointA.x, patrolPointA.y, -0.05f));
        patrolRenderer.SetPosition(1, new Vector3(patrolPointB.x, patrolPointB.y, -0.05f));
    }

    private void SetPathTo(Vector2 destination)
    {
        Vector2 start = new Vector2(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));
        if (!BuildingManager.IsWalkable(start))
        {
            Vector2 snappedStart = SnapToNearestPassable(transform.position);
            Vector2Int snappedGrid = new Vector2Int(Mathf.RoundToInt(snappedStart.x), Mathf.RoundToInt(snappedStart.y));
            Vector2Int startGrid = new Vector2Int(Mathf.RoundToInt(start.x), Mathf.RoundToInt(start.y));
            if (snappedGrid != startGrid && BuildingManager.IsWalkable(snappedGrid))
            {
                transform.position = new Vector3(snappedStart.x, snappedStart.y, transform.position.z);
                start = snappedStart;
            }
        }
        Vector2 snappedDestination = SnapToNearestPassable(destination);
        List<Vector2> newPath = BuildingManager.FindPath(start, snappedDestination);

        currentPath.Clear();
        currentPathIndex = 0;

        if (newPath.Count == 0)
        {
            hasMoveOrder = false;
            NotificationManager.Instance?.Notify("no_path",
                "Kein Weg! Soldat kann nur auf Inseln und Stegen laufen.", 4f);
            return;
        }

        currentPath.AddRange(newPath);
        hasMoveOrder = true;
    }

    private static Vector2 SnapToNearestPassable(Vector2 target)
    {
        Vector2 snapped = new Vector2(Mathf.Round(target.x), Mathf.Round(target.y));
        if (BuildingManager.IsWalkable(snapped))
        {
            return snapped;
        }

        Queue<Vector2> queue = new Queue<Vector2>();
        HashSet<Vector2> visited = new HashSet<Vector2>();
        queue.Enqueue(snapped);
        visited.Add(snapped);

        Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        while (queue.Count > 0 && visited.Count < 2500)
        {
            Vector2 current = queue.Dequeue();
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2 next = current + directions[i];
                if (!visited.Add(next))
                {
                    continue;
                }

                if (BuildingManager.IsWalkable(next))
                {
                    return next;
                }

                queue.Enqueue(next);
            }
        }

        return snapped;
    }

    private static List<Vector2> FindPath(Vector2 start, Vector2 destination)
    {
        List<Vector2> empty = new List<Vector2>();
        if (!IslandManager.IsLand(start) || !IslandManager.IsLand(destination))
        {
            return empty;
        }

        if (start == destination)
        {
            empty.Add(destination);
            return empty;
        }

        PriorityQueue frontier = new PriorityQueue();
        frontier.Enqueue(start, 0f);

        Dictionary<Vector2, Vector2> cameFrom = new Dictionary<Vector2, Vector2>();
        Dictionary<Vector2, float> costSoFar = new Dictionary<Vector2, float>
        {
            [start] = 0f
        };

        Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        int iterations = 0;

        while (frontier.Count > 0 && iterations++ < MaxPathIterations)
        {
            Vector2 current = frontier.Dequeue();
            if (current == destination)
            {
                return ReconstructPath(cameFrom, destination);
            }

            for (int i = 0; i < directions.Length; i++)
            {
                Vector2 next = current + directions[i];
                if (!IslandManager.IsLand(next))
                {
                    continue;
                }

                float newCost = costSoFar[current] + 1f;
                if (costSoFar.TryGetValue(next, out float existingCost) && newCost >= existingCost)
                {
                    continue;
                }

                costSoFar[next] = newCost;
                cameFrom[next] = current;
                frontier.Enqueue(next, newCost + Heuristic(next, destination));
            }
        }

        return empty;
    }

    private static List<Vector2> ReconstructPath(Dictionary<Vector2, Vector2> cameFrom, Vector2 destination)
    {
        List<Vector2> path = new List<Vector2> { destination };
        Vector2 current = destination;

        while (cameFrom.TryGetValue(current, out Vector2 previous))
        {
            current = previous;
            path.Add(current);
        }

        path.Reverse();
        if (path.Count > 0)
        {
            path.RemoveAt(0);
        }

        return path;
    }

    private static float Heuristic(Vector2 a, Vector2 b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private sealed class PriorityQueue
    {
        private readonly List<(Vector2 node, float priority)> items = new List<(Vector2, float)>();

        public int Count => items.Count;

        public void Enqueue(Vector2 node, float priority)
        {
            items.Add((node, priority));
        }

        public Vector2 Dequeue()
        {
            int bestIndex = 0;
            float bestPriority = items[0].priority;
            for (int i = 1; i < items.Count; i++)
            {
                if (items[i].priority < bestPriority)
                {
                    bestPriority = items[i].priority;
                    bestIndex = i;
                }
            }

            Vector2 result = items[bestIndex].node;
            items.RemoveAt(bestIndex);
            return result;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}


