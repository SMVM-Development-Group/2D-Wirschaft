using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using ExitGames.Client.Photon;
using Photon.Realtime;

public class BuildingInstance : MonoBehaviour
{
    public BuildingData data;
    public bool isLocal;
    [HideInInspector] public int footprintWidthOverride;
    [HideInInspector] public int footprintHeightOverride;

    [Header("Health")]
    public int maxHP = 100;
    public int currentHP = 100;
    private GameObject healthBar;
    private const float HealthBarWidth = 0.8f;
    private const float HealthBarHeight = 0.08f;
    private const byte BuildingCaptureEventCode = 17;
    private const float HealthBarY = -0.5f;
    
    private bool isConstructed = false;
    private float constructionProgress = 0f;
    private Renderer[] renderers;
    private FogRevealer revealer;

    // ── Produktion ────────────────────────────────────────────────────────
    public bool IsProductionPaused { get; private set; } = false;
    public int  TotalProduced      { get; private set; } = 0;
    public float ProductionProgress { get; private set; } = 0f;
    private float productionTimer = 0f;

    // ── Schedule ─────────────────────────────────────────────────────────
    public enum ScheduleMode { Continuous, DayOnly, Leisure }
    public ScheduleMode currentSchedule = ScheduleMode.DayOnly;
    
    [HideInInspector]
    public bool isBuilderHut = false;

    // ── Barracks Settings & Recruitment ────────────────────────────────────
    [Header("Barracks settings")]
    public bool spearSelected = true;
    public bool shieldSelected = true;
    public bool swordSelected = true;
    public bool bowSelected = true;

    [Header("Barracks Cost Settings")]
    public int woodSoldierCost = 5;
    public int stoneSoldierCost = 5;
    public int goldSoldierCost = 5;
    public int ironSoldierCost = 5;

    public enum BarracksResource { Wood, Stone, Gold, Iron }
    public BarracksResource selectedResource = BarracksResource.Stone;

    public bool autoRecruit = false;
    
    public Queue<SoldierType> recruitQueue = new Queue<SoldierType>();
    public Queue<BarracksResource> recruitResourceQueue = new Queue<BarracksResource>();
    public SoldierType currentRecruitingType;
    private BarracksResource currentRecruitingResource;
    private float recruitmentTimer = 0f;
    private const float RECRUIT_INTERVAL = 8f;

    public bool IsConstructed() => isConstructed;

    private int workersArrived = 0;
    private int workersAssigned = 0;
    private List<Villager> assignedWorkers = new List<Villager>();
    private List<Villager> operatingWorkers = new List<Villager>();

    [HideInInspector] public bool isPreBuiltLodging = false;
    [HideInInspector] public string displayNameOverride = "";
    [HideInInspector] public int sleepCapacityOverride = 0;
    private readonly List<Villager> sleepingVillagers = new List<Villager>();

    public string GetDisplayName()
    {
        if (!string.IsNullOrEmpty(displayNameOverride)) return displayNameOverride;
        return data != null ? data.buildingName : gameObject.name;
    }

    public int GetSleepCapacity()
    {
        if (sleepCapacityOverride > 0) return sleepCapacityOverride;
        if (data == null) return 0;
        if (data.sleepCapacity > 0) return data.sleepCapacity;
        if (data.productionResourceId == "bevolkerung")
        {
            return data.buildingName.Contains("Groß") ? 4 : 2;
        }
        return 0;
    }

    public bool ProvidesSleep => GetSleepCapacity() > 0;

    public int GetSleepingCount()
    {
        sleepingVillagers.RemoveAll(v => v == null);
        return sleepingVillagers.Count;
    }

    public bool HasFreeSleepSlot(Villager villager)
    {
        if (!ProvidesSleep || !IsConstructed()) return false;
        sleepingVillagers.RemoveAll(v => v == null);
        if (villager != null && sleepingVillagers.Contains(villager)) return true;
        return sleepingVillagers.Count < GetSleepCapacity();
    }

