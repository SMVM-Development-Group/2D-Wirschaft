using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;

public class Villager : MonoBehaviour
{
    public enum Role { Villager, Worker }
    public Role role = Role.Villager;
    
    public float moveSpeed = 1.5f;
    private Vector2 targetPosition;
    private bool isMoving = false;
    private readonly List<Vector2> currentPath = new List<Vector2>();
    private int currentPathIndex = 0;
    private BuildingInstance assignedBuilding;
    public BuildingInstance AssignedBuilding => assignedBuilding;
    private BuildingInstance assignedSleepHouse;
    
    [Header("Ship Assignment")]
    public Ship assignedShip;  // Reference to ship if assigned as crew
    
    [HideInInspector]
    public bool isLocal = true;
    [HideInInspector]
    public bool isOperatingWorker = false;
    [HideInInspector]
    public float stamina = 100f;

    // ── Netzwerk ─────────────────────────────────────────────────────────
    [HideInInspector] public int villagerId;          // 0 = noch nicht vergeben (Besitzer steckt in der ID)
    [HideInInspector] public int ownerActorNumber;
    /// <summary>Nur der Besitzer simuliert KI/Mood/Zufall. Remote-Villager sind nur Positions-Proxys.</summary>
    [HideInInspector] public bool isSimulated = true;

    private Vector2 netTargetPosition;
    private bool netHasTarget;
    private bool visibleToNetwork = true;

    /// <summary>Stabile netzwerkweite ID. Wird für lokale Villager faul vergeben (Besitzer = actor*1M+n).</summary>
    public int VillagerNetworkId
    {
        get
        {
            if (villagerId == 0 && isLocal)
            {
                villagerId = NetCombat.NextId(ownerActorNumber > 0 ? ownerActorNumber : NetCombat.LocalActorId);
            }
            return villagerId;
        }
    }

    /// <summary>Gibt an, ob der Villager sichtbar ist (Schlafen im Haus = unsichtbar) – für den Netzwerk-Snapshot.</summary>
    public bool IsVisibleToNetwork => visibleToNetwork;

    /// <summary>Empfangene Netzwerk-Position anwenden (nur für Remote-Villager).</summary>
    public void ApplyNetworkState(float x, float y, bool visible, Villager.Role netRole)
    {
        role = netRole;
        netTargetPosition = new Vector2(x, y);
        netHasTarget = true;
        SetVisibility(visible);
        // Role-Farbe ohne Simulation übernehmen
        ApplyRoleColor();
    }
    [HideInInspector]
    public float mood = 80f; // Villager Mood: starts at 80%
    private float workActionCooldown = 0f;

    private SpriteRenderer sr;
    private Renderer rend;

    private void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        rend = GetComponent<Renderer>();
        
        // Ensure they are visible and on top
        if (sr != null) sr.sortingOrder = 20;

        if (!isSimulated || !isLocal)
        {
            // Remote-Proxy: keine Zufalls-KI, keine Pfadsuche.
            // Gespeicherte Sichtbarkeit aus dem Netz-Snapshot anwenden (nicht überschreiben!).
            if (sr != null) sr.enabled = visibleToNetwork;
            else if (rend != null) rend.enabled = visibleToNetwork;
            return;
        }

