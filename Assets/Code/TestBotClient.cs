using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// ECHTER Test-Bot: baut eine ZWEITE echte Photon-Verbindung auf (LoadBalancingClient),
/// joinet denselben Raum wie DU und spielt — wie ein echter entfernter Spieler —
/// ausschließlich über die echte Netzwerk-Event-Pipeline (RaiseEvent über die Photon Cloud):
///
///   Event 2  → Gebäude platzieren (Lagerhaus, Häuser, Kaserne, Turm, Schiff mit shipId)
///   Event 19 → Villager-Zustands-Snapshots (Siedeln/Schlafen wie echte Spieler)
///   Event 12 → Soldaten rekrutieren
///   Event 15 → Soldaten-Marsch + HP (Kampf!)
///   Event 16 → Gebäude-Schaden (Angriff auf die Spielerinsel)
///   Event 22 → Schaden an Soldaten des Spielers
///   Event 14 → Schiff bewegen (Interpolation wie bei einem echten Client)
///
/// Der Bot hat KEINE Szene, KEINE GameObjects — er ist "nur das Gehirn eines Spielers".
/// Alles was du auf deiner Seite siehst, entsteht durch deine Empfänger-Logik.
/// </summary>
public class TestBotClient : MonoBehaviour
{
    private const string BotNickName = "TESTBOT";

    // Timings
    private const float StartDelay = 2.5f;
    private const float BuildStepInterval = 0.6f;
    private const float VillagerSnapshotInterval = 0.4f;
    private const float SoldierStateInterval = 0.1f;
    private const float ShipStateInterval = 0.1f;
    private const float AttackInterval = 0.8f;
    private const float SoldierSpawnDelay = 4f;

    private enum BotStage
    {
        Connecting,
        WaitingForRoom,
        BuildingBase,
        Simulating,
        Done
    }

    private LoadBalancingClient client;
    private BotCallbacks callbacks;

    private BotStage stage = BotStage.Connecting;
    private float stageTimer = StartDelay;
    private float stageStartTime;
    private int botActorNumber = -1;
    private int nextLocalId = 100; // bewusst NICHT bei 1 starten → IDs leicht als Bot erkennbar

    // Villager
    private struct BotVillager
    {
        public int id;
        public Vector2 pos;
        public Villager.Role role;
        public bool visible;
    }
    private readonly List<BotVillager> botVillagers = new List<BotVillager>();

    // Soldaten
    private struct BotSoldier
    {
        public int netId;
        public Vector2 pos;
        public SoldierType type;
        public float hp;
    }
    private readonly List<BotSoldier> botSoldiers = new List<BotSoldier>();

    private readonly List<string> buildPlanNames = new();
    private readonly List<Vector2> plannedPositions = new();
    private int buildStep;
    private float villagerTimer;
    private float soldierTimer;
    private float attackTimer;

    private int shipId;
    private Vector2 shipPos;
    private bool shipSpawned;
    private float shipTimer;

    private string statusLine = "Bot: verbinde...";

    // Bewegungs-Step pro Sync-Tick (im 0.1s-Tick => ~1.5 Einheiten Sekunde)
    private const float SoldierStepPerTick = 0.15f;
    private const float ShipStepPerTick = 0.12f;

    // ── Unity Lifecycle ──────────────────────────────────────────────────
    private void Start()
    {
        if (!PhotonNetwork.InRoom)
        {
            statusLine = "Bot: kein Raum — Abbruch";
            Shutdown();
            return;
        }

        client = new LoadBalancingClient();
        callbacks = new BotCallbacks(this);
        client.AddCallbackTarget(callbacks);

        // Exakt dieselben Verbindungsdaten wie der Host-Client (AppId, AppVersion, Region!)
        client.AppId = PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime;
        client.AppVersion = PhotonNetwork.NetworkingClient.AppVersion;

        AppSettings settings = new AppSettings
        {
            AppIdRealtime = client.AppId,
            AppVersion = client.AppVersion,
            UseNameServer = true,
        };
        if (!string.IsNullOrEmpty(PhotonNetwork.CloudRegion))
        {
            settings.FixedRegion = PhotonNetwork.CloudRegion;
        }
        else if (!string.IsNullOrEmpty(PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion))
        {
            settings.FixedRegion = PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion;
        }

        bool ok = client.ConnectUsingSettings(settings);
        statusLine = ok ? "Bot: verbinde zur Photon Cloud..." : "Bot: Connect komplett fehlgeschlagen!";
        Debug.Log($"[TestBot] Second connection started: {ok}");
        if (!ok)
        {
            Shutdown();
        }
    }