    public bool TryRegisterSleeper(Villager villager)
    {
        if (villager == null || !ProvidesSleep || !IsConstructed()) return false;
        sleepingVillagers.RemoveAll(v => v == null);
        if (sleepingVillagers.Contains(villager)) return true;
        if (sleepingVillagers.Count >= GetSleepCapacity()) return false;
        sleepingVillagers.Add(villager);
        return true;
    }

    public void UnregisterSleeper(Villager villager)
    {
        if (villager == null) return;
        sleepingVillagers.Remove(villager);
    }

    private void Update()
    {
        if (data == null) return;
        if (!isLocal) return;

        UpdateHealthBar();

        if (isConstructed && operatingWorkers.Count < data.workersNeeded)
        {
            TryHireOperatingWorkers();
        }

        // Handle Barracks recruitment if this is a constructed barracks
        if (isConstructed && data.isBarracks)
        {
            UpdateBarracksRecruitment();
        }

        // Production timer logic (pauses during night / when understaffed / when paused)
        if (isConstructed && !IsProductionPaused)
        {
            // Do not run standard production if this is a barracks (barracks only recruits soldiers)
            bool canProduce = (!string.IsNullOrEmpty(data.productionResourceId) || data.producesVillagers) && !data.isBarracks;
            if (canProduce)
            {
                if (operatingWorkers.Count >= data.workersNeeded && IsCurrentlyWorkTime())
                {
                    productionTimer += Time.deltaTime;
                    float interval = data.productionInterval > 0f ? data.productionInterval : 1f;
                    ProductionProgress = Mathf.Clamp01(productionTimer / interval);

                    if (productionTimer >= interval)
                     {
                         productionTimer = 0f;
                         ProductionProgress = 0f;
                         TriggerProductionCycle();
                     }
                }
                else
                {
                    // Freeze progress! Do not wipe the production timer or progress to 0.
                    // This allows workers to resume exactly where they left off when returning from breaks or sleep.
                }
            }
            else
            {
                ProductionProgress = 0f;
            }
        }
        else
        {
            ProductionProgress = 0f;
        }
    }

    public void TryHireOperatingWorkers()
    {
        if (VillagerManager.Instance == null) return;

        while (operatingWorkers.Count < data.workersNeeded)
        {
            Villager v = VillagerManager.Instance.GetAvailableVillager();
            if (v != null)
            {
                v.AssignAsOperatingWorker(this);
                operatingWorkers.Add(v);
            }
            else
            {
                break; 
            }
        }
    }

    public bool IsCurrentlyWorkTime()
    {
        if (!isConstructed || IsProductionPaused) return false;
        if (DayNightManager.Instance == null) return true;

        float hour = DayNightManager.Instance.currentHour;

        switch (currentSchedule)
        {
            case ScheduleMode.Continuous:
                return true;

            case ScheduleMode.DayOnly:
                return hour >= 6f && hour < 18f;

            case ScheduleMode.Leisure:
                bool isDay = hour >= 6f && hour < 18f;
                bool isLeisureTime = hour >= 12f && hour < 15f;
                return isDay && !isLeisureTime;

            default:
                return true;
        }
    }

    public void RemoveOperatingWorker(Villager v)
    {
        if (operatingWorkers.Contains(v))
        {
            operatingWorkers.Remove(v);
        }
    }

    private void Start()
    {
        renderers = GetComponentsInChildren<Renderer>();
        revealer = GetComponent<FogRevealer>();
        if (revealer != null) revealer.enabled = false;

        if (isPreBuiltLodging)
        {
            isConstructed = true;
            if (revealer != null) revealer.enabled = true;
            EnsureLodgingCollider();
            return;
        }

        if (data == null)
        {
            Debug.LogWarning($"[BuildingInstance] {name} has no BuildingData.");
            return;
        }

        maxHP = data.maxHP;
        currentHP = data.maxHP;
        CreateHealthBar();

        EnsureBuildingCollider();

        if (VillagerManager.Instance != null && isLocal)
            VillagerManager.Instance.RequestConstruction(this);

        StartCoroutine(ConstructionRoutine());
    }

