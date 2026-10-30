using System.Collections.Generic;
using ExitGames.Client.Photon;
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

public class SoldierSpawnEventCodes
{
    // Legacy-Hub: GameManager verteilt die Spawn-Events; Codes dort definiert.
}

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public class Soldier : MonoBehaviour
{
    private const float ArriveDistance = 0.08f;
    private const float AggroPadding = 4f;
    private const float TargetRescanInterval = 0.3f;
    private const float ChasePathRetryInterval = 0.5f;

    public static readonly List<Soldier> ActiveSoldiers = new List<Soldier>();

    // Beibehalten fuer Kompatibilitaet mit alten Codepfaden (werden nicht mehr fuer nette IDs verwendet)
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
    private GameObject healthBar;
    private Renderer healthBarRenderer;

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

    // ── Netzwerk ──────────────────────────────────────────────────────────
    /// <summary>Fremde Soldaten werden vom Client des Besitzers nicht simuliert (nur interpoliert).</summary>
    private bool isSimulated = true;
    /// <summary>true = Spawn ist zwar lokal, wird aber nicht simuliert (kommt vom Spawner-Helper).</summary>
    public bool PrepareVisualsOnly;

    private Vector3 syncTargetPosition;
    private float syncTargetHealth;
    private float syncSendTimer;
    private float lastSentHealth;
    private Vector3 lastSentPosition;
    private const float StateInterval = 0.15f;
    private const float StateMoveThreshold = 0.25f;

    // ── Suchtimer (Performance) ───────────────────────────────────────────
    private float targetRescanTimer;
    private float chasePathRetryTimer;
    private Soldier cachedEnemyTarget;
    private BuildingInstance cachedEnemyBuilding;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spearSprite == null) spearSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/speersoldat.png");
        if (shieldSprite == null) shieldSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/schildsoldat.png");
        if (swordSprite == null) swordSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/schwertkämpfer.png");
        if (bowSprite == null) bowSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/bogensoldat.png");
    }