    private void Update()
    {
        if (client != null)
        {
            client.Service(); // wie jeder echte Client: Service-Loop pro Frame
        }

        if (stage == BotStage.BuildingBase)
        {
            stageTimer -= Time.deltaTime;
            if (stageTimer <= 0f && buildStep < buildPlanNames.Count)
            {
                stageTimer = BuildStepInterval;
                ExecuteBuildStep();
            }
        }

        if (stage == BotStage.Simulating)
        {
            RunBotBrain();
        }
    }

    public string GetStatusLine() => statusLine;

    public void Shutdown()
    {
        stage = BotStage.Done;
        var cl = client;
        if (cl != null)
        {
            try
            {
                if (cl.State != ClientState.Disconnected && cl.State != ClientState.Disconnecting)
                {
                    cl.Disconnect();
                }
            }
            catch { /* wir sind eh am runterfahren */ }
        }
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (client != null && callbacks != null)
        {
            client.RemoveCallbackTarget(callbacks);
        }
        if (client != null && client.State != ClientState.Disconnected)
        {
            client.Disconnect();
        }
    }

    private void OnApplicationQuit()
    {
        if (client != null && client.State != ClientState.Disconnected)
        {
            client.Disconnect();
        }
    }

    // ── Join fertig — Bauplan ───────────────────────────────────────────
    internal void OnBotJoinedRoom()
    {
        botActorNumber = client.LocalPlayer.ActorNumber;
        stage = BotStage.BuildingBase;
        stageTimer = StartDelay;
        stageStartTime = Time.time;
        buildStep = 0;

        BuildPlan();
        statusLine = $"Bot: Actor {botActorNumber} im Raum, baue Basis...";
        Debug.Log($"[TestBot] Joined room as actor {botActorNumber}.");
    }

    /// <summary>Bauplan der Bot-Basis auf seiner Test-Insel (wie ein echter Spieler). </summary>
    private void BuildPlan()
    {
        buildPlanNames.Clear();
        plannedPositions.Clear();

        BuildingData warehouseData = FindBuildingData(d => d.isWarehouseType);
        BuildingData barracksData = FindBuildingData(d => d.isBarracks);
        BuildingData houseData = FindBuildingData(d => d.sleepCapacity > 0 && !d.isWarehouseType);
        BuildingData towerData = FindBuildingData(d => d.isDefenseTower);
        BuildingData shipData = FindBuildingData(d => d.placementRule == PlacementRule.Ship);

        Vector2 island = BotIsland();
        Vector2 dirToPlayer = (PlayerIsland() - island).normalized;
        Vector2 towardCenter = dirToPlayer * 6f;

        int i = 0;
        void AddPlan(BuildingData data, Vector2 offset)
        {
            if (data == null) return;
            buildPlanNames.Add(data.buildingName);
            plannedPositions.Add(island + offset);
            i++;
        }

        AddPlan(warehouseData, new Vector2(0f, 3f));
        AddPlan(houseData, new Vector2(-5f, 0f));
        AddPlan(houseData, new Vector2(5f, 0f));
        AddPlan(barracksData, new Vector2(3f, -4f));
        AddPlan(towerData, new Vector2(-3f, -5f));

        if (shipData != null)
        {
            // Schiff bekommt seine ID wie ein echter Spieler (actor*1M+n)
            shipId = NextId();
            buildPlanNames.Add("SHIP:" + shipData.buildingName);
            plannedPositions.Add(island + dirToPlayer * 8f);
        }
    }

    private void ExecuteBuildStep()
    {
        string buildName = buildPlanNames[buildStep];
        Vector2 pos = plannedPositions[Mathf.Min(buildStep, plannedPositions.Count - 1)];

        if (buildName.StartsWith("SHIP:"))
        {
            SendBuildingPlace(buildName.Substring(5), pos, shipId, 0);
            shipSpawned = true;
            shipPos = pos;
        }
        else
        {
            SendBuildingPlace(buildName, pos, 0, 0);
        }

        buildStep++;
        if (buildStep >= buildPlanNames.Count)
        {
            SpawnBotVillagers();
        }
    }