    private void EnsureBuildingCollider()
    {
        if (GetComponent<Collider2D>() != null) return;

        var existing3D = GetComponent<Collider>();
        if (existing3D != null)
        {
            Debug.Log("[BuildingInstance] Entferne inkompatiblen 3D-Collider vor dem Hinzufügen eines 2D-Colliders.");
            DestroyImmediate(existing3D);
        }

        var col = gameObject.AddComponent<BoxCollider2D>();
        Vector3 s = transform.localScale;
        int footprintWidth = footprintWidthOverride > 0 ? footprintWidthOverride : data.width;
        int footprintHeight = footprintHeightOverride > 0 ? footprintHeightOverride : data.height;
        col.size = new Vector2(
            s.x > 0.001f ? footprintWidth / s.x : footprintWidth,
            s.y > 0.001f ? footprintHeight / s.y : footprintHeight
        );
    }

    private void EnsureLodgingCollider()
    {
        if (GetComponent<Collider2D>() != null) return;

        var existing3D = GetComponent<Collider>();
        if (existing3D != null)
        {
            Debug.Log("[BuildingInstance] Entferne inkompatiblen 3D-Collider vor dem Hinzufügen eines 2D-Colliders.");
            DestroyImmediate(existing3D);
        }

        var col = gameObject.AddComponent<BoxCollider2D>();
        Vector3 s = transform.localScale;
        col.size = new Vector2(Mathf.Max(1f, s.x), Mathf.Max(1f, s.y));
    }

    public bool NeedsMoreWorkers() => data != null && workersAssigned < data.requiredWorkers;
    public int GetAssignedWorkerCount() => workersAssigned;
    public int GetOperatingWorkerCount() => operatingWorkers.Count;

    public void RegisterWorker(Villager v)
    {
        workersAssigned++;
        assignedWorkers.Add(v);
    }

    public void NotifyWorkerArrived(Villager v)
    {
        workersArrived++;
        Debug.Log($"[BuildingInstance] Worker arrived at {data.buildingName}. Total: {workersArrived}/{data.requiredWorkers}");
    }

    private IEnumerator ConstructionRoutine()
    {
        // 1. Blueprint Phase: Blueish and very transparent
        SetColor(Color.cyan);
        SetAlpha(0.2f);

        // Wait for all workers to arrive at the site, but only for local buildings
        if (isLocal)
        {
            while (workersArrived < data.requiredWorkers)
            {
                yield return null;
            }
        }

        // 2. Building Phase: Normal colors, fading in
        SetColor(Color.white);
        
        while (constructionProgress < 1f)
        {
            constructionProgress += Time.deltaTime / data.buildTime;
            SetAlpha(Mathf.Lerp(0.1f, 1f, constructionProgress));
            yield return null;
        }

        CompleteConstruction();
    }

    private void SetAlpha(float alpha)
    {
        foreach (var r in renderers)
        {
            if (r.gameObject.name == "FogMask") continue;
            Color c = r.material.color;
            c.a = alpha;
            r.material.color = c;
        }
    }

    private void SetColor(Color color)
    {
        foreach (var r in renderers)
        {
            if (r.gameObject.name == "FogMask") continue;
            float currentAlpha = r.material.color.a;
            Color newCol = color;
            newCol.a = currentAlpha;
            r.material.color = newCol;
        }
    }

    private void CompleteConstruction()
    {
        isConstructed = true;
        if (revealer != null && isLocal) revealer.enabled = true;
        
        // Lagerhaus-Typ: Enthülle die ganze Insel
        if (isLocal && data != null && data.isWarehouseType)
        {
            Vector2Int start = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
            var islandCells = BuildingManager.FloodFillIsland(start);
            foreach (var cell in islandCells)
            {
                FogProjector.RegisterExploration(new Vector2(cell.x, cell.y), 1f);
            }
            Debug.Log($"[BuildingInstance] Insel enthüllt ({islandCells.Count} Zellen) durch {name}");
        }
        
        if (isLocal)
        {
            AudioManager.Instance?.PlayConstructionSound(transform.position);

            // Release workers
            foreach (var w in assignedWorkers) w.Release();
            assignedWorkers.Clear();

            // Hire operating workers
            TryHireOperatingWorkers();

            // If it's a worker hub, maybe it converts nearby villagers? 
            if (data.productionResourceId == "bevolkerung")
            {
                Player_UI.Instance.AddMaxPopulation(data.productionAmount);
                int soldierLimitBonus = Mathf.Max(1, data.productionAmount / 2);
                Player_UI.Instance.SetMaxResource("soldaten", Player_UI.Instance.GetMaxResource("soldaten") + soldierLimitBonus);
            }
        }
    }

