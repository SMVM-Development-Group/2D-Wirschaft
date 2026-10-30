using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviourPunCallbacks, IOnEventCallback
{
    private const byte LobbyChatEventCode = 1;
    private readonly int maxChatMessages = 8;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI roomInfoText;
    [SerializeField] private TextMeshProUGUI playerListText;
    [SerializeField] private TextMeshProUGUI masterClientNoteText;
    [SerializeField] private Button leaveRoomButton;
    [SerializeField] private Button startTestButton;
    [SerializeField] private JoinLogUI joinLogUI;
    [SerializeField] private GameObject chatLogTextPrefab;

    private readonly List<string> chatMessages = new List<string>();
    private TextMeshProUGUI chatLogText;
    private TMP_InputField chatInputField;
    private Button chatSendButton;
    private RectTransform chatPanelRoot;
    private GameObject chatLogArea;
    private GameObject chatInputArea;
    private TextMeshProUGUI chatMinimizeButtonText;
    private bool chatIsMinimized;
    private readonly Vector2 chatExpandedAnchorMax = new Vector2(0.96f, 0.28f);
    private const float chatMinimizedAnchorMaxY = 0.08f;

    [Header("Speed Settings")]
    [SerializeField] private Sprite pfeil1;
    [SerializeField] private Sprite pfeil2;
    [SerializeField] private Sprite pfeil3;

    private readonly Color bgColor     = GameUITheme.ScreenBackdrop;
    private readonly Color panelColor  = GameUITheme.PanelBackground;
    private readonly Color accentColor = GameUITheme.AccentGold;
    private readonly Color labelColor  = GameUITheme.LabelParchment;

    private readonly int[] speedOptions = { 1, 2, 3 };
    private readonly float[] dayLengthOptions = { 60f, 90f, 150f };
    private readonly string[] dayLengthLabels = { "Kurz", "Normal", "Lang" };
    private readonly int[] villagerOptions = { 6, 10, 14 };
    private readonly int[] workerOptions = { 1, 2, 4 };
    private readonly string[] worldSizeLabels = { "Kompakt", "Standard", "Gross" };

    private Button lobbyStartButton;
    private TextMeshProUGUI startBtnText;
    private RectTransform settingsRoot;
    private TextMeshProUGUI settingsSummaryText;

    private Button[] speedButtons;
    private Button[] dayLengthButtons;
    private Button[] villagerButtons;
    private Button[] workerButtons;
    private Button[] worldSizeButtons;
    private Button[] debugModeButtons;

    private new void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private new void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    private void Start()
    {
        // Direkt in der LobbyScene gestartet (z.B. Play ohne MainMenu-Pfad) bzw. verlorene Verbindung?
        // Die Lobby ist nur mit einem echten Raum sinnvoll — sonst zeigt sie nur Platzhalter.
        if (!PhotonNetwork.InRoom)
        {
            Debug.Log("[Lobby] Keine aktive Lobby — zurück zum Hauptmenü.");
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.StartMenuScene);
            return;
        }

        PhotonNetwork.AutomaticallySyncScene = true;

        if (leaveRoomButton != null)
        {
            leaveRoomButton.onClick.RemoveAllListeners();
            leaveRoomButton.onClick.AddListener(OnLeaveRoomButtonClicked);
        }

        if (startTestButton != null)
        {
            startTestButton.onClick.RemoveAllListeners();
            startTestButton.onClick.AddListener(OnStartTestButtonClicked);
            startTestButton.gameObject.SetActive(false);
        }

        EnsureDefaultLobbySettings();
        StyleExistingUi();
        BuildLobbyLayout();
        UpdateLobbyUI();
        CheckAutoStart();
    }

    private void EnsureDefaultLobbySettings()
    {
        if (!PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
        var currentProps = PhotonNetwork.CurrentRoom.CustomProperties;

        if (!currentProps.ContainsKey(LobbySettingsKeys.GameSpeed)) props[LobbySettingsKeys.GameSpeed] = 1;
        if (!currentProps.ContainsKey(LobbySettingsKeys.DayLength)) props[LobbySettingsKeys.DayLength] = 90f;
        if (!currentProps.ContainsKey(LobbySettingsKeys.StartVillagers)) props[LobbySettingsKeys.StartVillagers] = 10;
        if (!currentProps.ContainsKey(LobbySettingsKeys.StartWorkers)) props[LobbySettingsKeys.StartWorkers] = 2;
        if (!currentProps.ContainsKey(LobbySettingsKeys.WorldSize)) props[LobbySettingsKeys.WorldSize] = "Standard";
        if (!currentProps.ContainsKey(LobbySettingsKeys.MapSeed)) props[LobbySettingsKeys.MapSeed] = Random.Range(1, 1000000);
        if (!currentProps.ContainsKey(LobbySettingsKeys.DebugMode)) props[LobbySettingsKeys.DebugMode] = false;

        if (props.Count > 0)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
    }

    private void UpdateLobbyUI()
    {
        if (!PhotonNetwork.InRoom) return;

        Room currentRoom = PhotonNetwork.CurrentRoom;
        bool isTestMode = currentRoom.CustomProperties.ContainsKey("TestMode") && (bool)currentRoom.CustomProperties["TestMode"];

        roomInfoText.text = $"LOBBY {currentRoom.Name}\n{currentRoom.PlayerCount}/{currentRoom.MaxPlayers} Spieler bereit";

        playerListText.text = "";
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            string role = player.IsMasterClient ? "Host" : "Mitspieler";
            string self = player.IsLocal ? "  •  Du" : "";
            playerListText.text += $"• {player.NickName}  ({role}){self}\n";
        }

        if (isTestMode)
        {
            playerListText.text += "• TESTBOT  (verbindet sich gleich wie ein echter Spieler)\n";
            if (startTestButton != null && PhotonNetwork.IsMasterClient)
            {
                startTestButton.gameObject.SetActive(true);
            }
        }

        if (masterClientNoteText != null)
        {
            masterClientNoteText.text = PhotonNetwork.IsMasterClient
                ? "Du bist Host. Stelle die Spielregeln ein und starte, wenn die Runde passt."
                : "Der Host richtet gerade die Runde ein. Deine Ansicht aktualisiert sich automatisch.";
        }

        UpdateSettingsSummary();
        UpdateSettingsButtons();
        UpdateSpeedUI();
    }

    private void UpdateSettingsSummary()
    {
        if (settingsSummaryText == null || !PhotonNetwork.InRoom)
        {
            return;
        }

        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        int speed = props.TryGetValue(LobbySettingsKeys.GameSpeed, out object speedObj) ? System.Convert.ToInt32(speedObj) : 1;
        float dayLength = props.TryGetValue(LobbySettingsKeys.DayLength, out object dayObj) ? System.Convert.ToSingle(dayObj) : 90f;
        int villagers = props.TryGetValue(LobbySettingsKeys.StartVillagers, out object villObj) ? System.Convert.ToInt32(villObj) : 10;
        int workers = props.TryGetValue(LobbySettingsKeys.StartWorkers, out object workObj) ? System.Convert.ToInt32(workObj) : 2;
        string worldSize = props.TryGetValue(LobbySettingsKeys.WorldSize, out object worldObj) ? worldObj.ToString() : "Standard";
        bool debugMode = props.TryGetValue(LobbySettingsKeys.DebugMode, out object debugObj) ? (bool)debugObj : false;

        settingsSummaryText.text = $"Tempo {speed}x   •   Tag {GetDayLengthLabel(dayLength)}   •   Start {villagers} Dorfbewohner / {workers} Bauarbeiter   •   Welt {worldSize}{(debugMode ? "   •   DEBUG" : "")}";
    }

    private float testAutoStartTimer = -1f;

    private void StartGameNow()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable {
            { LobbySettingsKeys.GameStarted, true },
            { LobbySettingsKeys.InitialPlayerCount, PhotonNetwork.CurrentRoom.PlayerCount }
        });
        PhotonNetwork.LoadLevel(SceneNames.GameScene);
    }

    private void OnStartTestButtonClicked()
    {
        StartGameNow();
    }

    public void OnLeaveRoomButtonClicked()
    {
        if (!PhotonNetwork.InRoom)
        {
            Debug.Log("[Lobby] LeaveRoom ignoriert — keine Lobby aktiv.");
            return;
        }

        if (leaveRoomButton != null) leaveRoomButton.interactable = false;
        PhotonNetwork.LeaveRoom();
    }

    private void CheckAutoStart()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

        bool isTestMode = PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("TestMode") && (bool)PhotonNetwork.CurrentRoom.CustomProperties["TestMode"];
        if (isTestMode)
        {
            // Test-Lobby startet automatisch kurz nach dem Erstellen — der echte TESTBOT
            // verbindet sich SEINE eigene Photon-Verbindung erst in der GameScene.
            if (testAutoStartTimer < 0f)
            {
                testAutoStartTimer = 2f;
                if (settingsSummaryText != null)
                    settingsSummaryText.text += "   →  Start in 2s...";
            }
        }
        else if (PhotonNetwork.CurrentRoom.PlayerCount == PhotonNetwork.CurrentRoom.MaxPlayers)
        {
            StartGameNow();
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        joinLogUI?.LogPlayerJoin(newPlayer.NickName);
        UpdateLobbyUI();
        CheckAutoStart();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdateLobbyUI();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        UpdateLobbyUI();
        CheckAutoStart();
    }

    public override void OnLeftRoom()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.StartMenuScene);
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged.ContainsKey(LobbySettingsKeys.GameSpeed) ||
            propertiesThatChanged.ContainsKey(LobbySettingsKeys.DayLength) ||
            propertiesThatChanged.ContainsKey(LobbySettingsKeys.StartVillagers) ||
            propertiesThatChanged.ContainsKey(LobbySettingsKeys.StartWorkers) ||
            propertiesThatChanged.ContainsKey(LobbySettingsKeys.WorldSize))
        {
            UpdateLobbyUI();
        }
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != LobbyChatEventCode) return;
        if (photonEvent.CustomData is string message)
        {
            AddChatMessage(message);
        }
    }

    private void BuildLobbyLayout()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        // Fix: Make sure canvas is always overlay so camera FOV doesn't clip/hide it
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        RemoveExistingRuntimeLayout(canvas.transform, "LobbyRuntimeLayout", "LobbyBackdrop");
        CreateBackdrop(canvas.transform);

        RectTransform root = CreateRect("LobbyRuntimeLayout", canvas.transform);
        Stretch(root);

        RectTransform leftCard = CreateCard(root, "LobbyLeftCard", new Vector2(0.05f, 0.10f), new Vector2(0.40f, 0.90f));
        RectTransform rightCard = CreateCard(root, "LobbyRightCard", new Vector2(0.43f, 0.10f), new Vector2(0.95f, 0.90f));

        CreateReadoutProxy(leftCard, "LobbyReadout", roomInfoText, new Vector2(18f, -18f), new Vector2(-18f, 62f), 28f, FontStyles.Bold, labelColor);
        CreateReadoutProxy(leftCard, "PlayersReadout", playerListText, new Vector2(18f, -94f), new Vector2(-18f, 300f), 20f, FontStyles.Normal, GameUITheme.LabelPrimary);
        CreateReadoutProxy(leftCard, "HostNoteReadout", masterClientNoteText, new Vector2(18f, -404f), new Vector2(-18f, 92f), 16f, FontStyles.Italic, GameUITheme.LabelSecondary);

        if (leaveRoomButton != null)
        {
            CreateButtonProxy(leftCard, "LeaveProxy", leaveRoomButton, "Lobby verlassen", GameUITheme.ButtonDanger, 16f, 56f);
        }

        if (joinLogUI != null && joinLogUI.transform is RectTransform joinLogRt)
        {
            RectTransform logPanel = CreateRect("JoinLogPanel", leftCard);
            logPanel.anchorMin = new Vector2(0f, 0f);
            logPanel.anchorMax = new Vector2(1f, 0f);
            logPanel.pivot = new Vector2(0.5f, 0f);
            logPanel.anchoredPosition = new Vector2(0f, 84f);
            logPanel.sizeDelta = new Vector2(-32f, 92f);
            Image logBg = logPanel.gameObject.AddComponent<Image>();
            logBg.color = GameUITheme.PanelBackgroundSoft;
            Outline logOutline = logPanel.gameObject.AddComponent<Outline>();
            logOutline.effectColor = GameUITheme.OutlineGold;
            logOutline.effectDistance = new Vector2(1f, -1f);
        }

        CreateChatPanel(leftCard);

        if (startTestButton != null)
        {
            CreateButtonProxy(leftCard, "TestProxy", startTestButton, "Testspiel starten", GameUITheme.ButtonConfirm, 82f, 50f);
        }

        CreateSettingsPanel(rightCard);
    }

    private void CreateSettingsPanel(RectTransform parent)
    {
        CreateStaticLabel(parent, "SPIEL SETTINGS", 24f, new Vector2(20f, -20f), new Vector2(-20f, 34f));

        settingsSummaryText = CreateStaticLabel(parent, "", 15f, new Vector2(20f, -60f), new Vector2(-20f, 44f));
        settingsSummaryText.color = GameUITheme.LabelPrimary;

        settingsRoot = CreateRect("SettingsRoot", parent);
        settingsRoot.anchorMin = new Vector2(0f, 0f);
        settingsRoot.anchorMax = new Vector2(1f, 1f);
        settingsRoot.offsetMin = new Vector2(18f, 100f);
        settingsRoot.offsetMax = new Vector2(-18f, -78f);

        speedButtons = CreateOptionRow(settingsRoot, 0, "Spieltempo", new[] { "1x", "2x", "3x" }, index => SetGameSpeed(speedOptions[index]));
        dayLengthButtons = CreateOptionRow(settingsRoot, 1, "Taglaenge", dayLengthLabels, index => SetDayLength(dayLengthOptions[index]));
        villagerButtons = CreateOptionRow(settingsRoot, 2, "Start Dorfbewohner", new[] { "6", "10", "14" }, index => SetStartVillagers(villagerOptions[index]));
        workerButtons = CreateOptionRow(settingsRoot, 3, "Start Bauarbeiter", new[] { "1", "2", "4" }, index => SetStartWorkers(workerOptions[index]));
        worldSizeButtons = CreateOptionRow(settingsRoot, 4, "Weltgroesse", worldSizeLabels, index => SetWorldSize(worldSizeLabels[index]));
        debugModeButtons = CreateOptionRow(settingsRoot, 5, "Debug Modus", new[] { "Aus", "An" }, index => SetDebugMode(index == 1));

        CreateStartButton(parent);
    }

    private Button[] CreateOptionRow(RectTransform parent, int rowIndex, string label, string[] options, System.Action<int> onChoose)
    {
        float top = -rowIndex * 84f;
        CreateStaticLabel(parent, label.ToUpper(), 16f, new Vector2(6f, top), new Vector2(-6f, 22f));

        RectTransform row = CreateRect(label + "_Row", parent);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, top - 24f);
        row.sizeDelta = new Vector2(0f, 48f);

        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = true;

        Button[] buttons = new Button[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            int localIndex = i;
            buttons[i] = CreateOptionButton(row, options[i], () => onChoose(localIndex));
        }

        return buttons;
    }

    private Button CreateOptionButton(RectTransform parent, string text, UnityEngine.Events.UnityAction action)
    {
        GameObject btnGO = new GameObject(text + "_Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGO.transform.SetParent(parent, false);
        LayoutElement le = btnGO.GetComponent<LayoutElement>();
        le.minHeight = 46f;
        le.minWidth = 0f;
        Image img = btnGO.GetComponent<Image>();
        img.color = GameUITheme.ButtonPressed;
        Outline outline = btnGO.AddComponent<Outline>();
        outline.effectColor = GameUITheme.OutlineGoldStrong;
        outline.effectDistance = new Vector2(2f, -2f);

        Button btn = btnGO.GetComponent<Button>();
        btn.onClick.AddListener(action);

        TextMeshProUGUI txt = CreateCenteredButtonText(btnGO.transform, text);
        txt.fontSize = 16f;
        txt.color = labelColor;
        return btn;
    }

    private void CreateStartButton(RectTransform parent)
    {
        GameObject startBtnGO = new GameObject("LobbyStartGameBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        startBtnGO.transform.SetParent(parent, false);
        RectTransform startBtnRT = startBtnGO.GetComponent<RectTransform>();
        startBtnRT.anchorMin = new Vector2(0f, 0f);
        startBtnRT.anchorMax = new Vector2(1f, 0f);
        startBtnRT.pivot = new Vector2(0.5f, 0f);
        startBtnRT.anchoredPosition = new Vector2(0f, 16f);
        startBtnRT.sizeDelta = new Vector2(-36f, 56f);

        Image startBtnImg = startBtnGO.GetComponent<Image>();
        startBtnImg.color = GameUITheme.ButtonConfirm;
        Outline outline = startBtnGO.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);

        startBtnText = CreateCenteredButtonText(startBtnGO.transform, "SPIEL STARTEN");
        startBtnText.fontSize = 18f;
        startBtnText.color = labelColor;

        lobbyStartButton = startBtnGO.GetComponent<Button>();
        lobbyStartButton.onClick.AddListener(OnLobbyStartButtonClicked);
    }

    private void CreateChatPanel(RectTransform parent)
    {
        RectTransform chatRoot = CreateRect("LobbyChatPanel", parent);
        chatPanelRoot = chatRoot;
        chatRoot.anchorMin = new Vector2(0.04f, 0.02f);
        chatRoot.anchorMax = chatExpandedAnchorMax;
        chatRoot.pivot = new Vector2(0.5f, 0f);
        chatRoot.anchoredPosition = Vector2.zero;
        chatRoot.offsetMin = Vector2.zero;
        chatRoot.offsetMax = Vector2.zero;

        Image chatBg = chatRoot.gameObject.AddComponent<Image>();
        chatBg.color = GameUITheme.PanelBackground;
        Outline chatOutline = chatRoot.gameObject.AddComponent<Outline>();
        chatOutline.effectColor = accentColor;
        chatOutline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI title = CreateStaticLabel(chatRoot, "LOBBY-CHAT", 16f, new Vector2(14f, -10f), new Vector2(-28f, 22f));
        title.color = GameUITheme.LabelParchment;
        title.alignment = TextAlignmentOptions.TopLeft;
        title.raycastTarget = false;

        RectTransform logRect = CreateRect("ChatLog", chatRoot);
        chatLogArea = logRect.gameObject;
        logRect.anchorMin = new Vector2(0f, 0.28f);
        logRect.anchorMax = new Vector2(1f, 1f);
        logRect.pivot = new Vector2(0.5f, 1f);
        logRect.anchoredPosition = new Vector2(0f, -36f);
        logRect.offsetMin = new Vector2(12f, 0f);
        logRect.offsetMax = new Vector2(-12f, 0f);

        chatLogText = logRect.gameObject.AddComponent<TextMeshProUGUI>();
        chatLogText.text = "Schreibe eine Nachricht, um den Lobby-Chat zu testen.";
        chatLogText.fontSize = 14f;
        chatLogText.alignment = TextAlignmentOptions.TopLeft;
        chatLogText.color = GameUITheme.LabelPrimary;
        chatLogText.textWrappingMode = TextWrappingModes.Normal;
        chatLogText.overflowMode = TextOverflowModes.Truncate;
        chatLogText.raycastTarget = false;

        RectTransform inputRow = CreateRect("ChatInputRow", chatRoot);
        chatInputArea = inputRow.gameObject;
        inputRow.anchorMin = new Vector2(0f, 0f);
        inputRow.anchorMax = new Vector2(1f, 0f);
        inputRow.pivot = new Vector2(0.5f, 0f);
        inputRow.anchoredPosition = new Vector2(0f, 12f);
        inputRow.sizeDelta = new Vector2(-24f, 36f);

        RectTransform inputFieldRect = CreateRect("ChatInputField", inputRow);
        inputFieldRect.anchorMin = new Vector2(0f, 0f);
        inputFieldRect.anchorMax = new Vector2(0.72f, 1f);
        inputFieldRect.offsetMin = Vector2.zero;
        inputFieldRect.offsetMax = Vector2.zero;

        Image inputBg = inputFieldRect.gameObject.AddComponent<Image>();
        inputBg.color = GameUITheme.InputBackground;
        Outline inputOutline = inputFieldRect.gameObject.AddComponent<Outline>();
        inputOutline.effectColor = GameUITheme.InputOutline;
        inputOutline.effectDistance = new Vector2(1f, -1f);

        chatInputField = inputFieldRect.gameObject.AddComponent<TMP_InputField>();
        chatInputField.textViewport = inputFieldRect;
        chatInputField.textComponent = CreateInputText(inputFieldRect, "");
        chatInputField.placeholder = CreateInputText(inputFieldRect, "Nachricht...", true);
        chatInputField.characterLimit = 120;
        chatInputField.onSubmit.AddListener(OnChatInputEndEdit);

        RectTransform buttonRect = CreateRect("ChatSendButton", inputRow);
        buttonRect.anchorMin = new Vector2(0.74f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        chatSendButton = buttonRect.gameObject.AddComponent<Button>();
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = GameUITheme.ButtonConfirm;
        Outline buttonOutline = buttonRect.gameObject.AddComponent<Outline>();
        buttonOutline.effectColor = accentColor;
        buttonOutline.effectDistance = new Vector2(2f, -2f);
        chatSendButton.onClick.AddListener(SubmitChatInput);

        TextMeshProUGUI buttonText = CreateCenteredButtonText(buttonRect.transform, "Senden");
        buttonText.fontSize = 14f;
        buttonText.color = labelColor;
        buttonText.alignment = TextAlignmentOptions.Center;

        AddChatMessage("Lobby-Chat aktiv. Schreibe hier, um zu prüfen, ob du in derselben Lobby bist.");
        CreateChatMinimizeButton(chatRoot);
    }

    private void CreateChatMinimizeButton(RectTransform parent)
    {
        RectTransform btnRect = CreateRect("ChatMinimizeButton", parent);
        btnRect.anchorMin = new Vector2(1f, 1f);
        btnRect.anchorMax = new Vector2(1f, 1f);
        btnRect.pivot = new Vector2(1f, 1f);
        btnRect.anchoredPosition = new Vector2(-10f, -8f);
        btnRect.sizeDelta = new Vector2(32f, 28f);

        Image btnImage = btnRect.gameObject.AddComponent<Image>();
        btnImage.color = GameUITheme.ButtonNormal;
        btnImage.raycastTarget = true;

        Button minimizeButton = btnRect.gameObject.AddComponent<Button>();
        minimizeButton.targetGraphic = btnImage;
        minimizeButton.onClick.AddListener(ToggleChatMinimized);

        chatMinimizeButtonText = CreateCenteredButtonText(btnRect.transform, "−");
        chatMinimizeButtonText.fontSize = 20f;
        chatMinimizeButtonText.color = accentColor;
        chatMinimizeButtonText.raycastTarget = false;

        btnRect.SetAsLastSibling();
    }

    private void ToggleChatMinimized()
    {
        chatIsMinimized = !chatIsMinimized;

        if (chatLogArea != null) chatLogArea.SetActive(!chatIsMinimized);
        if (chatInputArea != null) chatInputArea.SetActive(!chatIsMinimized);

        if (chatPanelRoot != null)
        {
            Vector2 anchorMax = chatPanelRoot.anchorMax;
            anchorMax.y = chatIsMinimized ? chatMinimizedAnchorMaxY : chatExpandedAnchorMax.y;
            chatPanelRoot.anchorMax = anchorMax;
        }

        if (chatMinimizeButtonText != null)
        {
            chatMinimizeButtonText.text = chatIsMinimized ? "+" : "−";
        }
    }

    private void Update()
    {
        // Test-Modus: Auto-Start (Host muss nichts klicken — der TESTBOT connectet sich selbst).
        if (testAutoStartTimer > 0f && PhotonNetwork.IsMasterClient)
        {
            testAutoStartTimer -= Time.deltaTime;
            if (testAutoStartTimer <= 0f)
            {
                testAutoStartTimer = -1f;
                StartGameNow();
            }
        }

        if (chatPanelRoot == null) return;
        if (Keyboard.current == null || !Keyboard.current.tKey.wasPressedThisFrame) return;
        if (chatInputField != null && chatInputField.isFocused) return;

        ToggleChatMinimized();

        if (!chatIsMinimized && chatInputField != null)
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
        text.color = isPlaceholder ? GameUITheme.LabelPlaceholder : GameUITheme.LabelPrimary;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
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

    private void OnChatInputEndEdit(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        SubmitChatInput();
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

    private void SetGameSpeed(int speed)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        SetRoomProperty(LobbySettingsKeys.GameSpeed, speed);
    }

    private void SetDayLength(float seconds)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        SetRoomProperty(LobbySettingsKeys.DayLength, seconds);
    }

    private void SetStartVillagers(int amount)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        SetRoomProperty(LobbySettingsKeys.StartVillagers, amount);
    }

    private void SetStartWorkers(int amount)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        SetRoomProperty(LobbySettingsKeys.StartWorkers, amount);
    }

    private void SetWorldSize(string worldSize)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        SetRoomProperty(LobbySettingsKeys.WorldSize, worldSize);
    }

    private void SetDebugMode(bool enabled)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        SetRoomProperty(LobbySettingsKeys.DebugMode, enabled);
    }

    private void SetRoomProperty(string key, object value)
    {
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { key, value } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void UpdateSpeedUI()
    {
        if (lobbyStartButton == null || startBtnText == null) return;

        bool isMaster = PhotonNetwork.IsMasterClient;
        lobbyStartButton.interactable = isMaster;
        startBtnText.text = isMaster ? "SPIEL STARTEN" : "WARTE AUF HOST";
        startBtnText.color = isMaster ? labelColor : GameUITheme.LabelSecondary;
    }

    private void UpdateSettingsButtons()
    {
        if (!PhotonNetwork.InRoom) return;

        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        int speed = props.TryGetValue(LobbySettingsKeys.GameSpeed, out object speedObj) ? System.Convert.ToInt32(speedObj) : 1;
        float dayLength = props.TryGetValue(LobbySettingsKeys.DayLength, out object dayObj) ? System.Convert.ToSingle(dayObj) : 90f;
        int villagers = props.TryGetValue(LobbySettingsKeys.StartVillagers, out object villObj) ? System.Convert.ToInt32(villObj) : 10;
        int workers = props.TryGetValue(LobbySettingsKeys.StartWorkers, out object workObj) ? System.Convert.ToInt32(workObj) : 2;
        string worldSize = props.TryGetValue(LobbySettingsKeys.WorldSize, out object worldObj) ? worldObj.ToString() : "Standard";
        bool debugMode = props.TryGetValue(LobbySettingsKeys.DebugMode, out object debugObj) ? (bool)debugObj : false;

        HighlightButtons(speedButtons, speedOptions, speed);
        HighlightButtons(dayLengthButtons, dayLengthOptions, dayLength);
        HighlightButtons(villagerButtons, villagerOptions, villagers);
        HighlightButtons(workerButtons, workerOptions, workers);
        HighlightButtons(worldSizeButtons, worldSizeLabels, worldSize);
        HighlightButtons(debugModeButtons, new[] { "Aus", "An" }, debugMode ? "An" : "Aus");

        SetButtonsInteractable(speedButtons, PhotonNetwork.IsMasterClient);
        SetButtonsInteractable(dayLengthButtons, PhotonNetwork.IsMasterClient);
        SetButtonsInteractable(villagerButtons, PhotonNetwork.IsMasterClient);
        SetButtonsInteractable(workerButtons, PhotonNetwork.IsMasterClient);
        SetButtonsInteractable(worldSizeButtons, PhotonNetwork.IsMasterClient);
        SetButtonsInteractable(debugModeButtons, PhotonNetwork.IsMasterClient);
    }

    private void HighlightButtons(Button[] buttons, int[] values, int active)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            SetButtonVisual(buttons[i], values[i] == active);
        }
    }

    private void HighlightButtons(Button[] buttons, float[] values, float active)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            SetButtonVisual(buttons[i], Mathf.Approximately(values[i], active));
        }
    }

    private void HighlightButtons(Button[] buttons, string[] values, string active)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            SetButtonVisual(buttons[i], values[i] == active);
        }
    }

    private void SetButtonsInteractable(Button[] buttons, bool interactable)
    {
        foreach (Button button in buttons)
        {
            button.interactable = interactable;
            if (!interactable && button.GetComponent<Image>() != null)
            {
                button.GetComponent<Image>().color = GameUITheme.ButtonPressed;
            }
        }
    }

    private void SetButtonVisual(Button button, bool active)
    {
        if (button == null) return;
        Image img = button.GetComponent<Image>();
        if (img != null)
        {
            img.color = active ? GameUITheme.AccentGold : GameUITheme.ButtonPressed;
        }
    }

    private string GetDayLengthLabel(float seconds)
    {
        if (Mathf.Approximately(seconds, 60f)) return "Kurz";
        if (Mathf.Approximately(seconds, 150f)) return "Lang";
        return "Normal";
    }

    private void OnLobbyStartButtonClicked()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            ShowLoadingOverlay();
            StartGameNow();
        }
    }

    private void ShowLoadingOverlay()
    {
        GameObject overlay = new GameObject("LoadingOverlay", typeof(RectTransform), typeof(Image));
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas != null)
        {
            overlay.transform.SetParent(canvas.transform, false);
            RectTransform rt = overlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = overlay.GetComponent<Image>();
            img.color = GameUITheme.ScreenBackdrop; // deckt Szene hinten komplett ab
            img.raycastTarget = true;

            GameObject loadingText = new GameObject("LoadingText", typeof(RectTransform), typeof(TextMeshProUGUI));
            loadingText.transform.SetParent(overlay.transform, false);
            RectTransform trt = loadingText.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.4f);
            trt.anchorMax = new Vector2(1f, 0.6f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            TextMeshProUGUI tmp = loadingText.GetComponent<TextMeshProUGUI>();
            tmp.text = "LADE SPIEL\nBitte warten...";
            tmp.fontSize = 36;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = GameUITheme.LabelPrimary;
        }
    }

    private void CreateBackdrop(Transform parent)
    {
        GameObject backdrop = new GameObject("LobbyBackdrop", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(parent, false);
        RectTransform rt = backdrop.GetComponent<RectTransform>();
        Stretch(rt);
        Image image = backdrop.GetComponent<Image>();
        image.color = bgColor;
        backdrop.transform.SetAsFirstSibling();
    }

    private void RemoveExistingRuntimeLayout(Transform canvasTransform, params string[] names)
    {
        for (int i = canvasTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = canvasTransform.GetChild(i);
            for (int j = 0; j < names.Length; j++)
            {
                if (child.name == names[j])
                {
                    Destroy(child.gameObject);
                    break;
                }
            }
        }
    }

    private void StyleExistingUi()
    {
        if (roomInfoText != null) roomInfoText.gameObject.SetActive(false);
        if (playerListText != null) playerListText.gameObject.SetActive(false);
        if (masterClientNoteText != null) masterClientNoteText.gameObject.SetActive(false);
        if (leaveRoomButton != null) leaveRoomButton.gameObject.SetActive(false);
        if (startTestButton != null) startTestButton.gameObject.SetActive(false);
    }

    private void CreateReadoutProxy(RectTransform parent, string name, TextMeshProUGUI source, Vector2 pos, Vector2 size, float fontSize, FontStyles style, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        Place(rect, pos, size);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        rect.gameObject.AddComponent<LobbyTextMirror>().Initialize(source, text);
    }

    private void CreateButtonProxy(RectTransform parent, string name, Button sourceButton, string label, Color fill, float y, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-32f, height);

        Image image = go.GetComponent<Image>();
        image.color = fill;
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(() => sourceButton.onClick.Invoke());

        TextMeshProUGUI text = CreateCenteredButtonText(go.transform, label.ToUpper());
        text.fontSize = 18f;
        text.color = labelColor;
    }

    private RectTransform CreateCard(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = panelColor;
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(3f, -3f);
        return rect;
    }

    private void StyleLobbyButton(Button button, string text, Color fill)
    {
        if (button == null) return;
        Image img = button.GetComponent<Image>();
        if (img != null) img.color = fill;
        Outline outline = button.gameObject.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.text = text.ToUpper();
            label.fontSize = 20f;
            label.fontStyle = FontStyles.Bold;
            label.color = labelColor;
        }
    }

    private void StyleText(TextMeshProUGUI text, float size, FontStyles style, TextAlignmentOptions alignment, Color color)
    {
        if (text == null) return;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
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
        text.color = labelColor;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private TextMeshProUGUI CreateCenteredButtonText(Transform parent, string value)
    {
        RectTransform rect = CreateRect("Text", parent);
        Stretch(rect);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        return text;
    }

    private RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private void Place(RectTransform rect, Vector2 offsetTopLeft, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = offsetTopLeft;
        rect.sizeDelta = size;
    }

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