    // ── Der Bot-Loop (Simulationsphase) ──────────────────────────────────
    private void RunBotBrain()
    {
        villagerTimer -= Time.deltaTime;
        soldierTimer -= Time.deltaTime;
        shipTimer -= Time.deltaTime;
        attackTimer -= Time.deltaTime;

        // 1) Villager-Periodikum (Event 19) — exakt das Format des echten Managers
        if (villagerTimer <= 0f && botVillagers.Count > 0)
        {
            villagerTimer = VillagerSnapshotInterval;
            SimulateVillagers();
            SendVillagerSnapshot();
        }

        // 2) Soldaten: Spawn nach Rekrutierungszeit, danach dauerhaft bewegen + kämpfen
        if (botSoldiers.Count == 0 && Time.time - stageStartTime > SoldierSpawnDelay)
        {
            SpawnBotSoldiers();
        }

        if (botSoldiers.Count > 0 && soldierTimer <= 0f)
        {
            soldierTimer = SoldierStateInterval;
            SimulateSoldiersMovement();
            SendSoldierStates();
        }

        // 3) Schiff — segeln Richtung Spieler (Event 14, mit Bewegungs-Flag)
        if (shipSpawned && shipTimer <= 0f)
        {
            shipTimer = ShipStateInterval;
            SimulateShip();
            SendShipState();
        }

        // 4) Angriffsphase — Events exakt wie ein echter angreifender Client
        if (attackTimer <= 0f && botSoldiers.Count > 0)
        {
            attackTimer = AttackInterval;
            DoAttackPhase();
        }
    }

    // ── Sende-Funktionen (IDENTISCHE Payloads wie echte Clients) ────────
    private void SendBuildingPlace(string buildingName, Vector2 pos, int shipIdValue, int rotation)
    {
        BuildingData data = FindBuildingData(d => d.buildingName == buildingName);
        int w = data != null ? data.width : 1;
        int h = data != null ? data.height : 1;

        // Event 2: exakt wie PlacementManager.PlaceBuilding → {name, pos, rot, w, h, shipId}
        object[] content = new object[] { buildingName, pos, rotation, w, h, shipIdValue };
        RaiseEvent(2, content, true);
    }

    private void RaiseEvent(byte code, object payload, bool reliable)
    {
        if (client == null || client.State != ClientState.Joined) return;
        client.OpRaiseEvent(code, payload,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            new SendOptions { Reliability = reliable });
    }

    // ── Villager ─────────────────────────────────────────────────────────
    private void SpawnBotVillagers()
    {
        botVillagers.Clear();
        Vector2 island = BotIsland();
        for (int i = 0; i < 8; i++)
        {
            botVillagers.Add(new BotVillager
            {
                id = NextId(),
                pos = island + Random.insideUnitCircle * 5f,
                role = i < 2 ? Villager.Role.Worker : Villager.Role.Villager,
                visible = true,
            });
        }

        stage = BotStage.Simulating;
        stageStartTime = Time.time;
        statusLine = $"Bot: simuliert ({botVillagers.Count} Villager, {botSoldiers.Count} Soldaten)";
        SendVillagerSnapshot();
    }