        Wander();
    }

    private void Update()
    {
        // Remote-Villager: nur interpolieren, sonst NICHTS (Zufall/KI/Authentizität bleibt beim Besitzer)
        if (!isSimulated)
        {
            UpdateRemoteProxy();
            return;
        }

        // Erhöhte Sterberate für alle bei schlechtem Mood
        if (VillagerManager.Instance != null)
        {
            float gMood = VillagerManager.Instance.globalMood;
            if (gMood < 45f)
            {
                // Sickness chance: higher when mood is closer to 0 (balanced to not be too extreme)
                float sicknessIntensity = 1f - (gMood / 45f); // 0.0 to 1.0
                if (Random.value < Time.deltaTime * 0.00012f * sicknessIntensity)
                {
                    DieOfSickness();
                    return;
                }
            }

            // Food availability mood scaling
            float foodEffect = VillagerManager.Instance.GetFoodMoodEffect();
            if (foodEffect > 0f)
                mood = Mathf.Min(100f, mood + foodEffect * Time.deltaTime);
            else if (foodEffect < 0f)
                mood = Mathf.Max(0f, mood + foodEffect * Time.deltaTime);
        }

        if (isMoving)
        {
            // Apply mood visual modifier to move speed: happy runs fast, sad walks slowly!
            float speedMod = 1.0f;
            if (mood > 80f) speedMod = 1.25f;
            else if (mood < 30f) speedMod = 0.7f;

            if (currentPath.Count > 0 && currentPathIndex < currentPath.Count)
            {
                Vector2 waypoint = currentPath[currentPathIndex];
                Vector2 next = Vector2.MoveTowards(transform.position, waypoint, moveSpeed * speedMod * Time.deltaTime);
                transform.position = new Vector3(next.x, next.y, transform.position.z);

                if (Vector2.Distance(transform.position, waypoint) < 0.1f)
                {
                    currentPathIndex++;
                }
            }
            else
            {
                Vector2 next = Vector2.MoveTowards(transform.position, targetPosition, moveSpeed * speedMod * Time.deltaTime);
                transform.position = new Vector3(next.x, next.y, transform.position.z);
            }

            if (currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
            {
                if (Vector2.Distance(transform.position, targetPosition) < 0.1f)
                {
                    OnReachedTarget();
                }
            }
        }
        else
        {
            if (isOperatingWorker && assignedBuilding != null)
            {
                float hour = DayNightManager.Instance != null ? DayNightManager.Instance.currentHour : 12f;
                bool isWorkTime = assignedBuilding.IsCurrentlyWorkTime();

                if (isWorkTime)
                {
                    ReleaseSleepSlot();
                    // WORK TIME: stay close, work, consume stamina if night
                    SetVisibility(true);

                    // Restore color tint
                    if (sr != null) sr.color = new Color(0.7f, 1f, 0.7f, 1f);

                    // Stamina drain if forced to work at night
                    if (hour >= 18f || hour < 6f)
                    {
                        stamina = Mathf.Max(0f, stamina - Time.deltaTime * 2.0f);
                        if (stamina <= 0f && Random.value < Time.deltaTime * 0.02f)
                        {
                            DieOfExhaustion();
                            return;
                        }
                    }

                    // VERY SLOW Mood changes during active work hours (adjusted to be more challenging):
                    if (assignedBuilding.currentSchedule == BuildingInstance.ScheduleMode.Leisure)
                    {
                        // Leisure shift slowly increases mood
                        mood = Mathf.Min(100f, mood + Time.deltaTime * 0.07f);
                    }
                    else if (assignedBuilding.currentSchedule == BuildingInstance.ScheduleMode.DayOnly)
                    {
                        // Regular day shift is mildly tiring: decays slowly to a baseline of 25%
                        mood = Mathf.Max(25f, mood - Time.deltaTime * 0.04f);
                    }
                    else if (assignedBuilding.currentSchedule == BuildingInstance.ScheduleMode.Continuous)
                    {
                        // 24/7 night-shift work decays mood slowly but surely to 0%
                        mood = Mathf.Max(0f, mood - Time.deltaTime * 0.22f);
                    }

                    // Active working actions
                    if (workActionCooldown > 0f)
                    {
                        workActionCooldown -= Time.deltaTime;
                    }
                    else
                    {
                        PerformWorkAction(0.25f); // Tight work inside
                    }

                    // Occasionally work animation
                    if (Random.value < 0.005f)
                    {
                        StartCoroutine(WorkAnimationRoutine());
                    }
                }
                else
                {
                    // NOT WORK TIME: Either sleep or leisure
                    bool isLeisureTime = hour >= 12f && hour < 15f && assignedBuilding.currentSchedule == BuildingInstance.ScheduleMode.Leisure;

                    if (isLeisureTime)
                    {
                        ReleaseSleepSlot();
                        // LEISURE TIME: wander freely and recover stamina & mood slowly
                        SetVisibility(true);
                        
                        // Restore color tint
                        if (sr != null) sr.color = new Color(0.7f, 1f, 0.7f, 1f);

                        stamina = Mathf.Min(100f, stamina + Time.deltaTime * 3.0f);
                        mood = Mathf.Min(100f, mood + Time.deltaTime * 0.16f); // Slow leisure recovery (was 0.22f)

                        if (workActionCooldown > 0f)
                        {
                            workActionCooldown -= Time.deltaTime;
                        }
                        else
                        {
                            PerformWorkAction(4.5f); // Wide stroll around building
                        }
                    }
                    else
                    {
                        // SLEEP TIME: go to nearest house with a free bed
                        BuildingInstance sleepHouse = FindSleepHouse();

                        if (sleepHouse != null)
                        {
                            float dist = Vector2.Distance(transform.position, sleepHouse.transform.position);
                            if (dist < 0.4f)
                            {
                                if (!sleepHouse.TryRegisterSleeper(this))
                                {
                                    ReleaseSleepSlot();
                                    return;
                                }

                                assignedSleepHouse = sleepHouse;
                                SetVisibility(false);
                                stamina = Mathf.Min(100f, stamina + Time.deltaTime * 5.0f);

                                float sleepMoodRecovery = GetSleepMoodRecovery(sleepHouse);
                                mood = Mathf.Min(100f, mood + Time.deltaTime * sleepMoodRecovery);
                            }
                            else
                            {
                                SetVisibility(true);
                                targetPosition = sleepHouse.transform.position;
                                SetPathTo(targetPosition);
                                isMoving = true;
                            }
                        }
                        else
                        {
                            ReleaseSleepSlot();
                            // Sleep outside when all houses are full
                            SetVisibility(true);
                            if (sr != null) sr.color = new Color(0.7f, 1f, 0.7f, 0.4f);
                            stamina = Mathf.Min(100f, stamina + Time.deltaTime * 3.5f);
                            mood = Mathf.Min(100f, mood + Time.deltaTime * 0.08f);
                        }
                    }
                }
            }
            else if (assignedBuilding == null)
            {
                // Unemployed: ensure visible and recover stamina & mood slowly
                SetVisibility(true);
                stamina = Mathf.Min(100f, stamina + Time.deltaTime * 1.5f);
                mood = Mathf.Min(100f, mood + Time.deltaTime * 0.04f); // happy just wandering slowly (was 0.06f)

                // Just idle wander, much less frequent
                if (Random.value < 0.002f) Wander();
            }
        }

        // Linear mood stabilizer (regression to 50% baseline):
        // The higher the mood, the easier it sinks (negative force).
        // The lower the mood, the easier it rises (positive force).
        float moodStabilizerForce = (50f - mood) * 0.003f;
        mood = Mathf.Clamp(mood + moodStabilizerForce * Time.deltaTime, 0f, 100f);
    }

    public void AssignToBuild(BuildingInstance building, Vector2 offset)
    {
        assignedBuilding = building;
        targetPosition = (Vector2)building.transform.position + offset;
        SetPathTo(targetPosition);
        isMoving = true;
        Debug.Log($"[Villager] Assigned to {building.data.buildingName}. New Target: {targetPosition}");
    }

    public bool IsBusy() => assignedBuilding != null || isOperatingWorker;

    public void AssignAsOperatingWorker(BuildingInstance building)
    {
        isOperatingWorker = true;
        assignedBuilding = building;
        // Keep them strictly inside the building's visual boundaries (radius 0.25f)
        targetPosition = (Vector2)building.transform.position + Random.insideUnitCircle * 0.25f;
        isMoving = true;
        workActionCooldown = Random.Range(3f, 7f);
        
        if (sr != null) sr.color = new Color(0.7f, 1f, 0.7f); // Light green tint for active workers
        else if (rend != null) rend.material.color = new Color(0.7f, 1f, 0.7f);
        
        Debug.Log($"[Villager] Assigned as operating worker to {building.data.buildingName}. New Target: {targetPosition}");
    }

    private void PerformWorkAction()
    {
        PerformWorkAction(0.25f);
    }

    private void PerformWorkAction(float radius)
    {
        if (assignedBuilding == null) return;
        
        targetPosition = (Vector2)assignedBuilding.transform.position + Random.insideUnitCircle * radius;
        isMoving = true;
        
        // Random cooldown before moving/working again
        workActionCooldown = Random.Range(3f, 7f);
    }

    private IEnumerator WorkAnimationRoutine()
    {
        float elapsed = 0f;
        float duration = 1.0f; // 1 second of active working action
        Vector3 basePos = transform.position;
        
        while (elapsed < duration)
        {
            if (isMoving) 
            {
                transform.rotation = Quaternion.identity;
                yield break;
            }
            elapsed += Time.deltaTime;
            
            // 1. Shaking left and right (tilting)
            float tiltAngle = Mathf.Sin(elapsed * 25f) * 12f; // Fast tilt
            transform.rotation = Quaternion.Euler(0, 0, tiltAngle);
            
            // 2. Tiny bounce up and down
            float yOffset = Mathf.Abs(Mathf.Sin(elapsed * 12f)) * 0.15f;
            transform.position = new Vector3(basePos.x, basePos.y + yOffset, basePos.z);
            
            yield return null;
        }
        
        // Reset to normal state
        if (!isMoving)
        {
            transform.position = basePos;
            transform.rotation = Quaternion.identity;
        }
    }

    private void Wander()
    {
        // Try to find a nearby land cell so villagers never walk into the ocean
        Vector2 currentPos = transform.position;
        for (int attempt = 0; attempt < 15; attempt++)
        {
            Vector2 candidate = currentPos + Random.insideUnitCircle * 5f;
            candidate = new Vector2(Mathf.Round(candidate.x), Mathf.Round(candidate.y));
            if (IslandManager.IsLand(candidate))
            {
                targetPosition = candidate;
                SetPathTo(targetPosition);
                isMoving = true;
                return;
            }
        }
        // No valid land cell found nearby – stay in place
        currentPath.Clear();
        isMoving = false;
    }

    public void FindWork()
    {
        BuildingInstance target = FindNearestOpportunity();
        if (target != null)
        {
            targetPosition = target.transform.position;
            SetPathTo(targetPosition);
            isMoving = true;
            assignedBuilding = target;
        }
        else
        {
            Debug.Log("Kein Job oder Kaserne gefunden!");
        }
    }

    private void SetPathTo(Vector2 destination)
    {
        Vector2 startGrid = new Vector2(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));
        Vector2 destGrid = new Vector2(Mathf.Round(destination.x), Mathf.Round(destination.y));
        
        // Snap destination to nearest passable point
        Vector2 snappedDest = SnapToNearestPassable(destGrid);
        
        List<Vector2> path = BuildingManager.FindPath(startGrid, snappedDest);

        currentPath.Clear();
        currentPathIndex = 0;

        if (path.Count > 0)
        {
            currentPath.AddRange(path);
            targetPosition = snappedDest;
            isMoving = true;
        }
        else
        {
            currentPath.Clear();
            isMoving = false;
            NotificationManager.Instance?.Notify("no_path_villager",
                "Kein Weg! Der Dorfbewohner kann nur auf Land gehen.", 4f);
        }
    }

    private Vector2 SnapToNearestPassable(Vector2 target)
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

    private BuildingInstance FindNearestOpportunity()
    {
        BuildingInstance nearest = null;
        float minDist = float.MaxValue;

        for (int i = 0; i < BuildingInstance.AllBuildings.Count; i++)
        {
            BuildingInstance h = BuildingInstance.AllBuildings[i];
            if (h == null) continue;

            if (h.IsConstructed() && (h.data.isWorkerHub || h.data.isBarracks))
            {
                float d = Vector2.Distance(transform.position, h.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    nearest = h;
                }
            }
        }
        return nearest;
    }

    private void OnReachedTarget()
    {
        isMoving = false;
        if (assignedBuilding != null)
        {
            if (isOperatingWorker)
            {
                // Operating workers just stay at the building
                return;
            }
            
            if (assignedBuilding.data.isWorkerHub)
            {
                PromoteToWorker();
            }
            else if (assignedBuilding.data.isBarracks)
            {
                if (assignedBuilding.IsConstructed())
                {
                    PromoteToSoldier();
                }
                else
                {
                    assignedBuilding.NotifyWorkerArrived(this);
                }
            }
            else
            {
                assignedBuilding.NotifyWorkerArrived(this);
            }
        }
    }

    private void PromoteToWorker()
    {
        role = Role.Worker;
        assignedBuilding = null;
        if (sr != null) sr.color = Color.orange;
        else if (rend != null) rend.material.color = Color.orange;
        Wander();
    }

    private void PromoteToSoldier()
    {
        if (Player_UI.Instance == null) return;
        
        int current = Player_UI.Instance.GetResource("soldaten");
        int max = Player_UI.Instance.GetMaxResource("soldaten");
        
        if (current < max)
        {
            // Zufall NUR hier,wo der Besitzer simuliert (Authority-Prinzip)
            SoldierType sType = (SoldierType)Random.Range(0, 4);
            WeaponMaterial sMaterial = (WeaponMaterial)Random.Range(0, 4);
            Debug.Log($"[Villager] Promoting to Soldier. Type: {sType}, Material: {sMaterial}");
            Player_UI.Instance.AddResource("soldaten", 1);
            Player_UI.Instance.AddResource("dorfbewohner", -1);
            
            if (VillagerManager.Instance != null)
            {
                VillagerManager.Instance.NotifyVillagerConverted(this);
            }

            int owner = PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null
                ? PhotonNetwork.LocalPlayer.ActorNumber
                : NetCombat.LocalActorId;

            // Transform into Soldier — zentral über NetCombat (spawnt selbst + broadcastet Event 12)
            Vector3 spawnPos = transform.position;
            Destroy(gameObject);
            NetCombat.SpawnSoldier(sType, sMaterial, spawnPos, owner, true);
        }
        else
        {
            Debug.Log("Soldaten-Limit erreicht!");
            assignedBuilding = null;
            Wander();
        }
    }

    private void OnMouseDown()
    {
        if (!isLocal) return;

        // Simple selection: Click to tell them to find work (only free villagers)
        if (role == Role.Villager && !isOperatingWorker)
        {
            AudioManager.Instance?.PlaySelectSound();
            FindWork();
        }
    }

    public void Release()
    {
        isOperatingWorker = false;
        assignedBuilding = null;
        isMoving = false;
        
        // ONLY reset to Villager if they are NOT a Worker!
        // A construction worker should stay a construction worker so they can build more things.
        if (role != Role.Worker)
        {
            role = Role.Villager;
        }

        transform.rotation = Quaternion.identity; // Reset rotation!
        SetVisibility(true); // Ensure visible!
        
        // Keep orange color for workers, white for normal villagers
        if (sr != null) sr.color = (role == Role.Worker) ? Color.orange : Color.white;
        else if (rend != null) rend.material.color = (role == Role.Worker) ? Color.orange : Color.white;
        
        Wander();
    }

    private void SetVisibility(bool visible)
    {
        visibleToNetwork = visible;
        if (sr != null) sr.enabled = visible;
        else if (rend != null) rend.enabled = visible;
    }

    /// <summary>Remote-Interpolation: sanft zur letzten Netzwerk-Position bewegen.</summary>
    private void UpdateRemoteProxy()
    {
        if (!netHasTarget) return;
        Vector2 current = transform.position;
        Vector2 next = Vector2.MoveTowards(current, netTargetPosition, moveSpeed * Time.deltaTime);
        transform.position = new Vector3(next.x, next.y, transform.position.z);
    }

    /// <summary>Rolle konsistent einfärben (auch für Remote-Proxy ohne Simulation).</summary>
    private void ApplyRoleColor()
    {
        Color c = (role == Role.Worker) ? Color.orange : Color.white;
        if (sr != null) sr.color = c;
        else if (rend != null) rend.material.color = c;
    }

    public struct MoodRateSnapshot
    {
        public float workRatePerSecond;
        public float housingRatePerSecond;
        public bool isSleepingInHouse;
        public bool isSleepingHomeless;
    }

    /// <summary>Aktuelle Stimmungsänderung durch Arbeit/Schlaf (Punkte pro Sekunde, 0–100-Skala).</summary>
    public MoodRateSnapshot GetMoodRateSnapshot()
    {
        MoodRateSnapshot snapshot = new MoodRateSnapshot();
        float hour = DayNightManager.Instance != null ? DayNightManager.Instance.currentHour : 12f;

        if (isOperatingWorker && assignedBuilding != null)
        {
            if (assignedBuilding.IsCurrentlyWorkTime())
            {
                switch (assignedBuilding.currentSchedule)
                {
                    case BuildingInstance.ScheduleMode.Leisure:
                        snapshot.workRatePerSecond = 0.07f;
                        break;
                    case BuildingInstance.ScheduleMode.DayOnly:
                        snapshot.workRatePerSecond = -0.04f;
                        break;
                    case BuildingInstance.ScheduleMode.Continuous:
                        snapshot.workRatePerSecond = -0.22f;
                        break;
                }
            }
            else
            {
                bool isLeisureTime = hour >= 12f && hour < 15f &&
                    assignedBuilding.currentSchedule == BuildingInstance.ScheduleMode.Leisure;

                if (isLeisureTime)
                {
                    snapshot.workRatePerSecond = 0.16f;
                }
                else
                {
                    BuildingInstance sleepHouse = assignedSleepHouse != null ? assignedSleepHouse : FindSleepHouse();
                    if (sleepHouse != null)
                    {
                        float dist = Vector2.Distance(transform.position, sleepHouse.transform.position);
                        if (dist < 0.4f && sleepHouse.HasFreeSleepSlot(this))
                        {
                            snapshot.isSleepingInHouse = true;
                            snapshot.housingRatePerSecond = GetSleepMoodRecovery(sleepHouse);
                        }
                    }
                    else
                    {
                        snapshot.isSleepingHomeless = true;
                        snapshot.housingRatePerSecond = 0.08f;
                    }
                }
            }
        }

        return snapshot;
    }

    private float GetSleepMoodRecovery(BuildingInstance sleepHouse)
    {
        if (sleepHouse == null) return 0.08f;
        if (sleepHouse.GetSleepCapacity() >= 5) return 0.14f;
        if (sleepHouse.data != null && sleepHouse.data.buildingName.Contains("Groß")) return 0.22f;
        return 0.12f;
    }

    private void ReleaseSleepSlot()
    {
        if (assignedSleepHouse != null)
        {
            assignedSleepHouse.UnregisterSleeper(this);
            assignedSleepHouse = null;
        }
    }

    public void ClearSleepHouseReference()
    {
        assignedSleepHouse = null;
    }
    
    // ── Ship Assignment (for Crew) ─────────────────────────────────────────────
    
    public void AssignToShip(Ship ship)
    {
        if (ship == null) return;
        
        // Release from any current assignment
        Release();
        
        assignedShip = ship;
        
        // Visual feedback - different color for ship crew
        if (sr != null) sr.color = new Color(0.6f, 0.8f, 1f, 1f); // Light blue for ship crew
        else if (rend != null) rend.material.color = new Color(0.6f, 0.8f, 1f, 1f);
        
        Debug.Log($"[Villager] Assigned as crew to ship {ship.GetShipName()}");
    }
    
    public void ReleaseFromShip()
    {
        if (assignedShip != null)
        {
            Debug.Log($"[Villager] Released from ship {assignedShip.GetShipName()}");
            assignedShip = null;
        }
        
        // Reset to normal color
        if (sr != null) sr.color = (role == Role.Worker) ? Color.orange : Color.white;
        else if (rend != null) rend.material.color = (role == Role.Worker) ? Color.orange : Color.white;
    }
    
    public bool IsOnShip()
    {
        return assignedShip != null;
    }
    
    public Ship GetAssignedShip()
    {
        return assignedShip;
    }

    private BuildingInstance FindSleepHouse()
    {
        if (assignedSleepHouse != null && assignedSleepHouse.IsConstructed() && assignedSleepHouse.HasFreeSleepSlot(this))
        {
            return assignedSleepHouse;
        }

        BuildingInstance nearestHouse = null;
        float minDist = float.MaxValue;

        for (int i = 0; i < BuildingInstance.AllBuildings.Count; i++)
        {
            BuildingInstance b = BuildingInstance.AllBuildings[i];
            if (b == null) continue;

            if (!b.ProvidesSleep || !b.IsConstructed() || !b.HasFreeSleepSlot(this))
            {
                continue;
            }

            float dist = Vector2.Distance(transform.position, b.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearestHouse = b;
            }
        }

        return nearestHouse;
    }

    private void DieOfExhaustion()
    {
        Debug.LogWarning($"[Villager] Ein Dorfbewohner ist vor Erschöpfung gestorben!");
        
        // Spawn a cute floating warning text above them!
        GameObject textGO = new GameObject("ExhaustionText");
        textGO.transform.position = transform.position + Vector3.up * 0.5f;
        var textMesh = textGO.AddComponent<TextMesh>();
        textMesh.text = "☠ Erschöpft gestorben!";
        textMesh.fontSize = 18;
        textMesh.characterSize = 0.08f;
        textMesh.color = Color.red;
        textMesh.alignment = TextAlignment.Center;
        textMesh.anchor = TextAnchor.MiddleCenter;
        
        // Add a simple translation over time
        textGO.AddComponent<SelfMovingText>();
        
        NotificationManager.Instance?.Notify("villager_death", "Ein Villager ist gestorben.", 4f);
        Destroy(textGO, 2f);
        
        if (assignedBuilding != null)
        {
            assignedBuilding.RemoveOperatingWorker(this);
        }
        
        if (VillagerManager.Instance != null)
        {
            VillagerManager.Instance.NotifyVillagerConverted(this);
        }

        ReleaseSleepSlot();
        Destroy(gameObject);
    }

    private void DieOfSickness()
    {
        Debug.LogWarning($"[Villager] {gameObject.name} ist an Stress/Krankheit gestorben!");
        
        // Spawn a cute floating warning text above them!
        GameObject textGO = new GameObject("SicknessText");
        textGO.transform.position = transform.position + Vector3.up * 0.5f;
        var textMesh = textGO.AddComponent<TextMesh>();
        textMesh.text = "☠ An Stress gestorben!";
        textMesh.fontSize = 18;
        textMesh.characterSize = 0.08f;
        textMesh.color = new Color(0.9f, 0.1f, 0.1f); // red
        textMesh.alignment = TextAlignment.Center;
        textMesh.anchor = TextAnchor.MiddleCenter;
        
        // Float upwards
        textGO.AddComponent<SelfMovingText>();
        
        NotificationManager.Instance?.Notify("villager_death", "Ein Villager ist gestorben.", 4f);
        Destroy(textGO, 2.5f);

        // Remove from work
        if (assignedBuilding != null)
        {
            assignedBuilding.RemoveOperatingWorker(this);
        }
        
        if (VillagerManager.Instance != null)
        {
            VillagerManager.Instance.NotifyVillagerConverted(this);
        }

        ReleaseSleepSlot();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        ReleaseSleepSlot();
    }
}

// Tiny helper to float the death text upward
public class SelfMovingText : MonoBehaviour
{
    private void Update()
    {
        transform.Translate(Vector3.up * Time.deltaTime * 0.6f);
    }
}