    private void TriggerProductionCycle()
    {
        // Check if building has enough operating workers to produce
        if (operatingWorkers.Count < data.workersNeeded) return;

        // Check if it's currently work time according to the schedule
        if (!IsCurrentlyWorkTime()) return;

        if (ResourceManager.Instance != null)
        {
            // 1. Verbrauch prüfen
            if (!string.IsNullOrEmpty(data.consumedResourceId) && data.consumedAmount > 0)
            {
                if (ResourceManager.Instance.HasResource(data.consumedResourceId, data.consumedAmount))
                    ResourceManager.Instance.SpendResource(data.consumedResourceId, data.consumedAmount);
                else
                    return; // Ressourcen fehlen, Zyklus überspringen
            }

            // 2. Produzieren
            bool producedSomething = false;
            if (!string.IsNullOrEmpty(data.productionResourceId))
            {
                int baseAmount = data.productionAmount;
                float globalMood = 100f;
                if (VillagerManager.Instance != null)
                {
                    globalMood = VillagerManager.Instance.globalMood;
                }
                
                // Mood modifier: slowly declining yield down to 30% under worst conditions (0% mood)
                float yieldModifier = Mathf.Lerp(0.3f, 1.0f, globalMood / 100f);
                int actualProduction = Mathf.Max(1, Mathf.RoundToInt(baseAmount * yieldModifier));

                ResourceManager.Instance.AddResource(data.productionResourceId, actualProduction);
                TotalProduced += actualProduction;
                producedSomething = true;
            }

            if (data.producesVillagers && VillagerManager.Instance != null)
            {
                if (Player_UI.Instance.GetResource("dorfbewohner") < Player_UI.Instance.GetMaxPopulation())
                {
                    Villager.Role spawnRole = isBuilderHut ? Villager.Role.Worker : Villager.Role.Villager;
                    VillagerManager.Instance.SpawnVillagerAt(transform.position, spawnRole);
                    TotalProduced++;
                    producedSomething = true;
                }
            }

            if (producedSomething)
            {
                SpawnProductionParticles();
            }
        }
    }

    private void SpawnProductionParticles()
    {
        GameObject pObj = new GameObject("ProductionParticles");
        pObj.transform.position = transform.position + new Vector3(0f, 0f, -0.5f);
        
        ParticleSystem ps = pObj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        
        var main = ps.main;
        main.duration = 1.0f;
        main.loop = false;
        main.startLifetime = 1.0f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.gravityModifier = -0.3f; // Rise upwards gently
        main.stopAction = ParticleSystemStopAction.Destroy;

        // Custom resource icon textures as sprites!
        ParticleSystemRenderer psr = pObj.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
        {
            Sprite resSprite = null;
            if (!string.IsNullOrEmpty(data.productionResourceId))
            {
                string capId = Capitalize(data.productionResourceId);
                resSprite = Resources.Load<Sprite>($"{capId}_Icon");
                if (resSprite == null) resSprite = Resources.Load<Sprite>($"{capId}_Overlay");
            }
            else if (data.producesVillagers)
            {
                resSprite = Resources.Load<Sprite>("Villager");
            }

            if (resSprite != null)
            {
                psr.renderMode = ParticleSystemRenderMode.Billboard;
                Material mat = new Material(Shader.Find("Sprites/Default"));
                mat.mainTexture = resSprite.texture;
                psr.material = mat;
                main.startColor = Color.white; // Keep original texture colors
            }
            else
            {
                psr.renderMode = ParticleSystemRenderMode.Billboard;
                psr.material = new Material(Shader.Find("Sprites/Default"));
                
                Color pColor = Color.green;
                if (data.productionResourceId == "gold") pColor = new Color(1f, 0.85f, 0f);
                else if (data.productionResourceId == "stein") pColor = Color.gray;
                else if (data.productionResourceId == "eisen") pColor = new Color(0.7f, 0.7f, 0.8f);
                else if (data.productionResourceId == "dorfbewohner") pColor = Color.cyan;
                main.startColor = pColor;
            }
        }

        // Add a gentle rotation over time
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-90f, 90f);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        
        var burst = new ParticleSystem.Burst(0f, 12);
        emission.SetBursts(new ParticleSystem.Burst[] { burst });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.5f;