    private void SimulateVillagers()
    {
        float hour = DayNightManager.Instance != null ? DayNightManager.Instance.currentHour : 12f;
        bool night = hour >= 19f || hour < 6f;

        Vector2 island = BotIsland();
        for (int i = 0; i < botVillagers.Count; i++)
        {
            BotVillager v = botVillagers[i];
            if (night && i % 2 == 0)
            {
                v.visible = false; // "schläft im Haus"
            }
            else
            {
                v.visible = true;
                v.pos += new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(-0.4f, 0.4f));
                Vector2 delta = v.pos - island;
                if (delta.sqrMagnitude > 40f) v.pos = island + delta.normalized * 5f;
            }
            botVillagers[i] = v;
        }
        statusLine = $"Bot: simuliert ({botVillagers.Count} Villager, {botSoldiers.Count} Soldaten)";
    }

    private void SendVillagerSnapshot()
    {
        // exakt das Event-19-Format aus VillagerManager.BroadcastVillagerState
        List<object> payload = new List<object>(botVillagers.Count * 5 + 1)
        {
            botVillagers.Count
        };
        foreach (var v in botVillagers)
        {
            payload.Add(v.id);
            payload.Add(v.pos.x);
            payload.Add(v.pos.y);
            payload.Add((int)v.role);
            payload.Add(v.visible ? 1 : 0);
        }
        RaiseEvent(VillagerManager.VillagerStateEventCode, payload.ToArray(), false);
    }

    // ── Soldaten ─────────────────────────────────────────────────────────
    private void SpawnBotSoldiers()
    {
        Vector2 island = BotIsland();
        SoldierType[] types = { SoldierType.Spear, SoldierType.Shield, SoldierType.Sword, SoldierType.Bow };

        for (int i = 0; i < 4; i++)
        {
            Vector2 pos = island + new Vector2(-2f + i, 3.5f);
            BotSoldier s = new BotSoldier
            {
                netId = NextId(),
                pos = pos,
                type = types[i % types.Length],
                hp = 100f,
            };
            botSoldiers.Add(s);

            // Event 12: exakt das Spawn-Format aus Soldier.SerializeSpawnEvent
            object[] spawn = new object[]
            {
                s.netId,
                (int)s.type,
                s.pos.x,
                s.pos.y,
                botActorNumber,
                (int)WeaponMaterial.Stone
            };
            RaiseEvent(12, spawn, true);
        }

        Debug.Log("[TestBot] 4 Soldaten rekrutiert → Marsch auf den Spieler!");
    }

    private void SimulateSoldiersMovement()
    {
        Vector2 playerIsland = PlayerIsland();

        for (int i = 0; i < botSoldiers.Count; i++)
        {
            BotSoldier s = botSoldiers[i];
            Vector2 target = playerIsland + new Vector2(i * 1.5f - 2.25f, -4f);

            if (soldierGroupPhase == 0 && Vector2.Distance(s.pos, target) < 7f)
            {
                soldierGroupPhase = 1; // in Reichweite → Angriffe an
            }

            s.pos = Vector2.MoveTowards(s.pos, target, SoldierStepPerTick);
            botSoldiers[i] = s;
        }
    }

    private int soldierGroupPhase;

    private void SendSoldierStates()
    {
        for (int i = 0; i < botSoldiers.Count; i++)
        {
            BotSoldier s = botSoldiers[i];
            // Event 15: exakt das Format aus Soldier.BroadcastState {netId, x, y, hp, shield}
            object[] state = new object[] { s.netId, s.pos.x, s.pos.y, s.hp, 0f };
            RaiseEvent(15, state, false);
        }
    }

    // ── Angriffe ─────────────────────────────────────────────────────────
    private void DoAttackPhase()
    {
        // 5a) SOLDAT vs SOLDAT (Event 22): Schaden an einen zufälligen Spieler-Soldaten
        List<Soldier> playerSoldiers = new List<Soldier>();
        foreach (var pair in NetCombat.SoldiersById)
        {
            if (pair.Value != null && NetCombat.OwnerFromId(pair.Key) != botActorNumber)
            {
                playerSoldiers.Add(pair.Value);
            }
        }
        if (playerSoldiers.Count > 0)
        {
            Soldier victim = playerSoldiers[Random.Range(0, playerSoldiers.Count)];
            // Event 22: exakt wie NetCombat.ApplySoldierDamage → {netId, amount(float)}
            RaiseEvent(NetCombat.SoldierDamageEvent, new object[] { victim.netId, Random.Range(8f, 16f) }, true);
        }

        // 5b) SOLDAT vs GEBÄUDE (Event 16 — exakt das Format aus BuildingInstance.TakeDamage)
        Vector2 soldiersCenter = Vector2.zero;
        foreach (var s in botSoldiers) soldiersCenter += s.pos;
        soldiersCenter /= Mathf.Max(1, botSoldiers.Count);

        BuildingInstance best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < BuildingInstance.AllBuildings.Count; i++)
        {
            BuildingInstance b = BuildingInstance.AllBuildings[i];
            if (b == null) continue;
            float d = Vector2.Distance(b.transform.position, soldiersCenter);
            if (d < bestDist)
            {
                bestDist = d;
                best = b;
            }
        }

        if (best != null && bestDist < 10f && soldierGroupPhase > 0)
        {
            object[] dmg = new object[]
            {
                best.data != null ? best.data.buildingName : best.name,
                best.transform.position.x,
                best.transform.position.y,
                (int)Random.Range(3, 9)
            };
            RaiseEvent(16, dmg, true);
            statusLine = $"Bot: greift {best.gameObject.name} an! ({botSoldiers.Count} Soldaten)";
        }
    }

    // ── Schiff ───────────────────────────────────────────────────────────
    private void SimulateShip()
    {
        Vector2 target = PlayerIsland() + new Vector2(3f, 6f);
        shipPos = Vector2.MoveTowards(shipPos, target, ShipStepPerTick);
    }

    private void SendShipState()
    {
        if (!shipSpawned) return;

        bool moving = Vector2.Distance(shipPos, PlayerIsland() + new Vector2(3f, 6f)) > 1.5f;

        // Event 14: exakt wie Ship.SendSyncEvent → {shipId, x, y, rot(float), flags(float), cargoUsed(int)}
        object[] state = new object[]
        {
            shipId,
            shipPos.x,
            shipPos.y,
            270f,
            moving ? 1f : 0f,
            3 // cargoUsed — wie ein beladenes Schiff
        };
        RaiseEvent(14, state, false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static BuildingData FindBuildingData(System.Func<BuildingData, bool> predicate)
    {
        BuildingData[] all = Resources.FindObjectsOfTypeAll<BuildingData>();
        foreach (var d in all)
        {
            if (d != null && predicate(d)) return d;
        }
        return null;
    }

    private static Vector2 IslandAt(int index)
    {
        if (TestModeManager.TestIslandLayout != null && TestModeManager.TestIslandLayout.Length > index)
        {
            return TestModeManager.TestIslandLayout[index];
        }
        return IslandManager.Instance != null ? IslandManager.Instance.GetIslandPosition(index) : Vector2.zero;
    }

    private static Vector2 BotIsland() => IslandAt(1);
    private static Vector2 PlayerIsland() => IslandAt(0);

    private int NextId()
    {
        nextLocalId++;
        return botActorNumber * 1000000 + nextLocalId;
    }

    private class BotCallbacks : IConnectionCallbacks, IMatchmakingCallbacks, IOnEventCallback
    {
        private readonly TestBotClient bot;

        public BotCallbacks(TestBotClient bot)
        {
            this.bot = bot;
        }

        public void OnConnected() { }

        public void OnConnectedToMaster()
        {
            Debug.Log("[TestBot] Mit Master Server verbunden. Trete Raum bei...");
            bot.statusLine = "Bot: Master OK, trete Raum bei...";

            // Nickname setzen, damit der Bot im Room-Log sichtbar ist (Player.NickName wird beim Join gesendet)
            bot.client.NickName = TestBotClient.BotNickName;

            var enterParams = new EnterRoomParams
            {
                RoomName = PhotonNetwork.CurrentRoom.Name,
                RoomOptions = new RoomOptions
                {
                    MaxPlayers = Mathf.Max(2, PhotonNetwork.CurrentRoom.MaxPlayers),
                    IsVisible = false,
                    IsOpen = true,
                },
            };
            bot.client.OpJoinOrCreateRoom(enterParams);
        }

        public void OnDisconnected(DisconnectCause cause)
        {
            Debug.LogWarning($"[TestBot] Disconnected: {cause}");
            bot.statusLine = $"Bot: getrennt ({cause})";
        }

        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string cause) { }

        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreatedRoom() { }
        public void OnCreateRoomFailed(short returnCode, string message) { bot.statusLine = $"Bot: CreateRoom fehlgeschlagen ({message})"; }
        public void OnJoinedRoom()
        {
            bot.OnBotJoinedRoom();
        }
        public void OnJoinRoomFailed(short returnCode, string message) { bot.statusLine = $"Bot: JoinRoom fehlgeschlagen ({message})"; }
        public void OnJoinRandomFailed(short returnCode, string message) { }
        public void OnLeftRoom() { }

        public void OnEvent(EventData photonEvent)
        {
            // Der Bot reagiert auf nichts — die Events der echten Spieler interessieren ihn nicht.
        }
    }
}