#endif

    // ── Public Properties ─────────────────────────────────────────────────
    public bool IsOwnedByLocalPlayer
    {
        get
        {
            if (!Photon.Pun.PhotonNetwork.InRoom)
                return ownerActorNumber == 0 || ownerActorNumber == 1;
            return ownerActorNumber == Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber;
        }
    }

    /// <summary>Dieser Client entscheidet ueber Schaden/Tod des Soldaten.</summary>
    public bool IsAuthority => netId == 0 ? IsOwnedByLocalPlayer : NetCombat.OwnerFromId(netId) == LocalAuthorityActor();

    private static int LocalAuthorityActor()
    {
        return Photon.Pun.PhotonNetwork.InRoom && Photon.Pun.PhotonNetwork.LocalPlayer != null
            ? Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber : 1;
    }

    public bool IsSelected => isSelected;
    public Vector2 Position2D => transform.position;
    public float CurrentHealth => currentHealth;

    private void OnEnable()
    {
        if (!ActiveSoldiers.Contains(this))
        {
            ActiveSoldiers.Add(this);
        }
        NetCombat.RegisterSoldier(this);
    }

    private void OnDisable()
    {
        ActiveSoldiers.Remove(this);
        NetCombat.UnregisterSoldier(this);
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sortingOrder = 21;

        if (spearSprite == null) spearSprite = Resources.Load<Sprite>("Textures/speersoldat");
        if (shieldSprite == null) shieldSprite = Resources.Load<Sprite>("Textures/schildsoldat");
        if (swordSprite == null) swordSprite = Resources.Load<Sprite>("Textures/schwertkämpfer");
        if (bowSprite == null) bowSprite = Resources.Load<Sprite>("Textures/bogensoldat");

        if (ownerActorNumber == 0 && team == Team.Player)
        {
            ownerActorNumber = NetCombat.LocalActorId;
        }

        syncTargetPosition = transform.position;
        syncTargetHealth = -1f;

        ApplyStatsForType();
    }

    private void Start()
    {
        if (netId == 0)
        {
            netId = NetCombat.NextId(ownerActorNumber);
        }

        isSimulated = !PrepareVisualsOnly && IsOwnedByLocalPlayer;
        PrepareVisualsOnly = false;

        ApplyWeaponMaterialMultiplier();

        currentHealth = maxHealth;
        syncTargetHealth = currentHealth;

        ownerColor = PlayerColorUtility.GetColorForActor(ownerActorNumber, team == Team.Player);

        SetupVisuals();
        ApplyOwnerTint();
        UpdateHealthText();
        SetSelected(false);
    }

    // ── Netzwerk-Serialisierung (Event 12 Spawn / Event 15 State) ─────────
    public static object[] SerializeSpawnEvent(Soldier s)
    {
        return new object[]
        {
            s == null ? 0 : s.netId,
            (int)(s == null ? SoldierType.Spear : s.soldierType),
            s == null ? 0f : s.transform.position.x,
            s == null ? 0f : s.transform.position.y,
            s == null ? 0 : s.ownerActorNumber,
            (int)(s == null ? WeaponMaterial.Stone : s.weaponMaterial)
        };
    }

    public void SendStateReliable(object[] payload)
    {
        NetCombat.Raise(15, payload, true);
    }

    // wird vom GameManager (Event 15) aufgerufen
    public void ApplyNetState(object[] data)
    {
        if (data == null || data.Length < 4) return;
        float nx = (float)data[1];
        float ny = (float)data[2];
        float hp = (float)data[3];
        float newShield = data.Length > 4 ? (float)data[4] : shield;

        syncTargetPosition = new Vector3(nx, ny, transform.position.z);

        bool hpChanged = Mathf.Abs(hp - currentHealth) > 0.01f ||
                         Mathf.Abs(newShield - shield) > 0.01f;
        if (hpChanged)
        {
            float previous = currentHealth;
            currentHealth = hp;
            shield = newShield;
            currentHealth = Mathf.Max(0f, currentHealth);
            UpdateHealthText();

            // Netzcode hat entscheiddend: kein lokaler Tod-Broadcast (Authority macht das).
            if (previous > 0f && currentHealth <= 0f && !IsAuthority)
            {
                NetDestroy();
            }
        }
    }

    /// <summary>Nur fuer Netzvernichtung (Event 23): destruieren ohne Re-Broadcast.</summary>
    public void NetDestroy()
    {
        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.NotifySoldierDestroyed(this);
        }
        Destroy(gameObject);
    }

    // ── Simulationskern ───────────────────────────────────────────────────
    private void Update()
    {
        if (!isSimulated)
        {
            InterpolateRemote();
            return;
        }

        SimulateOwned();
        BroadcastState();
    }

    private void InterpolateRemote()
    {
        float distance = Vector2.Distance(transform.position, syncTargetPosition);
        if (distance > 3f)
        {
            transform.position = syncTargetPosition;
        }
        else if (distance > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, syncTargetPosition,
                Mathf.Max(moveSpeed * 1.5f, distance * 6f) * Time.deltaTime);
        }
    }

    private void SimulateOwned()
    {
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

        // Ziele alle 0.3s neu scannen statt JEDEM Frame (Performance!)
        targetRescanTimer -= Time.deltaTime;
        if (chasePathRetryTimer > 0f) chasePathRetryTimer -= Time.deltaTime;
        if (targetRescanTimer <= 0f)
        {
            targetRescanTimer = TargetRescanInterval;
            cachedEnemyTarget = FindPreferredEnemy();
            cachedEnemyBuilding = FindEnemyBuildingInRange(attackRange);
        }

        attackTarget = cachedEnemyTarget;
        attackBuildingTarget = cachedEnemyBuilding;

        CleanupTarget();

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
            if (distance <= attackRange + AggroPadding)
            {
                if (!MoveTowards(attackBuildingTarget.transform.position))
                {
                    // Direktlinie blockiert (Wasser/Steg-Ecken): Pfad ueber Stege suchen
                    RequestChasePath(attackBuildingTarget.transform.position);
                }
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

            if (distance <= attackRange + AggroPadding)
            {
                if (!MoveTowards(attackTarget.transform.position))
                {
                    // Direktlinie blockiert (Wasser/Steg-Ecken): Pfad ueber Stege suchen
                    RequestChasePath(attackTarget.transform.position);
                }
                return;
            }
        }

        hasReportedEnemyContact = false;
        FollowOrders();
    }

    private void CleanupTarget()
    {
        if (attackTarget != null &&
            (!attackTarget.gameObject.activeInHierarchy || attackTarget.currentHealth <= 0f))
        {
            attackTarget = null;
            cachedEnemyTarget = null;
        }

        if (attackBuildingTarget != null &&
            (!attackBuildingTarget.gameObject.activeInHierarchy || attackBuildingTarget.currentHP <= 0))
        {
            attackBuildingTarget = null;
            cachedEnemyBuilding = null;
        }
    }

    // ── Befehle ───────────────────────────────────────────────────────────
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
        if (!IsOwnedByLocalPlayer) return;
        hasPatrolOrder = false;
        if (patrolRenderer != null) patrolRenderer.enabled = false;
        attackMoveEnabled = false;
        attackTarget = null;
        attackBuildingTarget = null;
        SetPathTo(destination);
    }

    public void IssueAttackMoveOrder(Vector2 destination)
    {
        if (!IsOwnedByLocalPlayer) return;
        hasPatrolOrder = false;
        if (patrolRenderer != null) patrolRenderer.enabled = false;
        attackMoveEnabled = true;
        attackTarget = null;
        attackBuildingTarget = null;
        SetPathTo(destination);
    }

    public void IssueAttackOrder(Soldier target)
    {
        if (!IsOwnedByLocalPlayer || target == null) return;
        hasPatrolOrder = false;
        hasMoveOrder = false;
        if (patrolRenderer != null) patrolRenderer.enabled = false;
        attackMoveEnabled = true;
        attackTarget = target;
        attackBuildingTarget = null;
        SetPathTo(target.transform.position);
    }

    public void IssueAttackBuildingOrder(BuildingInstance target)
    {
        if (!IsOwnedByLocalPlayer || target == null) return;
        hasPatrolOrder = false;
        hasMoveOrder = false;
        if (patrolRenderer != null) patrolRenderer.enabled = false;
        attackMoveEnabled = true;
        attackBuildingTarget = target;
        attackTarget = null;
        SetPathTo(target.transform.position);
    }

    public void IssueObserveOrder(Vector2 pointA, Vector2 pointB)
    {
        if (!IsOwnedByLocalPlayer) return;
        attackMoveEnabled = true;
        hasPatrolOrder = true;
        patrolPointA = SnapToNearestPassable(pointA);
        patrolPointB = SnapToNearestPassable(pointB);
        patrolTowardsB = true;
        UpdatePatrolRenderer();
        SetPathTo(patrolPointB);
    }

    // ── Schaden / Tod ─────────────────────────────────────────────────────
    /// <summary>Lokale Schadensanwendung (nur Authority oder Empfaenger von Events).</summary>
    public void ApplyLocalDamage(float amount)
    {
        TakeDamage(amount);
    }

    private bool ConsumeDamage(float amount)
    {
        if (currentHealth <= 0f) return true;

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
        return false;
    }

    /// <summary>Lokaler Schaden. Stirbt der Soldat, broadcastet nur der Authority-Client.</summary>
    public void TakeDamage(float amount)
    {
        if (currentHealth <= 0f) return;

        ConsumeDamage(amount);
        currentHealth = Mathf.Max(0f, currentHealth);
        UpdateHealthText();

        if (currentHealth <= 0f)
        {
            DieAndBroadcast();
        }
    }

    private void DieAndBroadcast()
    {
        if (IsAuthority)
        {
            NetCombat.BroadcastSoldierDie(netId);
        }
        NetCombat.UnregisterSoldier(this);
        NetDestroy();
    }

    public void SetTeam(Team newTeam, int newOwnerActorNumber = 0)
    {
        team = newTeam;

        if (newOwnerActorNumber > 0)
        {
            ownerActorNumber = newOwnerActorNumber;
        }
        else if (team == Team.Player && ownerActorNumber == 0)
        {
            ownerActorNumber = NetCombat.LocalActorId;
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

    // ── Stats ─────────────────────────────────────────────────────────────
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

    // ── Visuals ───────────────────────────────────────────────────────────
    private void CreateHealthBar()
    {
        healthBar = GameObject.CreatePrimitive(PrimitiveType.Quad);
        healthBar.name = "HealthBar";
        healthBar.transform.SetParent(transform);
        healthBar.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        healthBar.transform.localScale = new Vector3(0.6f, 0.05f, 1f);
        Destroy(healthBar.GetComponent<MeshCollider>());
        healthBarRenderer = healthBar.GetComponent<Renderer>();
        healthBarRenderer.material = new Material(Shader.Find("Sprites/Default"));
        healthBarRenderer.sortingOrder = 11;
        healthBar.SetActive(false);
    }

    private void UpdateHealthBar()
    {
        if (healthBar == null || healthBarRenderer == null) return;

        if (currentHealth >= maxHealth)
        {
            healthBar.SetActive(false);
            return;
        }

        healthBar.SetActive(true);
        float ratio = currentHealth / maxHealth;
        healthBar.transform.localScale = new Vector3(0.6f * ratio, 0.05f, 1f);
        healthBar.transform.localPosition = new Vector3(-0.3f * (1f - ratio), 0.85f, 0f);

        if (ratio > 0.5f)
            healthBarRenderer.material.color = Color.green;
        else if (ratio > 0.25f)
            healthBarRenderer.material.color = Color.yellow;
        else
            healthBarRenderer.material.color = Color.red;
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

        CreateHealthBar();

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
        UpdateHealthBar();

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

    // ── Zielsuche (Listen-basiert, kein FindObjectsOfType!) ────────────────
    private Soldier FindPreferredEnemy()
    {
        float searchRange = attackRange + AggroPadding;
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

    private BuildingInstance FindEnemyBuildingInRange(float range)
    {
        // Statikliste statt FindObjectsOfType pro Frame
        BuildingInstance.AllBuildings.RemoveAll(b => b == null);

        BuildingInstance closest = null;
        float closestDist = float.MaxValue;

        for (int i = 0; i < BuildingInstance.AllBuildings.Count; i++)
        {
            BuildingInstance b = BuildingInstance.AllBuildings[i];
            if (b == null || !b.gameObject.activeInHierarchy) continue;

            bool isEnemy = team == Team.Player ? !b.isLocal : b.isLocal;
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

    public bool IsHostileTo(Soldier other)
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

    // ── Bewegung ──────────────────────────────────────────────────────────
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

    private bool MoveTowards(Vector2 targetPos)
    {
        Vector2 next = Vector2.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        Vector2Int nextGrid = new Vector2Int(Mathf.RoundToInt(next.x), Mathf.RoundToInt(next.y));
        if (!BuildingManager.IsWalkable(nextGrid)) return false;
        transform.position = new Vector3(next.x, next.y, transform.position.z);
        return true;
    }

    // Ruft SetPathTo nur gedrosselt auf, damit die Verfolgung beim blockierten Direktweg
    // ueber die Steg-Kette neu geroutet wird und Soldaten zuegig Stege benutzen.
    private void RequestChasePath(Vector2 targetPos)
    {
        if (chasePathRetryTimer > 0f) return;
        chasePathRetryTimer = ChasePathRetryInterval;
        hasMoveOrder = false;
        SetPathTo(targetPos);
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

        // Schaden netzwerkfaehig: Authority des Ziels entscheidet ueber HP.
        NetCombat.ApplySoldierDamage(enemySoldier, damage);
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

        if (team == Team.Player && building.isLocal)
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

    // ── Netz-Synchronisation (Event 15) ───────────────────────────────────
    private void BroadcastState()
    {
        if (!Photon.Pun.PhotonNetwork.InRoom) return;

        Vector3 pos = transform.position;
        bool moved = (pos - lastSentPosition).sqrMagnitude >= StateMoveThreshold * StateMoveThreshold;
        bool hpChanged = Mathf.Abs(currentHealth - lastSentHealth) > 0.01f;

        if (!moved && !hpChanged) return;

        syncSendTimer += Time.deltaTime;
        if (syncSendTimer < StateInterval) return;
        syncSendTimer = 0f;

        lastSentPosition = pos;
        lastSentHealth = currentHealth;

        object[] data = new object[] { netId, pos.x, pos.y, currentHealth, shield };
        NetCombat.Raise(15, data, false);
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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
