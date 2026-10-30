using UnityEngine;
using Photon.Pun;

/// <summary>
/// TEST-MODUS: Zwei nebeneinanderliegende Inseln, 9999 aller Ressourcen
/// und ein echter BOT-CLIENT, der sich wie ein echter Spieler über Photon verbindet
/// (eigene Verbindung, eigener ActorNumber — NICHT lokal gefaked).
///
/// Aktivierung: MainMenu → "Test Lobby" (Raum-Prop "TestMode" = true).
/// </summary>
public static class TestModeManager
{
    public const int CheatResourceAmount = 9999;

    /// <summary>Aktiv sobald der GameScene-Start in einem TestMode-Raum läuft.
    /// Wird vom GameManager.SpawnPlayer gesetzt (Room-Prop "TestMode").</summary>
    public static bool Active;
    /// <summary>Deterministisches 2-Inseln-Layout für den Test-Modus (Insel 0 = Spieler, Insel 1 = Bot).</summary>
    public static readonly Vector2[] TestIslandLayout =
    {
        new Vector2(0f, 0f),
        new Vector2(65f, 0f),
    };

    /// <summary>Deterministisch: Test-Räume haben IMMER dieses Layout.</summary>
    public static bool IsTestRoom()
    {
        if (!PhotonNetwork.InRoom) return false;
        return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TestMode", out object v) && v is bool b && b;
    }
}

/// <summary>
/// Mono-Bootstrap pro Szenen: vergibt Cheat-Ressourcen, startet den Bot-Client
/// und zeigt einen kleinen DEBUG-Overlay über den Bot-Zustand.
/// </summary>
public class TestModeBootstrap : MonoBehaviour
{
    public static TestModeBootstrap Instance;

    private float tickTimer;
    private bool resourcesGranted;
    private TestBotClient bot;

    // ── Debug-Overlay ────────────────────────────────────────────────────
    private TextMeshProUGUIOverlay overlay;
    private bool overlayVisible = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public static void Create()
    {
        if (Instance != null) return;

        GameObject go = new GameObject("TestModeBootstrap");
        go.AddComponent<TestModeBootstrap>();
        Debug.Log("[TestMode] Bootstrap erstellt (Test-Modus aktiv).");
    }

    private void Update()
    {
        if (!TestModeManager.Active || !PhotonNetwork.InRoom) return;

        // F8 schaltet das Overlay um
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
        {
            overlayVisible = !overlayVisible;
            if (overlay != null) overlay.SetVisible(overlayVisible);
        }

        // 1) Cheat-Ressourcen an den lokalen Spieler, sobald die UI existiert
        if (!resourcesGranted && Player_UI.Instance != null)
        {
            resourcesGranted = true;
            Player_UI.Instance.DebugFillResources(TestModeManager.CheatResourceAmount);
        }

        // 2) Bot starten, sobald Welt + Manager stehen
        if (bot == null && IslandManager.Instance != null && IslandManager.Instance.IsGenerated
            && VillagerManager.Instance != null
            && PhotonNetwork.CurrentRoom != null)
        {
            GameObject botGO = new GameObject("TestBotClient");
            bot = botGO.AddComponent<TestBotClient>();
        }

        // 3) Overlay-Aktualisierung
        tickTimer -= Time.unscaledDeltaTime;
        if (tickTimer <= 0f)
        {
            tickTimer = 0.5f;
            EnsureOverlay();
            if (overlay != null)
            {
                overlay.SetText(BuildOverlayText());
            }
        }
    }

    private string BuildOverlayText()
    {
        string botState = bot != null ? bot.GetStatusLine() : "Bot: noch nicht gestartet";
        int localSoldiers = Soldier.ActiveSoldiers.Count;
        int buildings = BuildingInstance.AllBuildings.Count;
        return $"TEST-MODUS\n{botState}\nLokale Soldaten: {localSoldiers}   Gebäude: {buildings}\n(F8 = Overlay aus/an)";
    }

    private void EnsureOverlay()
    {
        if (overlay != null) return;
        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas == null) return;
        overlay = TextMeshProUGUIOverlay.Create(canvas, "TestModeOverlay");
    }

    private void OnDestroy()
    {
        if (bot != null) bot.Shutdown();
        if (Instance == this) Instance = null;
    }
}

/// <summary>Dünnes overlay-Text-Element für den Test-Modus (F8-Toggle).</summary>
public class TextMeshProUGUIOverlay
{
    private readonly TMPro.TextMeshProUGUI text;
    private readonly GameObject root;

    private TextMeshProUGUIOverlay(TMPro.TextMeshProUGUI text, GameObject root)
    {
        this.text = text;
        this.root = root;
    }

    public static TextMeshProUGUIOverlay Create(Canvas canvas, string name)
    {
        // Ein GameObject darf nur EINE Graphic-Komponente tragen (Image + TMP = Fehler).
        // Deshalb: Panel (BG/Outline) auf dem Root, der Text bekommt ein eigenes Kind.
        GameObject go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-16f, -16f);
        rect.sizeDelta = new Vector2(320f, 90f);

        var bg = go.AddComponent<UnityEngine.UI.Image>();
        bg.color = GameUITheme.OverlayVeil;

        var outline = go.AddComponent<UnityEngine.UI.Outline>();
        GameUITheme.ApplyOutline(outline, true);

        GameObject textGO = new GameObject(name + "_Text");
        textGO.transform.SetParent(go.transform, false);
        var textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 6f);
        textRect.offsetMax = new Vector2(-8f, -6f);

        var tmp = textGO.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.fontSize = 14;
        tmp.alignment = TMPro.TextAlignmentOptions.TopLeft;
        tmp.color = GameUITheme.LabelParchment;
        tmp.text = "TEST-MODUS";

        var overlay = new TextMeshProUGUIOverlay(tmp, go);
        return overlay;
    }

    public void SetText(string s)
    {
        if (text != null) text.text = s;
    }

    public void SetVisible(bool visible)
    {
        if (root != null) root.SetActive(visible);
    }
}