        ps.Play();
    }

    private string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    // ── Öffentliche Steuerung ────────────────────────────────────────────────

    /// <summary>Produktion pausieren / fortsetzen.</summary>
    public void ToggleProduction()
    {
        if (!isLocal) return;

        IsProductionPaused = !IsProductionPaused;
        Debug.Log($"[BuildingInstance] {data.buildingName} Produktion: {(IsProductionPaused ? "PAUSIERT" : "AKTIV")}");

        if (IsProductionPaused)
        {
            // Alle Arbeiter entlassen, damit sie sich andere Arbeit suchen
            foreach (var w in operatingWorkers)
            {
                if (w != null)
                {
                    w.Release();
                }
            }
            operatingWorkers.Clear();
        }
        else
        {
            // Sofort versuchen neue Arbeiter zuzuweisen
            TryHireOperatingWorkers();
        }
    }

    // ── Health System ────────────────────────────────────────────────────

    private void CreateHealthBar()
    {
        Transform existing = transform.Find("_HealthBar");
        if (existing != null) return;

        healthBar = GameObject.CreatePrimitive(PrimitiveType.Quad);
        healthBar.name = "_HealthBar";
        healthBar.transform.SetParent(transform, false);
        healthBar.transform.localPosition = new Vector3(0f, HealthBarY, -0.2f);
        healthBar.transform.localScale = new Vector3(HealthBarWidth, HealthBarHeight, 1f);

        var rend = healthBar.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Sprites/Default"));
        rend.material.color = Color.green;

        Destroy(healthBar.GetComponent<Collider>());
        healthBar.SetActive(false);
    }

    public void UpdateHealthBar()
    {
        if (healthBar == null) return;

        if (currentHP >= maxHP)
        {
            healthBar.SetActive(false);
            return;
        }

        healthBar.SetActive(true);
        float ratio = Mathf.Clamp01((float)currentHP / maxHP);
        healthBar.transform.localScale = new Vector3(HealthBarWidth * ratio, HealthBarHeight, 1f);
        healthBar.transform.localPosition = new Vector3(-HealthBarWidth * (1f - ratio) * 0.5f, HealthBarY, -0.2f);

        var rend = healthBar.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = ratio > 0.5f ? Color.green
                : ratio > 0.25f ? Color.yellow
                : Color.red;
        }
    }

    public void TakeDamage(int amount)
    {
        currentHP -= amount;

        if (PhotonNetwork.InRoom && !isLocal)
        {
            object[] damageData = new object[] { data.buildingName, transform.position.x, transform.position.y, amount };
            SendOptions sendOpts = new SendOptions { Reliability = true };
            PhotonNetwork.RaiseEvent(16, damageData,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others }, sendOpts);
        }

        UpdateHealthBar();

        if (currentHP <= 0)
        {
            currentHP = 0;
            Die();
        }
    }

    private void TransferIslandBuildings()
    {
        Vector2Int gridPos = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        var islandCells = BuildingManager.FloodFillIsland(gridPos);
        BuildingInstance[] allBuildings = FindObjectsByType<BuildingInstance>();
        foreach (var b in allBuildings)
        {
            if (b == null || b == this) continue;
            Vector2Int bGrid = new Vector2Int(Mathf.RoundToInt(b.transform.position.x), Mathf.RoundToInt(b.transform.position.y));
            if (islandCells.Contains(bGrid))
            {
                b.isLocal = !b.isLocal;
                b.UpdateHealthBar();
            }
        }
    }

    private void Die()
    {
        // Warehouse capture: Gebäude auf der Insel übernehmen
        if (data != null && data.isWarehouseType)
        {
            if (PhotonNetwork.InRoom)
            {
                object[] capData = new object[] { data.buildingName, transform.position.x, transform.position.y };
                SendOptions sendOpts = new SendOptions { Reliability = true };
                PhotonNetwork.RaiseEvent(BuildingCaptureEventCode, capData,
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others }, sendOpts);
            }

            TransferIslandBuildings();

            if (isLocal)
            {
                NotificationManager.Instance?.Notify("warehouse_lost",
                    "LAGERHAUS ZERSTÖRT! Du verlierst alle Gebäude auf dieser Insel!", 6f);
            }
            else
            {
                NotificationManager.Instance?.Notify("warehouse_captured",
                    "Lagerhaus zerstört! Alle Gebäude auf der Insel gehören dir.", 4f);
            }
        }

        // Game Over check: nur für eigene Gebäude
        if (isLocal && data != null && (data.isWarehouseType || data.canBuildOnOtherIslands))
        {
            bool anyLeft = false;
            foreach (var wh in FindObjectsByType<Warehouse>())
                if (wh != null && wh.isLocal) { anyLeft = true; break; }
            if (!anyLeft)
            {
                foreach (var bi in FindObjectsByType<BuildingInstance>())
                    if (bi != null && bi.isLocal && bi.data != null && bi.data.isWarehouseType) { anyLeft = true; break; }
            }
            if (!anyLeft)
            {
                NotificationManager.Instance?.Notify("game_over",
                    "ALLE LAGERHÄUSER ZERSTÖRT! DU HAST VERLOREN!", 30f);
                Debug.LogError("[GAME OVER] Kein Lagerhaus mehr vorhanden!");
            }
        }

        if (PhotonNetwork.InRoom && data != null)
        {
            object[] payload = new object[] { data.buildingName, transform.position.x, transform.position.y };
            SendOptions sendOpts = new SendOptions { Reliability = true };
            PhotonNetwork.RaiseEvent(13, payload,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others }, sendOpts);
        }

        Destroy(gameObject);
    }

    /// <summary>Gebäude abreißen – gibt Hälfte der Baukosten zurück.</summary>
    public void Demolish()
    {
        if (!isLocal) return;

        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.AddResource("holz",  data.woodCost  / 2);
            ResourceManager.Instance.AddResource("stein", data.stoneCost / 2);
            ResourceManager.Instance.AddResource("eisen", data.ironCost  / 2);
            ResourceManager.Instance.AddResource("gold",  data.goldCost  / 2);
        }
        Debug.Log($"[BuildingInstance] {data.buildingName} abgerissen.");
        Destroy(gameObject);
    }

    public bool ToggleHutType()
    {
        if (!isLocal) return false;
        if (Player_UI.Instance == null) return false;
        
        int wheat = Player_UI.Instance.GetResource("weizen");
        if (wheat < 1)
        {
            Debug.LogWarning("[HutType] Not enough wheat to change house type!");
            return false;
        }

        // Deduct 1 wheat!
        Player_UI.Instance.AddResource("weizen", -1);

        isBuilderHut = !isBuilderHut;
        
        if (isBuilderHut)
        {
            // Convert one available general Villager (Dorfbewohner) to a Worker (Bauarbeiter)!
            if (VillagerManager.Instance != null)
            {
                Villager v = VillagerManager.Instance.GetAvailableVillager();
                if (v != null)
                {
                    v.role = Villager.Role.Worker;
                    // Visually update the villager
                    SpriteRenderer sr = v.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        sr.color = Color.orange;
                        Sprite custom = Resources.Load<Sprite>("Worker");
                        if (custom != null) sr.sprite = custom;
                    }
                    Debug.Log("[HutType] Converted 1 Villager to Worker (Builder)");
                }
            }
        }
        else
        {
            // Convert one available Worker (Bauarbeiter) to a Villager (Dorfbewohner)!
            if (VillagerManager.Instance != null)
            {
                Villager w = VillagerManager.Instance.GetAvailableWorker();
                if (w != null)
                {
                    w.role = Villager.Role.Villager;
                    // Visually update the villager
                    SpriteRenderer sr = w.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        sr.color = Color.white;
                        Sprite custom = Resources.Load<Sprite>("Villager");
                        if (custom != null) sr.sprite = custom;
                    }
                    Debug.Log("[HutType] Converted 1 Worker to Villager");
                }
            }
        }
        return true;
    }

    public bool CanAffordSoldier()
    {
        if (ResourceManager.Instance == null) return false;
        
        switch (selectedResource)
        {
            case BarracksResource.Wood:
                return ResourceManager.Instance.HasResource("holz", woodSoldierCost);
            case BarracksResource.Stone:
                return ResourceManager.Instance.HasResource("stein", stoneSoldierCost);
            case BarracksResource.Gold:
                return ResourceManager.Instance.HasResource("gold", goldSoldierCost);
            case BarracksResource.Iron:
                return ResourceManager.Instance.HasResource("eisen", ironSoldierCost);
            default:
                return true;
        }
    }

    public void SpendSoldierResources()
    {
        if (ResourceManager.Instance == null) return;
        
        switch (selectedResource)
        {
            case BarracksResource.Wood:
                ResourceManager.Instance.SpendResource("holz", woodSoldierCost);
                break;
            case BarracksResource.Stone:
                ResourceManager.Instance.SpendResource("stein", stoneSoldierCost);
                break;
            case BarracksResource.Gold:
                ResourceManager.Instance.SpendResource("gold", goldSoldierCost);
                break;
            case BarracksResource.Iron:
                ResourceManager.Instance.SpendResource("eisen", ironSoldierCost);
                break;
        }
    }

    public void OrderSoldier(SoldierType sType)
    {
        if (!isLocal) return;

        if (CanAffordSoldier())
        {
            SpendSoldierResources();
            recruitQueue.Enqueue(sType);
            recruitResourceQueue.Enqueue(selectedResource);
        }
        else
        {
            NotificationManager.Instance?.Notify("barracks_no_resources", "Nicht genügend Ressourcen für Soldaten-Ausbildung!", 5f);
        }
    }

    private void UpdateBarracksRecruitment()
    {
        if (Player_UI.Instance == null) return;

        // If not currently recruiting, try to start one
        if (recruitmentTimer <= 0f)
        {
            if (recruitQueue.Count > 0)
            {
                currentRecruitingType = recruitQueue.Dequeue();
                currentRecruitingResource = recruitResourceQueue.Count > 0 ? recruitResourceQueue.Dequeue() : selectedResource;
                recruitmentTimer = RECRUIT_INTERVAL;
            }
            else if (autoRecruit)
            {
                int currentSoldiers = Player_UI.Instance.GetResource("soldaten");
                int currentVillagers = Player_UI.Instance.GetResource("dorfbewohner");
                int totalPop = currentSoldiers + currentVillagers;

                // Auto recruit if soldiers are less than 20% of population, and we have free villagers
                if (currentSoldiers < 0.20f * totalPop && currentVillagers > 0)
                {
                    // Randomly pick an enabled type
                    List<SoldierType> activeTypes = new List<SoldierType>();
                    if (spearSelected) activeTypes.Add(SoldierType.Spear);
                    if (shieldSelected) activeTypes.Add(SoldierType.Shield);
                    if (swordSelected) activeTypes.Add(SoldierType.Sword);
                    if (bowSelected) activeTypes.Add(SoldierType.Bow);

                    if (activeTypes.Count > 0)
                    {
                        if (CanAffordSoldier())
                        {
                            SpendSoldierResources();
                            currentRecruitingType = activeTypes[Random.Range(0, activeTypes.Count)];
                            currentRecruitingResource = selectedResource;
                            recruitmentTimer = RECRUIT_INTERVAL;
                        }
                        else
                        {
                            NotificationManager.Instance?.Notify("barracks_no_resources", "Auto-Ausbildung pausiert: Keine Ressourcen!", 10f);
                        }
                    }
                }
            }
        }

        // If recruiting, advance timer
        if (recruitmentTimer > 0f)
        {
            // Only advance recruitment if we have a free villager to recruit!
            Villager candidate = VillagerManager.Instance != null ? VillagerManager.Instance.GetAvailableVillager() : null;
            
            if (candidate != null)
            {
                recruitmentTimer -= Time.deltaTime;
                ProductionProgress = Mathf.Clamp01(1f - (recruitmentTimer / RECRUIT_INTERVAL));

                if (recruitmentTimer <= 0f)
                {
                    CompleteRecruitment(candidate);
                    ProductionProgress = 0f;
                }
            }
            else
            {
                // Pause recruitment if no villager is available
                NotificationManager.Instance?.Notify("no_free_villagers", "Du hast keine freien Arbeitslosen mehr.", 10f);
                ProductionProgress = 0f;
            }
        }
        else
        {
            ProductionProgress = 0f;
        }
    }

    private void CompleteRecruitment(Villager candidate)
    {
        if (Player_UI.Instance == null) return;

        int current = Player_UI.Instance.GetResource("soldaten");
        int max = Player_UI.Instance.GetMaxResource("soldaten");

        if (current < max)
        {
            Player_UI.Instance.AddResource("soldaten", 1);
            Player_UI.Instance.AddResource("dorfbewohner", -1);

            if (VillagerManager.Instance != null)
            {
                VillagerManager.Instance.NotifyVillagerConverted(candidate);
            }

            int formationIndex = Mathf.Max(0, Player_UI.Instance.GetResource("soldaten"));
            int columns = 3;
            int row = formationIndex / columns;
            int column = formationIndex % columns;
            Vector2 spawnOffset = new Vector2((column - 1) * 0.8f, -0.75f - row * 0.65f);
            Vector3 spawnPos = transform.position + new Vector3(spawnOffset.x, spawnOffset.y, 0f);
            
            // Create Soldier
            GameObject solObj = new GameObject($"Player_Soldier_{currentRecruitingType}");
            solObj.transform.position = spawnPos;
            solObj.transform.rotation = Quaternion.identity;

            SpriteRenderer sr = solObj.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 21;

            BoxCollider2D bc = solObj.AddComponent<BoxCollider2D>();
            bc.size = new Vector2(1f, 1f);

            Soldier s = solObj.AddComponent<Soldier>();
            s.netId = Soldier.nextNetIdValue;
            Soldier.nextNetIdValue++;
            s.soldierType = currentRecruitingType;
            s.team = Team.Player;
            s.moveSpeed = 1.5f;
            if (Photon.Pun.PhotonNetwork.LocalPlayer != null)
            {
                s.ownerActorNumber = Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber;
            }

            FogRevealer fr = solObj.AddComponent<FogRevealer>();
            fr.radius = 4f;
            fr.isLocalPlayer = true;

            // Map BarracksResource to WeaponMaterial (top-level enum)
            switch (currentRecruitingResource)
            {
                case BarracksResource.Wood:  s.weaponMaterial = WeaponMaterial.Wood; break;
                case BarracksResource.Stone: s.weaponMaterial = WeaponMaterial.Stone; break;
                case BarracksResource.Gold:  s.weaponMaterial = WeaponMaterial.Gold; break;
                case BarracksResource.Iron:  s.weaponMaterial = WeaponMaterial.Iron; break;
            }

            // Play recruitment particles!
            SpawnProductionParticles();

            if (PhotonNetwork.InRoom)
            {
                object[] syncData = new object[] { s.netId, (int)currentRecruitingType, spawnPos.x, spawnPos.y };
                SendOptions sendOpts = new SendOptions { Reliability = true };
                PhotonNetwork.RaiseEvent(12, syncData,
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others }, sendOpts);
            }

            Destroy(candidate.gameObject);
        }
        else
        {
            Debug.Log("Soldaten-Limit erreicht! Rekrutierung pausiert.");
        }
    }

    private void OnDestroy()
    {
        List<Villager> sleepers = new List<Villager>(sleepingVillagers);
        foreach (var sleeper in sleepers)
        {
            if (sleeper != null)
            {
                sleeper.ClearSleepHouseReference();
            }
        }
        sleepingVillagers.Clear();

        // Release construction workers if still building
        foreach (var w in assignedWorkers)
        {
            if (w != null) w.Release();
        }
        assignedWorkers.Clear();

        // Release operating workers
        foreach (var w in operatingWorkers)
        {
            if (w != null)
            {
                w.isOperatingWorker = false;
                w.Release();
            }
        }
        operatingWorkers.Clear();
    }
}


