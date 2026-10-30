using System.Collections.Generic;
using ExitGames.Client.Photon;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Manages the main gameplay loop and player spawning.
/// </summary>
public class GameManager : MonoBehaviourPunCallbacks, IOnEventCallback
{
    private const byte LobbyChatEventCode = 1;
    private const byte VillagerSpawnEventCode = 11;
    private const byte SoldierSpawnEventCode = 12;
    private const byte BuildingDestroyEventCode = 13;
    private const byte ShipSyncEventCode = 14;
    private const byte SoldierMoveEventCode = 15;
    private const byte BuildingDamageEventCode = 16;
    private const byte BuildingCaptureEventCode = 17;
    private const byte PierLineEventCode = 18;
    private const byte WorldStateRequestEventCode = 20;
    private const byte WorldStateResponseEventCode = 21;
    private readonly int maxChatMessages = 6;
    private readonly List<string> chatMessages = new List<string>();

    private TextMeshProUGUI chatLogText;
    private TMP_InputField chatInputField;
    private Button chatSendButton;
    private RectTransform chatPanelRoot;
    private GameObject chatLogArea;
    private GameObject chatInputArea;
    private GameObject chatToggleIcon;
    private NotificationManager notificationManager;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI gameStatusText;

    private void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    private void Start()
    {
        EnsureRequiredManagers();

        if (PhotonNetwork.InRoom)
        {
            SpawnPlayer();
            UpdateStatusText();
            BuildGameChatUI();
            notificationManager = NotificationManager.Instance;
        }
        else
        {
            if (gameStatusText != null) gameStatusText.text = "Game Started! (Offline Mode)";
            Debug.LogWarning("GameManager loaded, but client is not in a Photon room.");
        }

        // Nur Dummy-Soldaten im Testmodus – Spieler startet ohne
    }

    private void EnsureRequiredManagers()
    {
        EnsureManagerExists<GridManager>();
        EnsureManagerExists<IslandManager>();
        EnsureManagerExists<BuildingManager>();
        EnsureManagerExists<ResourceManager>();
        EnsureManagerExists<PlacementManager>();
        EnsureManagerExists<FogProjector>();
        EnsureManagerExists<SelectionManager>();
        EnsureManagerExists<VillagerManager>();
        EnsureManagerExists<NotificationManager>();
        EnsureManagerExists<AudioManager>();
        EnsureManagerExists<TradingManager>();
        EnsureManagerExists<TradingUI>();
    }

    private void EnsureManagerExists<T>() where T : MonoBehaviour
    {
        if (FindAnyObjectByType<T>() == null)
        {
            GameObject obj = new GameObject(typeof(T).Name);
            obj.AddComponent<T>();
            Debug.LogWarning($"[GameManager] Erstellte fehlenden Manager: {typeof(T).Name}");
        }
    }

