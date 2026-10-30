using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenu : MonoBehaviourPunCallbacks
{
    [Header("UI References - Inputs")]
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_Dropdown maxPlayersDropdown;

    [Header("UI References - Buttons")]
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRandomButton;
    [SerializeField] private Button joinByNameButton;

    [Header("UI References - Status")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button testLobbyButton;

    private const string PlayerNamePrefKey = "PlayerName";
    private readonly byte[] maxPlayersOptions = { 2, 4, 6, 8 };

    private readonly Color bgColor     = GameUITheme.ScreenBackdrop;
    private readonly Color panelColor  = GameUITheme.PanelBackground;
    private readonly Color accentColor = GameUITheme.AccentGold;
    private readonly Color textColor   = GameUITheme.LabelParchment;
    private TMP_InputField runtimePlayerNameInput;
    private TMP_InputField runtimeRoomNameInput;
    private TMP_Dropdown runtimeMaxPlayersDropdown;
    private TextMeshProUGUI runtimeStatusText;
    private CanvasGroup runtimeMenuCanvasGroup;
    private List<RoomInfo> cachedRoomList = new List<RoomInfo>();
    private RectTransform roomListPanel;
    private Transform roomListContent;
    private TextMeshProUGUI roomListStatusText;

    private void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;

        // Stale-Static zurücksetzen (relevant wenn Enter-Play-Mode ohne Domain Reload läuft).
        // Die Test-Kette setzt das Flag später selbst (GameManager.SpawnPlayer).
        TestModeManager.Active = false;

        LoadPlayerName();
        BuildRuntimeMenu();
        SetUIInteractable(false);
        SetStatus("Verbinde mit Photon...");
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        SetStatus("Verbinde mit Lobby-Dienst...");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        SetStatus("Verbunden. Erstelle eine Lobby oder tritt einer Runde bei.");
        SetUIInteractable(true);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        SetStatus($"Verbindung getrennt: {cause}");
        SetUIInteractable(false);
    }

    private void LoadPlayerName()
    {
        if (PlayerPrefs.HasKey(PlayerNamePrefKey))
        {
            playerNameInput.text = PlayerPrefs.GetString(PlayerNamePrefKey);
        }
    }

    private void SetUIInteractable(bool isInteractable)
    {
        if (createRoomButton != null) createRoomButton.interactable = isInteractable;
        if (joinRandomButton != null) joinRandomButton.interactable = isInteractable;
        if (joinByNameButton != null) joinByNameButton.interactable = isInteractable;
        if (playerNameInput != null) playerNameInput.interactable = isInteractable;
        if (roomNameInput != null) roomNameInput.interactable = isInteractable;
        if (maxPlayersDropdown != null) maxPlayersDropdown.interactable = isInteractable;
        if (testLobbyButton != null) testLobbyButton.interactable = isInteractable;

        if (runtimePlayerNameInput != null) runtimePlayerNameInput.interactable = isInteractable;
        if (runtimeRoomNameInput != null) runtimeRoomNameInput.interactable = isInteractable;
        if (runtimeMaxPlayersDropdown != null) runtimeMaxPlayersDropdown.interactable = isInteractable;

        if (runtimeMenuCanvasGroup != null)
        {
            runtimeMenuCanvasGroup.interactable = isInteractable;
            runtimeMenuCanvasGroup.blocksRaycasts = isInteractable;
            runtimeMenuCanvasGroup.alpha = isInteractable ? 1f : 0.72f;
        }

        if (roomListPanel != null)
        {
            CanvasGroup roomListCg = roomListPanel.GetComponent<CanvasGroup>();
            if (roomListCg == null) roomListCg = roomListPanel.gameObject.AddComponent<CanvasGroup>();
            roomListCg.interactable = isInteractable;
            roomListCg.blocksRaycasts = isInteractable;
            roomListCg.alpha = isInteractable ? 1f : 0.6f;
        }
    }

    private bool EnsureReadyForMatchmaking()
    {
        if (PhotonNetwork.IsConnectedAndReady)
        {
            return true;
        }

        SetStatus("Noch nicht mit Photon verbunden. Bitte kurz warten...");
        return false;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        if (runtimeStatusText != null)
        {
            runtimeStatusText.text = message;
        }
    }

    private bool SetupPlayerName()
    {
        string playerName = playerNameInput.text.Trim();
        if (string.IsNullOrEmpty(playerName))
        {
            SetStatus("Bitte gib erst einen Spielernamen ein.");
            return false;
        }

        PhotonNetwork.LocalPlayer.NickName = playerName;
        PlayerPrefs.SetString(PlayerNamePrefKey, playerName);
        PlayerPrefs.Save();
        return true;
    }

    public void OnCreateRoomButtonClicked()
    {
        if (!EnsureReadyForMatchmaking() || !SetupPlayerName()) return;

        string roomName = roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(roomName))
        {
            SetStatus("Bitte gib einen Lobby-Namen ein.");
            return;
        }

        byte selectedMaxPlayers = GetSelectedMaxPlayers();
        int seed = Random.Range(1, 1000000);
        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = selectedMaxPlayers,
            IsOpen = true,
            IsVisible = true,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable { 
                { LobbySettingsKeys.WorldSize, "Standard" },
                { LobbySettingsKeys.MapSeed, seed } 
            },
            CustomRoomPropertiesForLobby = new[] { LobbySettingsKeys.WorldSize, LobbySettingsKeys.MapSeed }
        };

        SetStatus($"Erstelle Lobby '{roomName}'...");
        SetUIInteractable(false);
        PhotonNetwork.CreateRoom(roomName, roomOptions);
    }

    private byte GetSelectedMaxPlayers()
    {
        if (runtimeMaxPlayersDropdown != null)
        {
            return maxPlayersOptions[Mathf.Clamp(runtimeMaxPlayersDropdown.value, 0, maxPlayersOptions.Length - 1)];
        }

        if (maxPlayersDropdown != null)
        {
            return maxPlayersOptions[Mathf.Clamp(maxPlayersDropdown.value, 0, maxPlayersOptions.Length - 1)];
        }

        return maxPlayersOptions[0];
    }

    public void OnJoinByNameButtonClicked()
    {
        if (!EnsureReadyForMatchmaking() || !SetupPlayerName()) return;

        string roomName = roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(roomName))
        {
            SetStatus("Bitte gib den Namen der Lobby ein.");
            return;
        }

        SetStatus($"Trete Lobby '{roomName}' bei...");
        SetUIInteractable(false);
        PhotonNetwork.JoinRoom(roomName);
    }

    public void OnJoinRandomButtonClicked()
    {
        if (!EnsureReadyForMatchmaking() || !SetupPlayerName()) return;

        SetStatus("Suche eine offene Lobby...");
        SetUIInteractable(false);
        PhotonNetwork.JoinRandomRoom();
    }

    public void OnTestLobbyButtonClicked()
    {
        if (!EnsureReadyForMatchmaking() || !SetupPlayerName()) return;

        string roomName = "TestRoom_" + Random.Range(1000, 9999);
        int seed = Random.Range(1, 1000000);
        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = 4,
            IsOpen = true,
            IsVisible = false,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable { 
                { "TestMode", true },
                { LobbySettingsKeys.MapSeed, seed },
                { LobbySettingsKeys.DebugMode, true }
            },
            CustomRoomPropertiesForLobby = new[] { "TestMode", LobbySettingsKeys.MapSeed, LobbySettingsKeys.DebugMode }
        };

        SetStatus("Erstelle Test-Lobby...");
        SetUIInteractable(false);
        PhotonNetwork.CreateRoom(roomName, roomOptions);
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        foreach (RoomInfo room in roomList)
        {
            if (room.RemovedFromList)
            {
                cachedRoomList.RemoveAll(r => r.Name == room.Name);
            }
            else
            {
                int index = cachedRoomList.FindIndex(r => r.Name == room.Name);
                if (index >= 0)
                    cachedRoomList[index] = room;
                else
                    cachedRoomList.Add(room);
            }
        }
        RefreshRoomListUI();
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[MainMenu] OnJoinedRoom: Raum={PhotonNetwork.CurrentRoom.Name}, Spieler={PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers}");

        bool gameStarted = false;
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbySettingsKeys.GameStarted, out object gs))
        {
            gameStarted = (bool)gs;
        }

        if (gameStarted)
        {
            SetStatus("Spiel läuft bereits. Trete laufender Runde bei...");
            PhotonNetwork.LoadLevel(SceneNames.GameScene);
        }
        else
        {
            SetStatus("Lobby gefunden. Wechsle in den Warteraum...");
            PhotonNetwork.LoadLevel(SceneNames.LobbyScene);
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        SetStatus($"Beitritt fehlgeschlagen: {message}");
        SetUIInteractable(true);
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        SetStatus("Keine offene Lobby gefunden. Erstelle einfach eine neue.");
        SetUIInteractable(true);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        SetStatus($"Lobby konnte nicht erstellt werden: {message}");
        SetUIInteractable(true);
    }

    private void BuildRuntimeMenu()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        // Fix: Make sure canvas is always overlay so camera FOV doesn't clip/hide it
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        HideOriginalUi();
        EnsureBackdrop(canvas.transform, "MainMenuBackdrop");
        EnsureTitle(canvas.transform, "MainMenuTitle", "ISLES OF WEALTH", new Vector2(0f, -60f));
        RemoveExistingRuntimeMenu(canvas.transform);

        RectTransform root = CreateRect("MainMenuRuntimeRoot", canvas.transform);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(-230f, 0f);
        root.sizeDelta = new Vector2(480f, 500f);

        Image panel = root.gameObject.AddComponent<Image>();
        panel.color = panelColor;
        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);

        VerticalLayoutGroup layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        CreateRuntimeLabel(root, "Spiele deinen Inselstaat, eröffne eine Lobby und starte direkt in eine neue Runde.", 18f, 56f, GameUITheme.LabelSecondary);
        runtimePlayerNameInput = CreateRuntimeInput(root, "SPIELERNAME", playerNameInput != null ? playerNameInput.text : "");
        runtimeRoomNameInput = CreateRuntimeInput(root, "LOBBYNAME", roomNameInput != null ? roomNameInput.text : "");
        runtimeMaxPlayersDropdown = CreateRuntimeDropdown(root, "MAX. SPIELER", new[] { "2", "4", "6", "8" }, maxPlayersDropdown != null ? maxPlayersDropdown.value : 1);

        CreateRuntimeButton(root, "LOBBY ERSTELLEN", GameUITheme.ButtonConfirm, OnRuntimeCreateRoom);
        CreateRuntimeButton(root, "PER NAME BEITRETEN", GameUITheme.ButtonNormal, OnRuntimeJoinByName);
        CreateRuntimeButton(root, "OFFENE LOBBY FINDEN", GameUITheme.ButtonNormal, OnRuntimeJoinRandom);
        CreateRuntimeButton(root, "TEST-LOBBY", GameUITheme.ButtonWarning, OnRuntimeTestLobby, 48f);

        runtimeStatusText = CreateRuntimeLabel(root, "", 16f, 50f, GameUITheme.LabelParchment);
        root.gameObject.AddComponent<MainMenuStatusMirror>().Initialize(statusText, runtimeStatusText);

        runtimeMenuCanvasGroup = root.gameObject.AddComponent<CanvasGroup>();
        runtimeMenuCanvasGroup.interactable = false;
        runtimeMenuCanvasGroup.blocksRaycasts = false;
        runtimeMenuCanvasGroup.alpha = 0.72f;

        BuildRoomListPanel(canvas.transform);
    }

    private void HideOriginalUi()
    {
        if (playerNameInput != null) playerNameInput.gameObject.SetActive(false);
        if (roomNameInput != null) roomNameInput.gameObject.SetActive(false);
        if (maxPlayersDropdown != null) maxPlayersDropdown.gameObject.SetActive(false);
        if (createRoomButton != null) createRoomButton.gameObject.SetActive(false);
        if (joinRandomButton != null) joinRandomButton.gameObject.SetActive(false);
        if (joinByNameButton != null) joinByNameButton.gameObject.SetActive(false);
        if (testLobbyButton != null) testLobbyButton.gameObject.SetActive(false);
        if (statusText != null) statusText.gameObject.SetActive(false);
    }

    private void RemoveExistingRuntimeMenu(Transform parent)
    {
        Transform existing = parent.Find("MainMenuRuntimeRoot");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }
    }

    private void OnRuntimeCreateRoom()
    {
        SyncRuntimeFieldsToOriginals();
        OnCreateRoomButtonClicked();
    }

    private void OnRuntimeJoinByName()
    {
        SyncRuntimeFieldsToOriginals();
        OnJoinByNameButtonClicked();
    }

    private void OnRuntimeJoinRandom()
    {
        SyncRuntimeFieldsToOriginals();
        OnJoinRandomButtonClicked();
    }

    private void OnRuntimeTestLobby()
    {
        SyncRuntimeFieldsToOriginals();
        OnTestLobbyButtonClicked();
    }

    private void SyncRuntimeFieldsToOriginals()
    {
        if (playerNameInput != null && runtimePlayerNameInput != null)
        {
            playerNameInput.text = runtimePlayerNameInput.text;
        }

        if (roomNameInput != null && runtimeRoomNameInput != null)
        {
            roomNameInput.text = runtimeRoomNameInput.text;
        }

        if (maxPlayersDropdown != null && runtimeMaxPlayersDropdown != null)
        {
            maxPlayersDropdown.value = runtimeMaxPlayersDropdown.value;
        }
    }

    private void EnsureBackdrop(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null)
        {
            return;
        }

        GameObject backdrop = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(parent, false);
        RectTransform rt = backdrop.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image image = backdrop.GetComponent<Image>();
        image.color = bgColor;
        backdrop.transform.SetAsFirstSibling();
    }

    private void EnsureTitle(Transform parent, string objectName, string textValue, Vector2 anchoredPos)
    {
        Transform existing = parent.Find(objectName);
        if (existing == null)
        {
            GameObject titleGo = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(parent, false);
            RectTransform rt = titleGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(900f, 90f);
            TextMeshProUGUI text = titleGo.GetComponent<TextMeshProUGUI>();
            text.text = textValue;
            text.fontSize = 40f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = textColor;
            return;
        }

        TextMeshProUGUI existingText = existing.GetComponent<TextMeshProUGUI>();
        if (existingText != null)
        {
            existingText.text = textValue;
        }
    }

    private void ApplyPanel(GameObject go, Color fill)
    {
        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.color = fill;
        Outline outline = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);
    }

    private RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private TMP_InputField CreateRuntimeInput(RectTransform parent, string label, string value)
    {
        GameObject wrapper = new GameObject(label + "_Wrapper", typeof(RectTransform), typeof(LayoutElement));
        wrapper.transform.SetParent(parent, false);
        wrapper.GetComponent<LayoutElement>().minHeight = 72f;

        CreateCaption(wrapper.transform, label);

        GameObject bg = new GameObject(label + "_Input", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(wrapper.transform, false);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0f);
        bgRt.anchorMax = new Vector2(1f, 0f);
        bgRt.pivot = new Vector2(0.5f, 0f);
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = new Vector2(0f, 50f);
        ApplyPanel(bg, panelColor);

        GameObject textArea = new GameObject("TextArea", typeof(RectTransform));
        textArea.transform.SetParent(bg.transform, false);
        RectTransform taRt = textArea.GetComponent<RectTransform>();
        taRt.anchorMin = new Vector2(0f, 0f);
        taRt.anchorMax = new Vector2(1f, 1f);
        taRt.offsetMin = new Vector2(14f, 8f);
        taRt.offsetMax = new Vector2(-14f, -8f);

        GameObject placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(textArea.transform, false);
        RectTransform phRt = placeholderGo.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero;
        phRt.anchorMax = Vector2.one;
        phRt.offsetMin = Vector2.zero;
        phRt.offsetMax = Vector2.zero;
        TextMeshProUGUI placeholder = placeholderGo.GetComponent<TextMeshProUGUI>();
        placeholder.text = label;
        placeholder.fontSize = 18f;
        placeholder.color = GameUITheme.LabelPlaceholder;

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        RectTransform txRt = textGo.GetComponent<RectTransform>();
        txRt.anchorMin = Vector2.zero;
        txRt.anchorMax = Vector2.one;
        txRt.offsetMin = Vector2.zero;
        txRt.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
        text.fontSize = 20f;
        text.color = textColor;

        TMP_InputField input = bg.AddComponent<TMP_InputField>();
        input.textViewport = taRt;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = value;
        return input;
    }

    private TMP_Dropdown CreateRuntimeDropdown(RectTransform parent, string label, string[] options, int selectedIndex)
    {
        GameObject wrapper = new GameObject(label + "_Wrapper", typeof(RectTransform), typeof(LayoutElement));
        wrapper.transform.SetParent(parent, false);
        wrapper.GetComponent<LayoutElement>().minHeight = 72f;

        CreateCaption(wrapper.transform, label);

        GameObject bg = new GameObject(label + "_Dropdown", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(wrapper.transform, false);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0f);
        bgRt.anchorMax = new Vector2(1f, 0f);
        bgRt.pivot = new Vector2(0.5f, 0f);
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = new Vector2(0f, 50f);
        ApplyPanel(bg, panelColor);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(bg.transform, false);
        RectTransform lblRt = labelGo.GetComponent<RectTransform>();
        lblRt.anchorMin = Vector2.zero;
        lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = new Vector2(14f, 8f);
        lblRt.offsetMax = new Vector2(-40f, -8f);
        TextMeshProUGUI caption = labelGo.GetComponent<TextMeshProUGUI>();
        caption.fontSize = 20f;
        caption.color = textColor;

        GameObject arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowGo.transform.SetParent(bg.transform, false);
        RectTransform arrRt = arrowGo.GetComponent<RectTransform>();
        arrRt.anchorMin = new Vector2(1f, 0.5f);
        arrRt.anchorMax = new Vector2(1f, 0.5f);
        arrRt.pivot = new Vector2(1f, 0.5f);
        arrRt.anchoredPosition = new Vector2(-14f, 0f);
        arrRt.sizeDelta = new Vector2(24f, 24f);
        TextMeshProUGUI arrowText = arrowGo.GetComponent<TextMeshProUGUI>();
        arrowText.text = "v";
        arrowText.fontSize = 18f;
        arrowText.color = accentColor;
        arrowText.alignment = TextAlignmentOptions.Center;

        TMP_Dropdown dropdown = bg.AddComponent<TMP_Dropdown>();
        dropdown.captionText = caption;
        dropdown.options.Clear();
        for (int i = 0; i < options.Length; i++)
        {
            dropdown.options.Add(new TMP_Dropdown.OptionData(options[i]));
        }
        dropdown.value = Mathf.Clamp(selectedIndex, 0, options.Length - 1);
        dropdown.RefreshShownValue();
        SetupDropdownTemplate(dropdown, textColor, panelColor, accentColor);
        return dropdown;
    }

    private void SetupDropdownTemplate(TMP_Dropdown dropdown, Color textCol, Color bgCol, Color accentCol)
    {
        GameObject template = new GameObject("Template", typeof(RectTransform));
        template.SetActive(false);
        template.transform.SetParent(dropdown.transform, false);
        RectTransform tmplRt = template.GetComponent<RectTransform>();
        tmplRt.anchorMin = new Vector2(0f, 1f);
        tmplRt.anchorMax = new Vector2(1f, 1f);
        tmplRt.pivot = new Vector2(0.5f, 1f);
        tmplRt.anchoredPosition = new Vector2(0f, 0f);
        tmplRt.sizeDelta = new Vector2(0f, 160f);

        GameObject vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        vpGo.transform.SetParent(template.transform, false);
        RectTransform vpRt = vpGo.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.sizeDelta = Vector2.zero;
        Image vpImg = vpGo.GetComponent<Image>();
        vpImg.color = bgCol;
        vpGo.GetComponent<Mask>().showMaskGraphic = false;

        GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(vpGo.transform, false);
        RectTransform ctRt = contentGo.GetComponent<RectTransform>();
        ctRt.anchorMin = Vector2.zero;
        ctRt.anchorMax = Vector2.one;
        ctRt.sizeDelta = Vector2.zero;
        ctRt.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter csf = contentGo.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject itemGo = new GameObject("Item", typeof(RectTransform), typeof(Toggle), typeof(LayoutElement));
        itemGo.transform.SetParent(contentGo.transform, false);
        itemGo.GetComponent<LayoutElement>().minHeight = 40f;
        itemGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
        RectTransform itemRt = itemGo.GetComponent<RectTransform>();
        itemRt.anchorMin = new Vector2(0f, 0.5f);
        itemRt.anchorMax = new Vector2(1f, 0.5f);
        itemRt.sizeDelta = new Vector2(0f, 40f);

        GameObject itemBg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        itemBg.transform.SetParent(itemGo.transform, false);
        RectTransform ibgRt = itemBg.GetComponent<RectTransform>();
        ibgRt.anchorMin = Vector2.zero;
        ibgRt.anchorMax = Vector2.one;
        ibgRt.sizeDelta = Vector2.zero;
        Image ibgImg = itemBg.GetComponent<Image>();
        ibgImg.color = GameUITheme.SlotWood;

        GameObject itemLabel = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        itemLabel.transform.SetParent(itemGo.transform, false);
        RectTransform ilRt = itemLabel.GetComponent<RectTransform>();
        ilRt.anchorMin = Vector2.zero;
        ilRt.anchorMax = Vector2.one;
        ilRt.offsetMin = new Vector2(10f, 4f);
        ilRt.offsetMax = new Vector2(-10f, -4f);
        TextMeshProUGUI ilText = itemLabel.GetComponent<TextMeshProUGUI>();
        ilText.fontSize = 18f;
        ilText.color = textCol;
        ilText.alignment = TextAlignmentOptions.MidlineLeft;

        ScrollRect sr = template.AddComponent<ScrollRect>();
        sr.content = ctRt;
        sr.viewport = vpRt;
        sr.horizontal = false;
        sr.vertical = true;

        dropdown.template = tmplRt;
        dropdown.itemText = ilText;
    }

    private void CreateRuntimeButton(RectTransform parent, string label, Color fill, UnityEngine.Events.UnityAction action, float height = 54f)
    {
        GameObject go = new GameObject(label + "_Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().minHeight = height;
        ApplyPanel(go, fill);

        TextMeshProUGUI text = CreateRuntimeLabel(go.GetComponent<RectTransform>(), label, 19f, height, textColor);
        text.alignment = TextAlignmentOptions.Center;

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
    }

    private TextMeshProUGUI CreateRuntimeLabel(RectTransform parent, string value, float fontSize, float height, Color color)
    {
        GameObject go = new GameObject(value + "_Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().minHeight = height;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private void CreateCaption(Transform parent, string label)
    {
        GameObject captionGo = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
        captionGo.transform.SetParent(parent, false);
        RectTransform rt = captionGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(0f, 18f);
        TextMeshProUGUI text = captionGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 12f;
        text.fontStyle = FontStyles.Bold;
        text.color = accentColor;
        text.alignment = TextAlignmentOptions.TopLeft;
    }

    private void BuildRoomListPanel(Transform canvasTransform)
    {
        roomListPanel = CreateRect("RoomListPanel", canvasTransform);
        roomListPanel.anchorMin = new Vector2(0.5f, 0.5f);
        roomListPanel.anchorMax = new Vector2(0.5f, 0.5f);
        roomListPanel.pivot = new Vector2(0.5f, 0.5f);
        roomListPanel.anchoredPosition = new Vector2(260f, 0f);
        roomListPanel.sizeDelta = new Vector2(460f, 500f);

        Image panel = roomListPanel.gameObject.AddComponent<Image>();
        panel.color = panelColor;
        Outline outline = roomListPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);

        VerticalLayoutGroup vlg = roomListPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 16, 16);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        CreateRuntimeLabel(roomListPanel, "OFFENE LOBBIES", 20f, 40f, accentColor);

        RectTransform scrollRectRt = CreateRect("RoomListScrollRect", roomListPanel);
        scrollRectRt.anchorMin = new Vector2(0f, 0f);
        scrollRectRt.anchorMax = new Vector2(1f, 1f);
        scrollRectRt.offsetMin = new Vector2(8f, 60f);
        scrollRectRt.offsetMax = new Vector2(-8f, -8f);

        Image scrollBg = scrollRectRt.gameObject.AddComponent<Image>();
        scrollBg.color = GameUITheme.PanelBackgroundSoft;

        RectTransform viewportRt = CreateRect("Viewport", scrollRectRt);
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.sizeDelta = Vector2.zero;

        Image vpImage = viewportRt.gameObject.AddComponent<Image>();
        vpImage.color = Color.clear;
        Mask mask = viewportRt.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportRt, false);
        RectTransform contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = Vector2.zero;
        contentRt.anchorMax = Vector2.one;
        contentRt.sizeDelta = Vector2.zero;
        contentRt.anchoredPosition = Vector2.zero;
        roomListContent = contentRt;

        VerticalLayoutGroup contentVlg = contentGo.GetComponent<VerticalLayoutGroup>();
        contentVlg.padding = new RectOffset(6, 6, 6, 6);
        contentVlg.spacing = 6f;
        contentVlg.childAlignment = TextAnchor.UpperCenter;
        contentVlg.childForceExpandWidth = true;
        contentVlg.childForceExpandHeight = false;

        ContentSizeFitter csf = contentGo.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect sr = scrollRectRt.gameObject.AddComponent<ScrollRect>();
        sr.content = contentRt;
        sr.viewport = viewportRt;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 35f;

        roomListStatusText = CreateRuntimeLabel(roomListPanel, "", 14f, 30f, GameUITheme.LabelSecondary);
        roomListStatusText.GetComponent<LayoutElement>().minHeight = 0;
        roomListStatusText.text = "Suche nach Lobbys...";
    }

    private void RefreshRoomListUI()
    {
        if (roomListContent == null) return;

        for (int i = roomListContent.childCount - 1; i >= 0; i--)
        {
            Destroy(roomListContent.GetChild(i).gameObject);
        }

        if (cachedRoomList.Count == 0)
        {
            roomListStatusText.text = "Keine offenen Lobbys gefunden.";
            return;
        }

        roomListStatusText.text = $"{cachedRoomList.Count} Lobbys gefunden. Klicke zum Beitreten.";

        foreach (RoomInfo room in cachedRoomList)
        {
            if (room == null || !room.IsOpen || room.IsVisible == false) continue;
            CreateRoomEntry(room);
        }
    }

    private void CreateRoomEntry(RoomInfo room)
    {
        GameObject entry = new GameObject("RoomEntry_" + room.Name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        entry.transform.SetParent(roomListContent, false);

        LayoutElement le = entry.GetComponent<LayoutElement>();
        le.minHeight = 64f;
        le.flexibleWidth = 1f;

        RectTransform rt = entry.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(0f, 64f);

        bool gameStarted = false;
        if (room.CustomProperties.TryGetValue(LobbySettingsKeys.GameStarted, out object gs))
            gameStarted = (bool)gs;

        string worldSize = "Standard";
        if (room.CustomProperties.TryGetValue(LobbySettingsKeys.WorldSize, out object ws))
            worldSize = ws.ToString();

        Image img = entry.GetComponent<Image>();
        img.color = gameStarted ? GameUITheme.SlotWood : GameUITheme.ButtonChatIcon;
        img.raycastTarget = true;

        Button btn = entry.GetComponent<Button>();
        btn.targetGraphic = img;
        RoomInfo capturedRoom = room;
        btn.onClick.AddListener(() => JoinRoomFromList(capturedRoom));

        GameObject textContainer = new GameObject("TextContainer", typeof(RectTransform));
        textContainer.transform.SetParent(entry.transform, false);
        RectTransform tcRt = textContainer.GetComponent<RectTransform>();
        tcRt.anchorMin = Vector2.zero;
        tcRt.anchorMax = Vector2.one;
        tcRt.offsetMin = new Vector2(12f, 4f);
        tcRt.offsetMax = new Vector2(-12f, -4f);

        TextMeshProUGUI nameText = textContainer.AddComponent<TextMeshProUGUI>();
        nameText.text = room.Name;
        nameText.fontSize = 16f;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = textColor;
        nameText.alignment = TextAlignmentOptions.TopLeft;

        string statusLabel = gameStarted ? "Im Spiel" : "In Lobby";
        string infoLine = $"{room.PlayerCount}/{room.MaxPlayers} Spieler  •  {worldSize}  •  {statusLabel}";
        Color infoColor = gameStarted ? GameUITheme.StatusWarning : GameUITheme.StatusGood;

        TextMeshProUGUI infoText = new GameObject("Info", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        infoText.transform.SetParent(textContainer.transform, false);
        RectTransform infoRt = infoText.GetComponent<RectTransform>();
        infoRt.anchorMin = new Vector2(0f, 0f);
        infoRt.anchorMax = new Vector2(1f, 0f);
        infoRt.pivot = new Vector2(0f, 0f);
        infoRt.anchoredPosition = Vector2.zero;
        infoRt.sizeDelta = new Vector2(0f, 20f);
        infoText.text = infoLine;
        infoText.fontSize = 13f;
        infoText.color = infoColor;
        infoText.alignment = TextAlignmentOptions.BottomLeft;
    }

    private void JoinRoomFromList(RoomInfo room)
    {
        if (!EnsureReadyForMatchmaking() || !SetupPlayerName()) return;
        if (room.PlayerCount >= room.MaxPlayers)
        {
            SetStatus($"Lobby '{room.Name}' ist bereits voll ({room.PlayerCount}/{room.MaxPlayers}).");
            return;
        }

        SetStatus($"Trete Lobby '{room.Name}' bei...");
        SetUIInteractable(false);
        PhotonNetwork.JoinRoom(room.Name);
    }
}
