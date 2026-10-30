using UnityEngine;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

public class VillagerManager : MonoBehaviour, IOnEventCallback
{
    public static VillagerManager Instance;

    /// <summary>Periodischer Villager-Zustands-Sync: { count, (id, x, y, role, visible)* } </summary>
    public const byte VillagerStateEventCode = 19;
    private const float VillagerSyncInterval = 0.4f;

    [Header("Settings")]
    public GameObject villagerPrefab;
    public float wanderRadius = 10f;
    [HideInInspector]
    public float globalMood = 100f;

    private List<Villager> activeVillagers = new List<Villager>();
    public IReadOnlyList<Villager> ActiveVillagers => activeVillagers;

    // Remote-Villager (fremder Spieler): nach ID verwaltet, keine Simulation
    private readonly Dictionary<int, Villager> remoteVillagers = new Dictionary<int, Villager>();
    private readonly Dictionary<int, int> remoteMissedSnapshots = new Dictionary<int, int>();
    private readonly HashSet<int> seenInLastSnapshot = new HashSet<int>();

    private float villagerSyncTimer = 0f;
    private float hudUpdateTimer = 0f;

    private List<BuildingInstance> pendingBuildings = new List<BuildingInstance>();
    private float currentFoodMoodEffect = 0f;
    private float currentWheatMoodEffect = 0f;
    private float currentLuxuryMoodEffect = 0f;
    private float wheatConsumedAccumulator = 0f;
    private float luxuryConsumedAccumulator = 0f;
    private bool lowWheatAlertSent;
    private bool lowWoodAlertSent;
    private bool lowStoneAlertSent;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public void SpawnStartingPopulation(int islandIndex)
    {
        int startVillagers = 10;
        int startWorkers = 2;

        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbySettingsKeys.StartVillagers, out object villagersObj))
            {
                startVillagers = System.Convert.ToInt32(villagersObj);
            }

            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbySettingsKeys.StartWorkers, out object workersObj))
            {
                startWorkers = System.Convert.ToInt32(workersObj);
            }
        }

        for (int i = 0; i < startVillagers; i++) SpawnVillager(islandIndex, Villager.Role.Villager);
        for (int i = 0; i < startWorkers; i++) SpawnVillager(islandIndex, Villager.Role.Worker);
    }

    private void SpawnVillager(int islandIndex, Villager.Role role)
    {
        Vector2 islandPos = IslandManager.Instance.GetIslandPosition(islandIndex);
        Vector2 randomOffset = Random.insideUnitCircle * 8f;
        SpawnVillagerAt(islandPos + randomOffset, role);
    }

    public void SpawnVillagerAt(Vector2 position, Villager.Role role, bool isLocal = true)
    {
        EnsurePrefab();
        GameObject vObj = Instantiate(villagerPrefab, new Vector3(position.x, position.y, -0.2f), Quaternion.identity);
        vObj.SetActive(true);
        Villager v = vObj.AddComponent<Villager>();
        v.role = role;
        v.isLocal = isLocal;
        v.isSimulated = isLocal;

        if (isLocal)
        {
            // Stabile Netzwerk-ID (Besitzer-Actor steckt in der ID) — auch offline eindeutig
            v.ownerActorNumber = NetCombat.LocalActorId;
            v.villagerId = NetCombat.NextId(v.ownerActorNumber);
        }
        
        // Visual distinction
        SpriteRenderer sr = vObj.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            if (role == Villager.Role.Worker) sr.color = Color.orange;
            else sr.color = Color.white;
            
            Sprite custom = Resources.Load<Sprite>(role.ToString());
            if (custom != null) sr.sprite = custom;
        }

        FogRevealer fr = vObj.AddComponent<FogRevealer>();
        fr.radius = 4f;
        fr.isLocalPlayer = isLocal;

        activeVillagers.Add(v);
    }

    /// <summary>
    /// Erzeugt (oder aktualisiert) einen Villager, der einem FREMDEN Spieler gehört.
    /// Kein FogRevealer, keine Simulation — nur Positions-Proxy, der per Event 19 versorgt wird.
    /// </summary>
    public Villager SpawnRemoteVillager(int villagerId, Vector2 position, Villager.Role role,
        int ownerActorNumber, bool visible)
    {
        if (villagerId <= 0 || ownerActorNumber == NetCombat.LocalActorId) return null;

        // Schon vorhanden → nur Zustand auffrischen
        if (remoteVillagers.TryGetValue(villagerId, out Villager existing))
        {
            if (existing == null)
            {
                remoteVillagers.Remove(villagerId);
            }
            else
            {
                remoteMissedSnapshots[villagerId] = 0;
                existing.ApplyNetworkState(position.x, position.y, visible, role);
                return existing;
            }
        }

        EnsurePrefab();
        GameObject vObj = Instantiate(villagerPrefab, new Vector3(position.x, position.y, -0.2f), Quaternion.identity);
        vObj.SetActive(true);
        Villager v = vObj.AddComponent<Villager>();
        v.villagerId = villagerId;
        v.ownerActorNumber = ownerActorNumber;
        v.role = role;
        v.isLocal = false;
        v.isSimulated = false;
        v.ApplyNetworkState(position.x, position.y, visible, role);
        // Sichtbarkeit aus dem Snapshot respektieren (Schlafende sind unsichtbar)

        SpriteRenderer sr = vObj.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = (role == Villager.Role.Worker) ? Color.orange : Color.white;
            Sprite custom = Resources.Load<Sprite>(role.ToString());
            if (custom != null) sr.sprite = custom;
        }

        remoteVillagers[villagerId] = v;
        remoteMissedSnapshots[villagerId] = 0;
        return v;
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code == VillagerStateEventCode && photonEvent.CustomData is object[] data)
        {
            ReceiveVillagerState(data, photonEvent.Sender);
        }
    }

    private void ReceiveVillagerState(object[] data, int senderActor)
    {
        if (data == null || data.Length < 1) return;
        int count = (int)data[0];

        seenInLastSnapshot.Clear();
        int idx = 1;
        for (int i = 0; i < count && idx + 4 < data.Length; i++, idx += 5)
        {
            int villagerId = (int)data[idx];
            float x = (float)data[idx + 1];
            float y = (float)data[idx + 2];
            Villager.Role role = (Villager.Role)(int)data[idx + 3];
            bool visible = (int)data[idx + 4] > 0;

            Villager v = SpawnRemoteVillager(villagerId, new Vector2(x, y), role,
                NetCombat.OwnerFromId(villagerId), visible);
            if (v != null) seenInLastSnapshot.Add(villagerId);
        }

        // Reconcile — NUR für Villager des Senders dieses Snapshots!
        // (Ein Snapshot enthält nur eigene Villager; andere Spieler senden eigene Snapshots.)
        // Alles was hier fehlt, zählt als verpasst → nach 2 verpassten Snapshots ist er
        // wirklich weg (Tod/Krankheit/Umwandlung zum Soldaten) → entfernen.
        List<int> toReset = null;
        foreach (var kvp in remoteVillagers)
        {
            if (NetCombat.OwnerFromId(kvp.Key) != senderActor) continue;

            if (seenInLastSnapshot.Contains(kvp.Key))
            {
                remoteMissedSnapshots[kvp.Key] = 0;
            }
            else
            {
                remoteMissedSnapshots.TryGetValue(kvp.Key, out int missed);
                remoteMissedSnapshots[kvp.Key] = missed + 1;

                if (remoteMissedSnapshots[kvp.Key] >= 2)
                {
                    if (toReset == null) toReset = new List<int>();
                    toReset.Add(kvp.Key);
                }
            }
        }

        if (toReset != null)
        {
            foreach (int id in toReset)
            {
                if (remoteVillagers.TryGetValue(id, out Villager v) && v != null)
                {
                    Destroy(v.gameObject);
                }
                remoteVillagers.Remove(id);
                remoteMissedSnapshots.Remove(id);
            }
        }
    }

    private void BroadcastVillagerState()
    {
        if (!PhotonNetwork.InRoom || activeVillagers.Count == 0) return;

        List<object> data = new List<object>();
        int count = 0;
        foreach (var v in activeVillagers)
        {
            if (v == null || !v.isLocal) continue;
            data.Add(v.VillagerNetworkId);
            data.Add(v.transform.position.x);
            data.Add(v.transform.position.y);
            data.Add((int)v.role);
            data.Add(v.IsVisibleToNetwork ? 1 : 0);
            count++;
        }
        if (count == 0) return;

        List<object> payload = new List<object>(data.Count + 1);
        payload.Add(count);
        payload.AddRange(data);

        PhotonNetwork.RaiseEvent(VillagerStateEventCode, payload.ToArray(),
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            new SendOptions { Reliability = false }); // Positionen: unbezuverlässig ist okay, Tod kommt über "fehlen" im Reconcile
    }

    private void EnsurePrefab()
    {
        if (villagerPrefab == null)
        {
            villagerPrefab = Resources.Load<GameObject>("Villager");
            if (villagerPrefab == null)
            {
                villagerPrefab = GameObject.CreatePrimitive(PrimitiveType.Quad);
                villagerPrefab.name = "Villager_Fallback";
                Destroy(villagerPrefab.GetComponent<Collider>());
                villagerPrefab.SetActive(false);
            }
        }
    }

    public void RequestConstruction(BuildingInstance building)
    {
        Debug.Log($"[VillagerManager] Construction requested for: {building.data.buildingName}");
        pendingBuildings.Add(building);
    }

    private void Update()
    {
        UpdateFoodEffect();

        // HUD/Zähler + Alerts: 4×/s reicht (war: jedes Frame O(2N) über alle Villager)
        hudUpdateTimer -= Time.deltaTime;
        if (hudUpdateTimer <= 0f)
        {
            hudUpdateTimer = 0.25f;
            UpdateHUD();
            CheckAlerts();
        }

        if (pendingBuildings.Count > 0)
        {
            TryAssignWorkers();
        }

        // Periodischer Villager-Zustands-Broadcast (nur der Besitzer sendet)
        if (PhotonNetwork.InRoom)
        {
            villagerSyncTimer -= Time.deltaTime;
            if (villagerSyncTimer <= 0f)
            {
                villagerSyncTimer = VillagerSyncInterval;
                BroadcastVillagerState();
            }
        }
    }

    private void UpdateHUD()
    {
        if (Player_UI.Instance == null) return;

        int freeVillagers = 0;
        int workers = 0;
        int employedVillagers = 0;

        foreach (var v in activeVillagers)
        {
            if (v == null || !v.isLocal) continue;
            
            if (v.role == Villager.Role.Worker)
            {
                workers++;
            }
            else
            {
                if (v.isOperatingWorker)
                {
                    employedVillagers++;
                }
                else
                {
                    freeVillagers++;
                }
            }
        }

        int totalVillagers = freeVillagers + employedVillagers;
        Player_UI.Instance.SetMaxResource("dorfbewohner", totalVillagers);
        Player_UI.Instance.SetResource("dorfbewohner", freeVillagers);
        Player_UI.Instance.SetResource("arbeiter", workers);
        Player_UI.Instance.SetResource("bevolkerung", totalVillagers + workers);

        // Calculate global mood based on local villagers only
        float totalMood = 0f;
        int moodCount = 0;
        foreach (var v in activeVillagers)
        {
            if (v != null && v.isLocal)
            {
                totalMood += v.mood;
                moodCount++;
            }
        }
        globalMood = moodCount > 0 ? (totalMood / moodCount) : 100f;
        int avgMood = Mathf.RoundToInt(globalMood);
        Player_UI.Instance.SetResource("stimmung", avgMood);
    }

    public Villager GetAvailableVillager()
    {
        foreach (var v in activeVillagers)
        {
            if (v != null && v.isLocal && v.role == Villager.Role.Villager && !v.isOperatingWorker && !v.IsBusy())
            {
                return v;
            }
        }

        NotificationManager.Instance?.Notify("no_free_villagers", "Du hast keine freien Arbeitslosen mehr.", 10f);
        return null;
    }

    private void TryAssignWorkers()
    {
        for (int i = pendingBuildings.Count - 1; i >= 0; i--)
        {
            BuildingInstance b = pendingBuildings[i];
            if (b.NeedsMoreWorkers())
            {
                Villager worker = GetAvailableWorker();
                if (worker != null)
                {
                    Debug.Log($"[VillagerManager] Assigning worker to {b.data.buildingName}");
                    // Spread workers around the building
                    float angle = b.GetAssignedWorkerCount() * 2f * Mathf.PI / b.data.requiredWorkers;
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.5f;
                    
                    worker.AssignToBuild(b, offset);
                    b.RegisterWorker(worker);
                }
            }
            else
            {
                pendingBuildings.RemoveAt(i);
            }
        }
    }

    public Villager GetAvailableWorker()
    {
        int total = 0;
        int busy = 0;
        foreach (var v in activeVillagers)
        {
            if (v != null && v.isLocal && v.role == Villager.Role.Worker)
            {
                total++;
                if (!v.IsBusy()) return v;
                busy++;
            }
        }
        // Debug.Log($"[VillagerManager] No free workers. Total: {total}, Busy: {busy}");
        if (total <= 0)
        {
            NotificationManager.Instance?.Notify("no_workers_total", "Du hast keine Bauarbeiter.", 10f);
        }
        else
        {
            NotificationManager.Instance?.Notify("no_workers_free", "Alle Bauarbeiter sind gerade beschaftigt.", 10f);
        }
        return null;
    }

    public void NotifyVillagerConverted(Villager v)
    {
        if (activeVillagers.Contains(v))
        {
            activeVillagers.Remove(v);
        }
    }

    private void RemoveVillager()
    {
        if (activeVillagers.Count > 0)
        {
            Villager v = activeVillagers[activeVillagers.Count - 1];
            activeVillagers.RemoveAt(activeVillagers.Count - 1);
            Destroy(v.gameObject);
        }
    }

    public float GetFoodMoodEffect() => currentFoodMoodEffect;

    public void AppendMoodTooltipBreakdown(List<string> breakdown)
    {
        if (breakdown == null) return;

        breakdown.Add("<b>Nahrung:</b>");
        if (currentWheatMoodEffect < -0.001f)
        {
            breakdown.Add($"<color={GameUITheme.HtmlBad}>Weizen knapp: {currentWheatMoodEffect * 100f:F0}%/s Stimmung</color>");
        }
        else if (currentWheatMoodEffect > 0.001f)
        {
            breakdown.Add($"<color={GameUITheme.HtmlGood}>Weizen-Vorrat: +{currentWheatMoodEffect * 100f:F0}%/s Stimmung</color>");
        }
        else
        {
            breakdown.Add("Weizen-Vorrat: stabil");
        }

        if (currentLuxuryMoodEffect > 0.001f)
        {
            breakdown.Add($"<color={GameUITheme.HtmlGood}>Luxus (Fleisch/Früchte): +{currentLuxuryMoodEffect * 100f:F0}%/s Stimmung</color>");
        }

        breakdown.Add("");
        breakdown.Add("<b>Arbeit (alle Arbeiter):</b>");

        float totalWorkStressPerSecond = 0f;
        int workStressCount = 0;
        float totalWorkBonusPerSecond = 0f;
        int workBonusCount = 0;

        float totalHousePerSecond = 0f;
        int houseSleepCount = 0;
        float totalHomelessPerSecond = 0f;
        int homelessSleepCount = 0;

        foreach (var villager in activeVillagers)
        {
            if (villager == null || !villager.isOperatingWorker)
            {
                continue;
            }

            Villager.MoodRateSnapshot snapshot = villager.GetMoodRateSnapshot();

            if (snapshot.workRatePerSecond < -0.0001f)
            {
                totalWorkStressPerSecond += snapshot.workRatePerSecond;
                workStressCount++;
            }
            else if (snapshot.workRatePerSecond > 0.0001f)
            {
                totalWorkBonusPerSecond += snapshot.workRatePerSecond;
                workBonusCount++;
            }

            if (snapshot.isSleepingInHouse)
            {
                totalHousePerSecond += snapshot.housingRatePerSecond;
                houseSleepCount++;
            }
            else if (snapshot.isSleepingHomeless)
            {
                totalHomelessPerSecond += snapshot.housingRatePerSecond;
                homelessSleepCount++;
            }
        }

        if (workStressCount > 0)
        {
            float totalPerMinute = totalWorkStressPerSecond * 60f;
            float avgPerMinute = totalPerMinute / workStressCount;
            breakdown.Add($"<color={GameUITheme.HtmlBad}>Arbeitsstress gesamt ({workStressCount}): {totalPerMinute:F1} Pkt/Min</color>");
            breakdown.Add($"<color={GameUITheme.HtmlBad}>Ø pro Arbeiter: {avgPerMinute:F1} Pkt/Min</color>");
        }
        else
        {
            breakdown.Add("Arbeitsstress: keine aktive Belastung");
        }

        if (workBonusCount > 0)
        {
            float totalPerMinute = totalWorkBonusPerSecond * 60f;
            float avgPerMinute = totalPerMinute / workBonusCount;
            breakdown.Add($"<color={GameUITheme.HtmlGood}>Arbeitsbonus gesamt ({workBonusCount}): +{totalPerMinute:F1} Pkt/Min</color>");
            breakdown.Add($"<color={GameUITheme.HtmlGood}>Ø pro Arbeiter: +{avgPerMinute:F1} Pkt/Min</color>");
        }

        breakdown.Add("");
        breakdown.Add("<b>Unterkunft (Schlaf):</b>");

        const float referenceHouseRatePerMinute = 0.12f * 60f;

        if (houseSleepCount > 0)
        {
            float totalPerMinute = totalHousePerSecond * 60f;
            float avgPerMinute = totalPerMinute / houseSleepCount;
            breakdown.Add($"<color={GameUITheme.HtmlGood}>Haus ({houseSleepCount}): +{totalPerMinute:F1} Pkt/Min gesamt</color>");
            breakdown.Add($"<color={GameUITheme.HtmlGood}>Ø pro Person: +{avgPerMinute:F1} Pkt/Min</color>");
        }
        else
        {
            breakdown.Add("Haus: aktuell niemand im Schlaf");
        }

        if (homelessSleepCount > 0)
        {
            float totalPerMinute = totalHomelessPerSecond * 60f;
            float avgPerMinute = totalPerMinute / homelessSleepCount;
            float deltaVsHouse = avgPerMinute - referenceHouseRatePerMinute;
            breakdown.Add($"<color={GameUITheme.HtmlWarning}>Obdachlosigkeit ({homelessSleepCount}): +{totalPerMinute:F1} Pkt/Min gesamt</color>");
            breakdown.Add($"<color={GameUITheme.HtmlWarning}>Ø pro Person: +{avgPerMinute:F1} Pkt/Min</color>");
            if (deltaVsHouse < -0.05f)
            {
                breakdown.Add($"<color={GameUITheme.HtmlBad}>vs. Haus: {deltaVsHouse:F1} Pkt/Min</color>");
            }
        }
        else
        {
            breakdown.Add("Obdachlosigkeit: niemand schläft draußen");
        }

        breakdown.Add("");
        breakdown.Add("<b>Stimmungs-Ausgleich (Drift zu 50%):</b>");
        float avgDriftPerSecond = (50f - globalMood) * 0.003f;
        float avgDriftPerMinute = avgDriftPerSecond * 60f;
        if (globalMood > 50.1f)
        {
            breakdown.Add($"<color={GameUITheme.HtmlBad}>Druck nach unten (hohe Stimmung): {avgDriftPerMinute:F1} Pkt/Min</color>");
        }
        else if (globalMood < 49.9f)
        {
            breakdown.Add($"<color={GameUITheme.HtmlGood}>Druck nach oben (niedrige Stimmung): +{avgDriftPerMinute:F1} Pkt/Min</color>");
        }
        else
        {
            breakdown.Add("Druck zu 50%: neutral");
        }

        float yieldModifier = Mathf.Lerp(0.3f, 1.0f, globalMood / 100f);
        if (yieldModifier < 0.995f)
        {
            breakdown.Add($"<color={GameUITheme.HtmlBad}>Ertragsmalus (niedrige Stimmung): −{(1f - yieldModifier) * 100f:F0}%</color>");
        }
    }

    private void UpdateFoodEffect()
    {
        if (Player_UI.Instance == null) return;

        int desertFruits = Player_UI.Instance.GetResource("wüstenfrucht");
        int fruits = Player_UI.Instance.GetResource("fruechte") + desertFruits;
        int meat = Player_UI.Instance.GetResource("fleisch");
        int weizen = Player_UI.Instance.GetResource("weizen");
        int localVillagerCount = 0;
        foreach (var v in activeVillagers) { if (v != null && v.isLocal) localVillagerCount++; }
        int pop = Mathf.Max(1, localVillagerCount);
        
        // 1. Wheat reserve levels (Primary food: crisis if low)
        float wheatRatio = (float)weizen / pop;
        float wheatEffect = 0f;
        if (weizen == 0)
        {
            // Wheat fully depleted: severe starvation! (increased penalty)
            wheatEffect = -0.25f;
        }
        else if (wheatRatio < 0.5f)
        {
            // Low wheat reserve: strong shortage panic! (increased penalty)
            wheatEffect = -0.12f;
        }

        // 2. Dynamic Fruits & Meat reserve scales (Feasting: consume more when abundant, granting stronger boosts!)
        float fruitsRatio = (float)fruits / pop;
        float meatRatio = (float)meat / pop;
        
        float fruitsConsMod = fruits > 0 ? Mathf.Clamp(fruitsRatio, 0.1f, 1.5f) : 0f;
        float meatConsMod = meat > 0 ? Mathf.Clamp(meatRatio, 0.1f, 1.5f) : 0f;
        
        float luxuryEffect = 0f;
        if (fruits > 0)
        {
            luxuryEffect += Mathf.Min(0.18f, fruitsConsMod * 0.035f); // Früchte: starker Zufriedenheits-Boost
        }
        if (meat > 0)
        {
            luxuryEffect += Mathf.Min(0.08f, meatConsMod * 0.018f);
        }

        currentWheatMoodEffect = wheatEffect;
        currentLuxuryMoodEffect = luxuryEffect;
        currentFoodMoodEffect = Mathf.Clamp(wheatEffect + luxuryEffect, -0.25f, 0.22f);

        // 3. Proportional consumption for staple (wheat): ALWAYS 0.2 units per villager per 60 seconds (1 minute)
        wheatConsumedAccumulator += pop * 0.2f * (Time.deltaTime / 60f);
        if (wheatConsumedAccumulator >= 1f)
        {
            int toConsume = Mathf.FloorToInt(wheatConsumedAccumulator);
            wheatConsumedAccumulator -= toConsume;
            
            if (weizen >= toConsume)
            {
                Player_UI.Instance.AddResource("weizen", -toConsume);
            }
            else
            {
                if (weizen > 0) Player_UI.Instance.AddResource("weizen", -weizen);
            }
        }

        // 4. Dynamic luxury consumption (fruits & meat): consume more when abundant, up to 1.5x base rate (max 0.225/min)!
        luxuryConsumedAccumulator += pop * 0.15f * (Time.deltaTime / 60f);
        if (luxuryConsumedAccumulator >= 1f)
        {
            int baseToConsume = Mathf.FloorToInt(luxuryConsumedAccumulator);
            luxuryConsumedAccumulator -= baseToConsume;
            
            // Scaled consumption based on abundance
            int targetFruits = fruits > 0 ? Mathf.Max(1, Mathf.RoundToInt(baseToConsume * fruitsConsMod)) : 0;
            int targetMeat = meat > 0 ? Mathf.Max(1, Mathf.RoundToInt(baseToConsume * meatConsMod)) : 0;
            
            int fruitsTaken = Mathf.Min(fruits, targetFruits);
            int meatTaken = Mathf.Min(meat, targetMeat);

            if (fruitsTaken > 0)
            {
                int fromDesert = Mathf.Min(desertFruits, fruitsTaken);
                int fromRegular = fruitsTaken - fromDesert;
                if (fromDesert > 0) Player_UI.Instance.AddResource("wüstenfrucht", -fromDesert);
                if (fromRegular > 0) Player_UI.Instance.AddResource("fruechte", -fromRegular);
            }
            if (meatTaken > 0) Player_UI.Instance.AddResource("fleisch", -meatTaken);
        }
    }

    private void CheckAlerts()
    {
        if (globalMood < 35f)
        {
            NotificationManager.Instance?.Notify("low_mood", "Villager Stimmung ist niedrig.", 18f);
        }

        if (Player_UI.Instance == null)
        {
            return;
        }

        int wheat = Player_UI.Instance.GetResource("weizen");
        int wood = Player_UI.Instance.GetResource("holz");
        int stone = Player_UI.Instance.GetResource("stein");

        int wheatThreshold = Mathf.Max(2, activeVillagers.Count / 2);
        if (wheat <= wheatThreshold)
        {
            if (!lowWheatAlertSent)
            {
                NotificationManager.Instance?.Notify("low_weizen", "Ressource Weizen ist low.", 14f);
                lowWheatAlertSent = true;
            }
        }
        else if (wheat > wheatThreshold + 2)
        {
            lowWheatAlertSent = false;
        }

        if (wood <= 15)
        {
            if (!lowWoodAlertSent)
            {
                NotificationManager.Instance?.Notify("low_holz", "Ressource Holz ist low.", 14f);
                lowWoodAlertSent = true;
            }
        }
        else if (wood > 20)
        {
            lowWoodAlertSent = false;
        }

        if (stone <= 15)
        {
            if (!lowStoneAlertSent)
            {
                NotificationManager.Instance?.Notify("low_stein", "Ressource Stein ist low.", 14f);
                lowStoneAlertSent = true;
            }
        }
        else if (stone > 20)
        {
            lowStoneAlertSent = false;
        }
    }
}