    private void SpawnPlayer()
    {
        if (IslandManager.Instance == null)
        {
            Debug.LogError("IslandManager instance not found! Cannot calculate island spawn position.");
            return;
        }

        // Ensure islands are generated before we look up their positions
        IslandManager.Instance.GenerateIslands();

        // Check if we are in Test Mode
        bool isTestMode = false;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TestMode", out object testModeValue))
        {
            isTestMode = (bool)testModeValue;
        }

        // Get player index
        int playerIndex = 0;
        Player[] players = PhotonNetwork.PlayerList;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].IsLocal)
            {
                playerIndex = i;
                break;
            }
        }

        // Pick an island based on the player index
        Vector2 islandPos = IslandManager.Instance.GetIslandPosition(playerIndex);
        IslandType islandType = IslandManager.Instance.GetIslandType(playerIndex);
        
        Vector3 spawnPos = new Vector3(islandPos.x, islandPos.y, -10f);
        
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.InitializeResources(islandType);
        }
        
        Debug.Log($"[GameManager] Spawning at Island {playerIndex}: {spawnPos}");
        
        // Move camera for the local player
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Vector2 localIslandPos = IslandManager.Instance.GetIslandPosition(playerIndex);
            mainCam.transform.position = new Vector3(localIslandPos.x, localIslandPos.y, -10f);
            mainCam.transform.rotation = Quaternion.identity;
        }

        // Spawn warehouses for ALL players
        if (BuildingManager.Instance != null)
        {
            for (int i = 0; i < players.Length; i++)
            {
                Vector2 pos = IslandManager.Instance.GetIslandPosition(i);
                bool isLocal = players[i].IsLocal;
                BuildingManager.Instance.SpawnMainWarehouse(pos, isLocal);

                // If it's the local player, reveal the entire starting island
                if (isLocal)
                {
                    GameObject islandRevealer = new GameObject("StartIslandRevealer");
                    islandRevealer.transform.position = new Vector3(pos.x, pos.y, 0);
                    FogRevealer fr = islandRevealer.AddComponent<FogRevealer>();
                    fr.radius = 95f; // Large enough to cover the spawn island
                    fr.isLocalPlayer = true;
                    
                    // Also register it as fully explored
                    FogProjector.RegisterExploration(pos, 80f);

                    if (VillagerManager.Instance != null)
                    {
                        VillagerManager.Instance.SpawnStartingPopulation(playerIndex);
                    }

                    SyncLocalPopulation(playerIndex);
                }
            }

            // Testmodus: Dummy-Gegner auf einer anderen Insel spawnen
            if (isTestMode && players.Length < 2)
            {
                SpawnDummyPlayer();
            }

            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
            {
                RequestWorldState();
            }
        }
    }

    private void SpawnDummyPlayer()
    {
        int dummyIndex = 1;
        Vector2 dummyPos = IslandManager.Instance.GetIslandPosition(dummyIndex);

        BuildingManager.Instance.SpawnMainWarehouse(dummyPos, false);

        // Ein Holzhaus für den Gegner (als Testziel)
        if (BuildingManager.Instance != null)
        {
            BuildingData woodData = null;
            BuildingData[] allData = Resources.FindObjectsOfTypeAll<BuildingData>();
            foreach (var d in allData)
            {
                if (d.buildingName != null && d.buildingName.ToLower().Contains("holz"))
                {
                    woodData = d;
                    break;
                }
            }
            if (woodData != null)
            {
                BuildingManager.Instance.SpawnBuilding(woodData, dummyPos + new Vector2(0f, 4f), false);
            }
        }

        Debug.Log("[GameManager] Dummy-Gegner gespawnt (Lagerhaus + Holzhaus).");
    }

    private void SyncLocalPopulation(int islandIndex)
    {
        if (!PhotonNetwork.InRoom || VillagerManager.Instance == null) return;
        Vector2 islandPos = IslandManager.Instance.GetIslandPosition(islandIndex);
        List<object> spawnData = new List<object>();
        spawnData.Add(islandPos.x);
        spawnData.Add(islandPos.y);
        foreach (var v in VillagerManager.Instance.ActiveVillagers)
        {
            if (v == null || !v.isLocal) continue;
            spawnData.Add((int)v.role);
            spawnData.Add(v.transform.position.x);
            spawnData.Add(v.transform.position.y);
        }
        if (spawnData.Count <= 2) return;
        ExitGames.Client.Photon.SendOptions sendOpts = new ExitGames.Client.Photon.SendOptions { Reliability = true };
        PhotonNetwork.RaiseEvent(VillagerSpawnEventCode, spawnData.ToArray(),
            new Photon.Realtime.RaiseEventOptions { Receivers = Photon.Realtime.ReceiverGroup.Others }, sendOpts);
    }

    private void UpdateStatusText()
    {
        if (gameStatusText != null)
        {
            gameStatusText.text = $"Game Started! Players: {PhotonNetwork.CurrentRoom.PlayerCount}";
        }
    }

    private void BuildGameChatUI()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        RectTransform root = CreateRect("GameChatPanel", canvas.transform);
        chatPanelRoot = root;
        root.anchorMin = new Vector2(0.04f, 0.02f);
        root.anchorMax = new Vector2(0.36f, 0.24f);
        root.pivot = new Vector2(0f, 0f);
        root.anchoredPosition = Vector2.zero;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        Image bg = root.gameObject.AddComponent<Image>();
        bg.color = new Color(0.07f, 0.10f, 0.16f, 0.90f);
        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.76f, 0.70f, 0.45f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI title = CreateStaticLabel(root, "SPIEL-CHAT", 16f, new Vector2(12f, -10f), new Vector2(-24f, 22f));
        title.color = new Color(0.90f, 0.84f, 0.60f, 1f);
        title.alignment = TextAlignmentOptions.TopLeft;
        title.raycastTarget = false;

        RectTransform logRect = CreateRect("GameChatLog", root);
        chatLogArea = logRect.gameObject;
        logRect.anchorMin = new Vector2(0f, 0.30f);
        logRect.anchorMax = new Vector2(1f, 1f);
        logRect.pivot = new Vector2(0.5f, 1f);
        logRect.anchoredPosition = new Vector2(0f, -36f);
        logRect.offsetMin = new Vector2(12f, 0f);
        logRect.offsetMax = new Vector2(-12f, 0f);

        chatLogText = logRect.gameObject.AddComponent<TextMeshProUGUI>();
        chatLogText.text = "Spiel-Chat aktiv. Schreibe etwas, um Lastigkeit zu prüfen.";
        chatLogText.fontSize = 14f;
        chatLogText.alignment = TextAlignmentOptions.TopLeft;
        chatLogText.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        chatLogText.enableWordWrapping = true;
        chatLogText.overflowMode = TextOverflowModes.Truncate;
        chatLogText.raycastTarget = false;

        RectTransform inputRow = CreateRect("GameChatInputRow", root);
        chatInputArea = inputRow.gameObject;
        inputRow.anchorMin = new Vector2(0f, 0f);
        inputRow.anchorMax = new Vector2(1f, 0f);
        inputRow.pivot = new Vector2(0.5f, 0f);
        inputRow.anchoredPosition = new Vector2(0f, 10f);
        inputRow.sizeDelta = new Vector2(-24f, 36f);

        RectTransform inputFieldRect = CreateRect("GameChatInputField", inputRow);
        inputFieldRect.anchorMin = new Vector2(0f, 0f);
        inputFieldRect.anchorMax = new Vector2(0.72f, 1f);
        inputFieldRect.offsetMin = Vector2.zero;
        inputFieldRect.offsetMax = Vector2.zero;

        Image inputBg = inputFieldRect.gameObject.AddComponent<Image>();
        inputBg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        Outline inputOutline = inputFieldRect.gameObject.AddComponent<Outline>();
        inputOutline.effectColor = new Color(0.5f, 0.5f, 0.55f, 0.7f);
        inputOutline.effectDistance = new Vector2(1f, -1f);

        chatInputField = inputFieldRect.gameObject.AddComponent<TMP_InputField>();
        chatInputField.textViewport = inputFieldRect;
        chatInputField.textComponent = CreateInputText(inputFieldRect, "");
        chatInputField.placeholder = CreateInputText(inputFieldRect, "Nachricht...", true);
        chatInputField.characterLimit = 120;
        chatInputField.onSubmit.AddListener(OnChatInputEndEdit);

        RectTransform buttonRect = CreateRect("GameChatSendButton", inputRow);
        buttonRect.anchorMin = new Vector2(0.74f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        chatSendButton = buttonRect.gameObject.AddComponent<Button>();
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = new Color(0.24f, 0.34f, 0.18f, 1f);
        Outline buttonOutline = buttonRect.gameObject.AddComponent<Outline>();
        buttonOutline.effectColor = new Color(0.80f, 0.70f, 0.40f, 1f);
        buttonOutline.effectDistance = new Vector2(2f, -2f);
        chatSendButton.onClick.AddListener(SubmitChatInput);

        TextMeshProUGUI buttonText = CreateCenteredButtonText(buttonRect.transform, "Senden");
        buttonText.fontSize = 14f;
        buttonText.color = new Color(0.96f, 0.96f, 0.96f, 1f);
        buttonText.alignment = TextAlignmentOptions.Center;

        AddChatMessage("Spiel-Chat bereit. Wenn du den Gast siehst, bist du in derselben Lobby.");
        CreateChatCloseButton(root);

        chatPanelRoot.gameObject.SetActive(false);
        CreateChatToggleIcon(canvas.transform);
    }

    private void OnChatInputEndEdit(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        SubmitChatInput();
    }

    private void SubmitChatInput()
    {
        if (chatInputField == null) return;
        string message = chatInputField.text.Trim();
        if (string.IsNullOrEmpty(message)) return;
        SendChatMessage(message);
        chatInputField.text = string.Empty;
        chatInputField.ActivateInputField();
    }

    private void SendChatMessage(string message)
    {
        if (!PhotonNetwork.InRoom) return;
        string sender = string.IsNullOrEmpty(PhotonNetwork.NickName) ? "Spieler" : PhotonNetwork.NickName;
        string payload = $"[{sender}] {message}";

        RaiseEventOptions options = new RaiseEventOptions { Receivers = ReceiverGroup.All };
        SendOptions sendOptions = new SendOptions { Reliability = true };
        PhotonNetwork.RaiseEvent(LobbyChatEventCode, payload, options, sendOptions);
    }

    private void AddChatMessage(string message)
    {
        chatMessages.Add(message);
        if (chatMessages.Count > maxChatMessages)
        {
            chatMessages.RemoveAt(0);
        }

        if (chatLogText != null)
        {
            chatLogText.text = string.Join("\n", chatMessages);
        }
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code == LobbyChatEventCode && photonEvent.CustomData is string message)
        {
            AddChatMessage(message);
            if (chatPanelRoot != null && !chatPanelRoot.gameObject.activeSelf && notificationManager != null)
            {
                notificationManager.Notify("chat", $"Neue Nachricht: {message}");
            }
            return;
        }

        if (photonEvent.Code == VillagerSpawnEventCode && photonEvent.CustomData is object[] vData)
        {
            ReceiveVillagerSpawn(vData);
            return;
        }

        if (photonEvent.Code == SoldierSpawnEventCode && photonEvent.CustomData is object[] sData)
        {
            ReceiveSoldierSpawn(sData);
            return;
        }

        if (photonEvent.Code == BuildingDestroyEventCode && photonEvent.CustomData is object[] bData)
        {
            ReceiveBuildingDestroy(bData);
            return;
        }

        if (photonEvent.Code == ShipSyncEventCode && photonEvent.CustomData is object[] shipData)
        {
            ReceiveShipSync(shipData);
            return;
        }

        if (photonEvent.Code == SoldierMoveEventCode && photonEvent.CustomData is object[] moveData)
        {
            ReceiveSoldierMove(moveData);
            return;
        }

        if (photonEvent.Code == BuildingDamageEventCode && photonEvent.CustomData is object[] dmgData)
        {
            ReceiveBuildingDamage(dmgData);
            return;
        }

        if (photonEvent.Code == BuildingCaptureEventCode && photonEvent.CustomData is object[] capData)
        {
            ReceiveBuildingCapture(capData);
            return;
        }

        if (photonEvent.Code == PierLineEventCode && photonEvent.CustomData is object[] pierData)
        {
            ReceivePierLine(pierData);
            return;
        }

        if (photonEvent.Code == WorldStateRequestEventCode)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                SendWorldStateToPlayer(photonEvent.Sender);
            }
            return;
        }

        if (photonEvent.Code == WorldStateResponseEventCode && photonEvent.CustomData is ExitGames.Client.Photon.Hashtable stateData)
        {
            ReceiveWorldState(stateData);
            return;
        }
    }

    private void ReceiveVillagerSpawn(object[] data)
    {
        if (VillagerManager.Instance == null) return;
        float islandX = (float)data[0];
        float islandY = (float)data[1];
        for (int i = 2; i + 2 < data.Length; i += 3)
        {
            Villager.Role role = (Villager.Role)(int)data[i];
            float vx = (float)data[i + 1];
            float vy = (float)data[i + 2];
            VillagerManager.Instance.SpawnVillagerAt(new Vector2(vx, vy), role, false);
        }
    }

    private void ReceiveSoldierSpawn(object[] data)
    {
        int netId = (int)data[0];
        SoldierType type = (SoldierType)(int)data[1];
        float px = (float)data[2];
        float py = (float)data[3];
        SpawnRemoteSoldier(new Vector3(px, py, 0f), type, netId);
    }

    private void SpawnRemoteSoldier(Vector3 position, SoldierType type, int netId = 0)
    {
        GameObject solObj = new GameObject($"Remote_{type}");
        solObj.transform.position = position;
        var sr = solObj.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 21;
        solObj.AddComponent<BoxCollider2D>().size = new Vector2(1f, 1f);
        var s = solObj.AddComponent<Soldier>();
        s.netId = netId;
        s.soldierType = type;
        s.team = Team.Player;
        s.moveSpeed = 1.5f;
        FogRevealer fr = solObj.AddComponent<FogRevealer>();
        fr.radius = 4f;
        fr.isLocalPlayer = false;
    }

    private void ReceiveBuildingDestroy(object[] data)
    {
        string buildingName = (string)data[0];
        float bx = (float)data[1];
        float by = (float)data[2];
        Vector3 bPos = new Vector3(bx, by, -0.21f);
        var allBuildings = FindObjectsByType<BuildingInstance>();
        foreach (var b in allBuildings)
        {
            if (b == null) continue;
            if (b.data != null && b.data.buildingName == buildingName &&
                Vector3.Distance(b.transform.position, bPos) < 0.5f)
            {
                Destroy(b.gameObject);
                return;
            }
        }
    }

    private void ReceiveShipSync(object[] data)
    {
        float spawnX = (float)data[0];
        float spawnY = (float)data[1];
        float posX = (float)data[2];
        float posY = (float)data[3];
        float rot = (float)data[4];

        Ship[] ships = FindObjectsByType<Ship>();
        Vector2Int searchOrigin = new Vector2Int(Mathf.RoundToInt(spawnX), Mathf.RoundToInt(spawnY));
        foreach (Ship ship in ships)
        {
            if (ship.spawnOrigin == searchOrigin)
            {
                ship.transform.position = new Vector3(posX, posY, ship.transform.position.z);
                ship.transform.rotation = Quaternion.Euler(0f, 0f, rot);
                return;
            }
        }
    }

    private void ReceiveSoldierMove(object[] data)
    {
        int netId = (int)data[0];
        float px = (float)data[1];
        float py = (float)data[2];

        foreach (Soldier s in Soldier.ActiveSoldiers)
        {
            if (s.netId == netId)
            {
                s.transform.position = new Vector3(px, py, s.transform.position.z);
                return;
            }
        }
    }

    private void ReceiveBuildingDamage(object[] data)
    {
        string buildingName = (string)data[0];
        float bx = (float)data[1];
        float by = (float)data[2];
        int amount = (int)data[3];
        Vector3 bPos = new Vector3(bx, by, -0.21f);

        var allBuildings = FindObjectsByType<BuildingInstance>();
        foreach (var b in allBuildings)
        {
            if (b == null) continue;
            if (b.data != null && b.data.buildingName == buildingName &&
                Vector3.Distance(b.transform.position, bPos) < 0.5f)
            {
                b.TakeDamage(amount);
                return;
            }
        }
    }

    private void ReceiveBuildingCapture(object[] data)
    {
        string buildingName = (string)data[0];
        float bx = (float)data[1];
        float by = (float)data[2];
        Vector3 bPos = new Vector3(bx, by, -0.21f);

        var allBuildings = FindObjectsByType<BuildingInstance>();
        BuildingInstance wb = null;
        foreach (var b in allBuildings)
        {
            if (b == null) continue;
            if (b.data != null && b.data.buildingName == buildingName &&
                Vector3.Distance(b.transform.position, bPos) < 0.5f)
            {
                wb = b;
                break;
            }
        }

        Vector2Int origin;
        if (wb != null)
        {
            origin = new Vector2Int(Mathf.RoundToInt(wb.transform.position.x), Mathf.RoundToInt(wb.transform.position.y));
        }
        else
        {
            origin = new Vector2Int(Mathf.RoundToInt(bx), Mathf.RoundToInt(by));
        }

        var islandCells = BuildingManager.FloodFillIsland(origin);
        foreach (var b in allBuildings)
        {
            if (b == null || b == wb) continue;
            Vector2Int bGrid = new Vector2Int(Mathf.RoundToInt(b.transform.position.x), Mathf.RoundToInt(b.transform.position.y));
            if (islandCells.Contains(bGrid))
            {
                b.isLocal = !b.isLocal;
                b.UpdateHealthBar();
            }
        }
    }

    private void ReceivePierLine(object[] data)
    {
        if (BuildingManager.Instance == null) return;
        if (data.Length < 4) return;

        float sx = (float)data[0];
        float sy = (float)data[1];
        float ex = (float)data[2];
        float ey = (float)data[3];

        Vector2Int start = new Vector2Int(Mathf.RoundToInt(sx), Mathf.RoundToInt(sy));
        Vector2Int end = new Vector2Int(Mathf.RoundToInt(ex), Mathf.RoundToInt(ey));

        List<Vector2Int> cells = GetPierLineCells(start, end);
        foreach (Vector2Int cell in cells)
        {
            BuildingManager.Instance.PlaceStegAt(new Vector2(cell.x, cell.y), false, true);
        }
    }

    private List<Vector2Int> GetPierLineCells(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        int x = start.x, y = start.y;
        int dx = Mathf.Abs(end.x - start.x);
        int dy = -Mathf.Abs(end.y - start.y);
        int sx = start.x < end.x ? 1 : -1;
        int sy = start.y < end.y ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            cells.Add(new Vector2Int(x, y));
            if (x == end.x && y == end.y) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        return cells;
    }

    private void CreateChatCloseButton(RectTransform parent)
    {
        RectTransform btnRect = CreateRect("GameChatCloseButton", parent);
        btnRect.anchorMin = new Vector2(1f, 1f);
        btnRect.anchorMax = new Vector2(1f, 1f);
        btnRect.pivot = new Vector2(1f, 1f);
        btnRect.anchoredPosition = new Vector2(-10f, -8f);
        btnRect.sizeDelta = new Vector2(28f, 28f);

        Image btnImage = btnRect.gameObject.AddComponent<Image>();
        btnImage.color = new Color(0.16f, 0.20f, 0.26f, 0.95f);
        btnImage.raycastTarget = true;

        Button closeBtn = btnRect.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = btnImage;
        closeBtn.onClick.AddListener(CloseChatPanel);

        TextMeshProUGUI txt = CreateCenteredButtonText(btnRect.transform, "X");
        txt.fontSize = 16f;
        txt.color = new Color(0.90f, 0.84f, 0.60f, 1f);
        txt.raycastTarget = false;

        btnRect.SetAsLastSibling();
    }

    private void CreateChatToggleIcon(Transform canvasTransform)
    {
        RectTransform iconRect = CreateRect("ChatToggleIcon", canvasTransform);
        iconRect.anchorMin = new Vector2(0.04f, 0.02f);
        iconRect.anchorMax = new Vector2(0.04f, 0.02f);
        iconRect.pivot = new Vector2(0f, 0f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(56f, 56f);

        Image iconImage = iconRect.gameObject.AddComponent<Image>();
        iconImage.raycastTarget = true;

        Texture2D chatTex = Resources.Load<Texture2D>("chat-icon");
        Sprite chatSprite = chatTex != null ? Sprite.Create(chatTex, new Rect(0, 0, chatTex.width, chatTex.height), new Vector2(0.5f, 0.5f)) : null;
        if (chatSprite != null)
        {
            iconImage.sprite = chatSprite;
            iconImage.preserveAspect = true;
        }
        else
        {
            iconImage.color = new Color(0.16f, 0.20f, 0.26f, 0.95f);
            TextMeshProUGUI fallback = CreateCenteredButtonText(iconRect, "Chat");
            fallback.fontSize = 12f;
        }

        Button toggleBtn = iconRect.gameObject.AddComponent<Button>();
        toggleBtn.targetGraphic = iconImage;
        toggleBtn.onClick.AddListener(ToggleChatPanel);

        chatToggleIcon = iconRect.gameObject;
    }

    private void ToggleChatPanel()
    {
        if (chatPanelRoot == null || chatToggleIcon == null) return;
        bool show = !chatPanelRoot.gameObject.activeSelf;
        chatPanelRoot.gameObject.SetActive(show);
        chatToggleIcon.SetActive(!show);

        if (show && chatInputField != null)
        {
            chatInputField.ActivateInputField();
        }
    }

    private void CloseChatPanel()
    {
        if (chatPanelRoot != null) chatPanelRoot.gameObject.SetActive(false);
        if (chatToggleIcon != null) chatToggleIcon.SetActive(true);
    }

    private void Update()
    {
        if (chatPanelRoot == null || chatToggleIcon == null) return;
        if (Keyboard.current == null || !Keyboard.current.tKey.wasPressedThisFrame) return;

        ToggleChatPanel();

        if (chatPanelRoot.gameObject.activeSelf && chatInputField != null)
        {
            chatInputField.ActivateInputField();
        }
    }

    private TextMeshProUGUI CreateInputText(RectTransform parent, string value, bool isPlaceholder = false)
    {
        RectTransform rect = CreateRect(isPlaceholder ? "Placeholder" : "InputText", parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8f, 6f);
        rect.offsetMax = new Vector2(-8f, -6f);

        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = 14f;
        text.alignment = TextAlignmentOptions.Left;
        text.color = isPlaceholder ? new Color(0.68f, 0.68f, 0.68f, 1f) : new Color(0.95f, 0.95f, 0.95f, 1f);
        text.enableWordWrapping = false;
        return text;
    }

    private RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private TextMeshProUGUI CreateStaticLabel(RectTransform parent, string textValue, float size, Vector2 pos, Vector2 height)
    {
        RectTransform rect = CreateRect(textValue + "_Label", parent);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = height;

        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = textValue;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = new Color(0.90f, 0.84f, 0.60f, 1f);
        text.enableWordWrapping = true;
        return text;
    }

    private TextMeshProUGUI CreateCenteredButtonText(Transform parent, string value)
    {
        RectTransform rect = CreateRect("Text", parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(0.96f, 0.96f, 0.96f, 1f);
        return text;
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdateStatusText();
        Debug.Log($"{newPlayer.NickName} joined the room.");

        if (IslandManager.Instance != null && IslandManager.Instance.IsGenerated && BuildingManager.Instance != null)
        {
            int newIndex = -1;
            Player[] players = PhotonNetwork.PlayerList;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].ActorNumber == newPlayer.ActorNumber)
                {
                    newIndex = i;
                    break;
                }
            }

            if (newIndex >= 0)
            {
                Vector2 pos = IslandManager.Instance.GetIslandPosition(newIndex);
                BuildingManager.Instance.SpawnMainWarehouse(pos, false);
                Debug.Log($"[GameManager] Spawned warehouse for late-joiner {newPlayer.NickName} (island {newIndex})");
            }
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdateStatusText();
        Debug.Log($"{otherPlayer.NickName} left the room.");
    }

    public void ReturnToMenu()
    {
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.StartMenuScene);
        }
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"Disconnected from Photon. Cause: {cause}");
        UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.StartMenuScene);
    }

    private void SpawnInitialSoldiers()
    {
        Vector3 spawnPos;
        Warehouse wh = FindAnyObjectByType<Warehouse>();
        if (wh != null)
        {
            spawnPos = wh.transform.position + new Vector3(2f, 0f, 0f);
        }
        else if (IslandManager.Instance != null)
        {
            Vector2 islandPos = IslandManager.Instance.GetIslandPosition(0);
            spawnPos = new Vector3(islandPos.x + 2f, islandPos.y, 0f);
        }
        else
        {
            return;
        }

        SoldierType[] types = { SoldierType.Spear, SoldierType.Shield, SoldierType.Sword, SoldierType.Bow };

        foreach (var type in types)
        {
            for (int i = 0; i < 2; i++)
            {
                GameObject solObj = new GameObject($"Init_{type}_{i}");
                Vector3 offset = new Vector3(i * 1.2f, (int)type * 1.2f, 0f);
                solObj.transform.position = spawnPos + offset;

                var sr = solObj.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 21;
                solObj.AddComponent<BoxCollider2D>().size = new Vector2(1f, 1f);

                var s = solObj.AddComponent<Soldier>();
                s.soldierType = type;
                s.team = Team.Player;
                s.moveSpeed = 1.5f;
            }
        }

        Debug.Log("[GameManager] 8 Start-Soldaten gespawnt (2 pro Typ).");

        SyncInitialSoldiers(spawnPos, types);
    }

    private void SyncInitialSoldiers(Vector3 basePos, SoldierType[] types)
    {
        if (!PhotonNetwork.InRoom) return;
        List<object> data = new List<object>();
        data.Add(basePos.x);
        data.Add(basePos.y);
        foreach (var type in types)
        {
            for (int i = 0; i < 2; i++)
            {
                data.Add((int)type);
                data.Add(i * 1.2f);
                data.Add((int)type * 1.2f);
            }
        }
        ExitGames.Client.Photon.SendOptions sendOpts = new ExitGames.Client.Photon.SendOptions { Reliability = true };
        PhotonNetwork.RaiseEvent(SoldierSpawnEventCode, data.ToArray(),
            new Photon.Realtime.RaiseEventOptions { Receivers = Photon.Realtime.ReceiverGroup.Others }, sendOpts);
    }

    private void RequestWorldState()
    {
        Debug.Log("[GameManager] Requesting world state from MasterClient...");
        PhotonNetwork.RaiseEvent(WorldStateRequestEventCode, null, 
            new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, 
            SendOptions.SendReliable);
    }

    private void SendWorldStateToPlayer(Player targetPlayer)
    {
        ExitGames.Client.Photon.Hashtable state = new ExitGames.Client.Photon.Hashtable();

        // 1. Buildings (excluding MyWarehouse, EnemyWarehouse)
        List<object> buildingsList = new List<object>();
        BuildingInstance[] allBuildings = FindObjectsByType<BuildingInstance>();
        foreach (var b in allBuildings)
        {
            if (b == null) continue;
            // Skip main warehouses (they are spawned by the late-joiner's SpawnPlayer loop)
            if (b.isPreBuiltLodging || b.name == "MyWarehouse" || b.name == "EnemyWarehouse") continue;

            buildingsList.Add(b.data != null ? b.data.buildingName : b.name);
            buildingsList.Add(b.transform.position.x);
            buildingsList.Add(b.transform.position.y);
            buildingsList.Add(Mathf.RoundToInt(b.transform.rotation.eulerAngles.z));
            buildingsList.Add(b.footprintWidthOverride);
            buildingsList.Add(b.footprintHeightOverride);
        }
        state.Add((byte)0, buildingsList.ToArray());

        // 2. Stegs (that do not have a BuildingInstance on the same gameobject)
        List<object> stegsList = new List<object>();
        Steg[] allStegs = FindObjectsByType<Steg>();
        foreach (var s in allStegs)
        {
            if (s == null) continue;
            if (s.GetComponent<BuildingInstance>() != null) continue; // Skip if part of a building

            stegsList.Add(s.transform.position.x);
            stegsList.Add(s.transform.position.y);
        }
        state.Add((byte)1, stegsList.ToArray());

        // 3. Villagers
        List<object> villagersList = new List<object>();
        Villager[] allVillagers = FindObjectsByType<Villager>();
        foreach (var v in allVillagers)
        {
            if (v == null) continue;
            villagersList.Add((int)v.role);
            villagersList.Add(v.transform.position.x);
            villagersList.Add(v.transform.position.y);
        }
        state.Add((byte)2, villagersList.ToArray());

        // 4. Soldiers
        List<object> soldiersList = new List<object>();
        foreach (var s in Soldier.ActiveSoldiers)
        {
            if (s == null) continue;
            soldiersList.Add(s.netId);
            soldiersList.Add((int)s.soldierType);
            soldiersList.Add(s.transform.position.x);
            soldiersList.Add(s.transform.position.y);
        }
        state.Add((byte)3, soldiersList.ToArray());

        // Send to targetPlayer only
        RaiseEventOptions opts = new RaiseEventOptions { TargetActors = new int[] { targetPlayer.ActorNumber } };
        PhotonNetwork.RaiseEvent(WorldStateResponseEventCode, state, opts, SendOptions.SendReliable);
        Debug.Log($"[GameManager] Sent world state to late-joiner: {targetPlayer.NickName}");
    }

    private void ReceiveWorldState(ExitGames.Client.Photon.Hashtable stateData)
    {
        Debug.Log("[GameManager] Received world state. Spawning entities...");

        // 1. Spawning buildings
        if (stateData.TryGetValue((byte)0, out object buildingsObj) && buildingsObj is object[] buildings)
        {
            for (int i = 0; i + 5 < buildings.Length; i += 6)
            {
                string name = (string)buildings[i];
                float x = (float)buildings[i + 1];
                float y = (float)buildings[i + 2];
                int rot = (int)buildings[i + 3];
                int w = (int)buildings[i + 4];
                int h = (int)buildings[i + 5];

                if (BuildingManager.Instance != null)
                {
                    BuildingData bData = BuildingManager.Instance.GetBuildingDataByName(name);
                    if (bData != null)
                    {
                        BuildingManager.Instance.SpawnBuilding(bData, new Vector2(x, y), false, rot, w, h);
                    }
                }
            }
        }

        // 2. Spawning stegs
        if (stateData.TryGetValue((byte)1, out object stegsObj) && stegsObj is object[] stegs)
        {
            for (int i = 0; i + 1 < stegs.Length; i += 2)
            {
                float x = (float)stegs[i];
                float y = (float)stegs[i + 1];
                if (BuildingManager.Instance != null)
                {
                    BuildingManager.Instance.PlaceStegAt(new Vector2(x, y), false, true);
                }
            }
        }

        // 3. Spawning villagers
        if (stateData.TryGetValue((byte)2, out object villagersObj) && villagersObj is object[] villagers)
        {
            for (int i = 0; i + 2 < villagers.Length; i += 3)
            {
                Villager.Role role = (Villager.Role)(int)villagers[i];
                float x = (float)villagers[i + 1];
                float y = (float)villagers[i + 2];
                if (VillagerManager.Instance != null)
                {
                    VillagerManager.Instance.SpawnVillagerAt(new Vector2(x, y), role, false);
                }
            }
        }

        // 4. Spawning soldiers
        if (stateData.TryGetValue((byte)3, out object soldiersObj) && soldiersObj is object[] soldiers)
        {
            for (int i = 0; i + 3 < soldiers.Length; i += 4)
            {
                int netId = (int)soldiers[i];
                SoldierType type = (SoldierType)(int)soldiers[i + 1];
                float x = (float)soldiers[i + 2];
                float y = (float)soldiers[i + 3];
                SpawnRemoteSoldier(new Vector3(x, y, 0f), type, netId);
            }
        }

        Debug.Log("[GameManager] World state spawned successfully.");
    }
}




