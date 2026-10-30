using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Rechtes Seitenfeld das erscheint wenn ein Gebäude angeklickt wird.
/// Zeigt Stats, Produktion, Verbrauch – mit Pause- und Abriss-Button.
/// Zeigt einen geschmeidigen, flüssigen Fortschrittsbalken für die Produktion an.
/// Wird vollständig per Code erstellt (kein Prefab nötig).
/// Verwendet das neue Input System und bietet eine geschmeidige Slide-In/Out Animation.
/// </summary>
public class BuildingInfoPanel : MonoBehaviour
{
    public static BuildingInfoPanel Instance;

    // ── Farben (zentrale Palette: GameUITheme) ─────────────────────────────
    private readonly Color bgColor     = GameUITheme.PanelBackground;
    private readonly Color borderColor = GameUITheme.OutlineGold;
    private readonly Color labelColor  = GameUITheme.LabelParchment;
    private readonly Color valueColor  = GameUITheme.LabelPrimary;
    private readonly Color btnDanger   = GameUITheme.ButtonDanger;
    private readonly Color btnNeutral  = GameUITheme.ButtonConfirm;
    private readonly Color btnWarning  = GameUITheme.ButtonWarning;

    // ── Panel-Objekte ────────────────────────────────────────────────────────
    private GameObject panelRoot;
    private RectTransform rootRT;
    private TextMeshProUGUI txtName;
    private TextMeshProUGUI txtStatus;
    private TextMeshProUGUI txtProduces;
    private TextMeshProUGUI txtConsumes;
    private TextMeshProUGUI txtTotalProduced;
    private TextMeshProUGUI txtWorkers;
    private TextMeshProUGUI txtSize;
    private Button          btnPause;
    private TextMeshProUGUI txtPauseLabel;
    private Button          btnDemolish;
    private TextMeshProUGUI txtSchedule;
    private Button          btnToggleSchedule;
    private TextMeshProUGUI txtScheduleBtnLabel;
    private Button          btnToggleHutType;
    private TextMeshProUGUI txtHutTypeBtnLabel;

    // ── Barracks UI ──────────────────────────────────────────────────────────
    private GameObject barracksContainer;
    private Button btnToggleSpear;
    private Button btnToggleShield;
    private Button btnToggleSword;
    private Button btnToggleBow;
    private Button btnResWood;
    private Button btnResStone;
    private Button btnResGold;
    private Button btnResIron;
    private Button btnAutoRecruit;
    private Button btnOrderSpear;
    private Button btnOrderShield;
    private Button btnOrderSword;
    private Button btnOrderBow;
    private TextMeshProUGUI txtQueueStatus;

    // ── Progress Bar Objekte ─────────────────────────────────────────────────
    private GameObject    goProgressBG;
    private RectTransform rtProgressFill;

    // ── Animation & Positionierung ───────────────────────────────────────────
    private float targetPosX = 480f;    // Vollständig außerhalb des Bildschirms rechts
    private float currentPosX = 480f;
    private float animSpeed = 12f;      // Geschwindigkeit der Slide-Animation
    private bool isPanelActive = false;

    // ── Aktuell geöffnetes Gebäude ───────────────────────────────────────────
    private BuildingInstance currentBuilding;

    // ── Update-Rate ──────────────────────────────────────────────────────────
    private float refreshTimer = 0f;
    private const float RefreshInterval = 0.5f;
    private float progressTimer = 0f;

    // ────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        BuildPanel();
        
        // Initiale Position auf unsichtbar / außerhalb setzen
        if (rootRT != null)
        {
            currentPosX = 480f;
            targetPosX = 480f;
            rootRT.anchoredPosition = new Vector2(currentPosX, 0f);
        }
        panelRoot.SetActive(false);
    }

    private void Update()
    {
        // Panel-Position weich interpolieren
        currentPosX = Mathf.Lerp(currentPosX, targetPosX, Time.deltaTime * animSpeed);
        if (rootRT != null)
        {
            rootRT.anchoredPosition = new Vector2(currentPosX, 0f);
        }

        // Wenn wir das Panel schließen und es fast vollständig außerhalb ist, deaktivieren
        if (!isPanelActive && currentPosX >= 470f)
        {
            currentBuilding = null;
            panelRoot.SetActive(false);
            return;
        }

        // Stats updaten falls ein Gebäude aktiv gewählt ist
        if (currentBuilding != null && isPanelActive)
        {
            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = RefreshInterval;
                RefreshStats();
            }

            // Progress-Balken: 10 Hz reicht völlig (war: jedes Frame)
            progressTimer -= Time.deltaTime;
            if (progressTimer <= 0f)
            {
                progressTimer = 0.1f;
                UpdateProgressBar();
            }
        }

        // Escape-Taste schließt das Panel über das neue Input System
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Hide();
        }
    }

    // ── Öffentliche API ──────────────────────────────────────────────────────

    public void Show(BuildingInstance building)
    {
        SetTowerRangeVisible(currentBuilding, false);
        currentBuilding = building;
        SetTowerRangeVisible(currentBuilding, true);
        isPanelActive = true;
        panelRoot.SetActive(true);
        
        // Wenn es komplett versteckt war, von ganz rechts reinfliegen lassen
        if (currentPosX >= 470f)
        {
            currentPosX = 480f;
        }
        
        targetPosX = -30f; // Sichtbare Position (30px Abstand vom rechten Bildschirmrand)
        refreshTimer = 0f;  // sofort refreshen
    }

    public void Hide()
    {
        SetTowerRangeVisible(currentBuilding, false);
        targetPosX = 480f; // Slide out nach rechts
        isPanelActive = false;
    }

    public bool IsVisible => isPanelActive;

    // ── Stats aktualisieren ──────────────────────────────────────────────────

    private void RefreshStats()
    {
        if (currentBuilding == null) return;
        BuildingData d = currentBuilding.data;

        int multiCount = 0;
        if (SelectionManager.Instance != null)
        {
            var sel = SelectionManager.Instance.GetSelectedBuildings();
            bool containsCurrent = false;
            foreach (var b in sel)
            {
                if (b == currentBuilding) { containsCurrent = true; break; }
            }
            if (sel.Count > 1 && containsCurrent)
                multiCount = sel.Count;
        }
        txtName.text = multiCount > 1
            ? $"{multiCount}× {currentBuilding.GetDisplayName().ToUpper()}"
            : currentBuilding.GetDisplayName().ToUpper();

        // Status
        string statusStr;
        if (!currentBuilding.IsConstructed())
            statusStr = $"<color={GameUITheme.HtmlWarning}>⚙ Im Bau...</color>";
        else if (currentBuilding.IsProductionPaused)
            statusStr = $"<color={GameUITheme.HtmlBad}>⏸ Produktion pausiert</color>";
        else
            statusStr = $"<color={GameUITheme.HtmlGood}>✔ Aktiv</color>";
        txtStatus.text = statusStr;

        if (currentBuilding.ProvidesSleep && (d == null || !d.isBarracks))
        {
            RefreshLodgingStats(d);
            return;
        }

        if (d == null) return;

        // Check if building is a barracks
        if (d.isBarracks)
        {
            if (barracksContainer != null) barracksContainer.SetActive(true);
            
            // Hide standard elements not useful for barracks
            txtProduces.gameObject.SetActive(false);
            txtConsumes.gameObject.SetActive(false);
            txtTotalProduced.gameObject.SetActive(false);
            txtWorkers.gameObject.SetActive(false);
            txtSchedule.gameObject.SetActive(false);
            btnToggleSchedule.gameObject.SetActive(false);
            btnToggleHutType.gameObject.SetActive(false);
            
            // Update Toggle Button Visuals (Zugelassene Typen)
            UpdateButtonToggleState(btnToggleSpear, currentBuilding.spearSelected);
            UpdateButtonToggleState(btnToggleShield, currentBuilding.shieldSelected);
            UpdateButtonToggleState(btnToggleSword, currentBuilding.swordSelected);
            UpdateButtonToggleState(btnToggleBow, currentBuilding.bowSelected);

            // Update Resource Choice Visuals (Radio buttons)
            UpdateButtonResourceState(btnResWood, currentBuilding.selectedResource == BuildingInstance.BarracksResource.Wood, GameUITheme.SlotWood);
            UpdateButtonResourceState(btnResStone, currentBuilding.selectedResource == BuildingInstance.BarracksResource.Stone, Color.gray);
            UpdateButtonResourceState(btnResGold, currentBuilding.selectedResource == BuildingInstance.BarracksResource.Gold, GameUITheme.StatusWarning);
            UpdateButtonResourceState(btnResIron, currentBuilding.selectedResource == BuildingInstance.BarracksResource.Iron, GameUITheme.LabelSecondary);

            // Auto Recruit toggle button state
            var autoTxt = btnAutoRecruit.GetComponentInChildren<TextMeshProUGUI>();
            if (currentBuilding.autoRecruit)
            {
                autoTxt.text = "🤖 Autorekrutierung (20%): AN";
                btnAutoRecruit.GetComponent<Image>().color = GameUITheme.StatusGood;
            }
            else
            {
                autoTxt.text = "🤖 Autorekrutierung (20%): AUS";
                btnAutoRecruit.GetComponent<Image>().color = btnWarning;
            }

            // Update order button labels with current costs
            int currentCost = 0;
            switch (currentBuilding.selectedResource)
            {
                case BuildingInstance.BarracksResource.Wood:
                    currentCost = currentBuilding.woodSoldierCost;
                    break;
                case BuildingInstance.BarracksResource.Stone:
                    currentCost = currentBuilding.stoneSoldierCost;
                    break;
                case BuildingInstance.BarracksResource.Gold:
                    currentCost = currentBuilding.goldSoldierCost;
                    break;
                case BuildingInstance.BarracksResource.Iron:
                    currentCost = currentBuilding.ironSoldierCost;
                    break;
            }

            if (btnOrderSpear != null)  btnOrderSpear.GetComponentInChildren<TextMeshProUGUI>().text = $"+Speer ({currentCost})";
            if (btnOrderShield != null) btnOrderShield.GetComponentInChildren<TextMeshProUGUI>().text = $"+Schild ({currentCost})";
            if (btnOrderSword != null)  btnOrderSword.GetComponentInChildren<TextMeshProUGUI>().text = $"+Schwert ({currentCost})";
            if (btnOrderBow != null)    btnOrderBow.GetComponentInChildren<TextMeshProUGUI>().text = $"+Bogen ({currentCost})";

            // Order/Queue state text
            int qCount = currentBuilding.recruitQueue.Count;
            if (qCount > 0)
            {
                txtQueueStatus.text = $"Warteschlange: <b>{qCount}</b> in Ausbildung... (nächstes: {currentBuilding.currentRecruitingType})";
                txtQueueStatus.color = GameUITheme.StatusWarning;
            }
            else
            {
                txtQueueStatus.text = "Warteschlange: Leer";
                txtQueueStatus.color = labelColor;
            }
        }
        else
        {
            if (barracksContainer != null) barracksContainer.SetActive(false);
            
            // Restore standard elements
            txtProduces.gameObject.SetActive(true);
            txtConsumes.gameObject.SetActive(true);
            txtTotalProduced.gameObject.SetActive(true);
            txtWorkers.gameObject.SetActive(true);

            // Produktion
            if (!string.IsNullOrEmpty(d.productionResourceId) && d.productionResourceId != "bevolkerung")
            {
                float globalMood = 100f;
                if (VillagerManager.Instance != null) globalMood = VillagerManager.Instance.globalMood;
                float yieldModifier = Mathf.Lerp(0.3f, 1.0f, globalMood / 100f);
                int actualProduction = Mathf.Max(1, Mathf.RoundToInt(d.productionAmount * yieldModifier));

                string prodText = $"<b>Produziert:</b>  +{actualProduction} {Capitalize(d.productionResourceId)}";
                if (actualProduction < d.productionAmount)
                {
                    prodText += $" <size=13><color={GameUITheme.HtmlBad}>(-{d.productionAmount - actualProduction} wg. Stimmung)</color></size>";
                }
                prodText += $"\n<b>Intervall:</b> alle {d.productionInterval:F0}s";
                txtProduces.text = prodText;
            }
            else if (d.producesVillagers)
            {
                txtProduces.text = "<b>Produziert:</b> Dorfbewohner";
            }
            else if (d.productionResourceId == "bevolkerung")
            {
                txtProduces.text = $"Erhöht Bevölkerungs-\nkapazität um {d.productionAmount}";
            }
            else
            {
                txtProduces.text = "<b>Produziert:</b> –";
            }

            // Verbrauch
            txtConsumes.text = (!string.IsNullOrEmpty(d.consumedResourceId) && d.consumedAmount > 0)
                ? $"<b>Verbraucht:</b>  -{d.consumedAmount} {Capitalize(d.consumedResourceId)}/Zyklus"
                : "<b>Verbraucht:</b> –";

            // Gesamtproduktion
            txtTotalProduced.text = $"<b>Gesamt produziert:</b> {currentBuilding.TotalProduced}";

            // Arbeiter / Größe
            txtWorkers.text = $"<b>Arbeiter benötigt:</b> {d.requiredWorkers}";
            txtSize.text    = $"<b>Größe:</b> {d.width} × {d.height} Grid-Felder";

            // Schedule
            if (d.workersNeeded > 0)
            {
                txtSchedule.gameObject.SetActive(true);
                btnToggleSchedule.gameObject.SetActive(currentBuilding.IsConstructed());
                
                if (currentBuilding.currentSchedule == BuildingInstance.ScheduleMode.Continuous)
                {
                    txtSchedule.text = $"<b>Arbeitsplan:</b> Nachtarbeit (24/7)\n<color={GameUITheme.HtmlBad}>⚠️ Starker Stress & Erschöpfungsrisiko!</color>";
                    txtScheduleBtnLabel.text = "📅 Plan: 24/7";
                }
                else if (currentBuilding.currentSchedule == BuildingInstance.ScheduleMode.DayOnly)
                {
                    txtSchedule.text = $"<b>Arbeitsplan:</b> Regulär (Tag)\n<color={GameUITheme.HtmlWarning}>⚠ Milder Arbeitsstress (Normal)</color>";
                    txtScheduleBtnLabel.text = "📅 Plan: Regulär (Tag)";
                }
                else
                {
                    txtSchedule.text = $"<b>Arbeitsplan:</b> Freizeit-Plan\n<color={GameUITheme.HtmlGood}>✔ +Zufriedenheit (Mittagspause & Freizeit)</color>";
                    txtScheduleBtnLabel.text = "📅 Plan: Freizeit";
                }
            }
            else
            {
                txtSchedule.gameObject.SetActive(false);
                btnToggleSchedule.gameObject.SetActive(false);
            }

            // Hut Type Button (Only for buildings that produce villagers/citizens, e.g. residential huts/houses)
            if (d.producesVillagers)
            {
                btnToggleHutType.gameObject.SetActive(currentBuilding.IsConstructed());
                if (currentBuilding.isBuilderHut)
                {
                    txtHutTypeBtnLabel.text = "🏗️ Typ: Bauarbeiter-Hütte (Kosten: 1 Weizen)";
                    btnToggleHutType.GetComponent<Image>().color = GameUITheme.ButtonWarning; // orange/gold theme
                }
                else
                {
                    txtHutTypeBtnLabel.text = "🏠 Typ: Wohnhaus (Kosten: 1 Weizen)";
                    btnToggleHutType.GetComponent<Image>().color = btnNeutral; // standard green/neutral
                }
            }
            else
            {
                btnToggleHutType.gameObject.SetActive(false);
            }
        }

        // Size
        txtSize.text    = $"<b>Größe:</b> {d.width} × {d.height} Grid-Felder";

        // Pause-Button Text
        txtPauseLabel.text = currentBuilding.IsProductionPaused ? "▶  Fortsetzen" : "⏸  Pausieren";
        btnPause.gameObject.SetActive(currentBuilding.IsConstructed() &&
            (!string.IsNullOrEmpty(d.productionResourceId) || d.producesVillagers || d.isBarracks));
    }

    private void RefreshLodgingStats(BuildingData d)
    {
        if (barracksContainer != null) barracksContainer.SetActive(false);

        txtProduces.gameObject.SetActive(true);
        txtConsumes.gameObject.SetActive(true);
        txtTotalProduced.gameObject.SetActive(false);
        txtWorkers.gameObject.SetActive(true);
        txtSchedule.gameObject.SetActive(false);
        btnToggleSchedule.gameObject.SetActive(false);
        btnToggleHutType.gameObject.SetActive(false);
        btnPause.gameObject.SetActive(false);

        int used = currentBuilding.GetSleepingCount();
        int cap = currentBuilding.GetSleepCapacity();
        string sleepColor = used >= cap ? "#FF5555" : (used > 0 ? "#FFAA44" : "#88FF88");
        txtWorkers.text = $"<b>Schlafplätze:</b> <color={sleepColor}>{used} / {cap}</color>";

        if (d == null)
        {
            txtProduces.text = "<b>Funktion:</b> Zentrales Hauptlager";
            txtConsumes.text = "<b>Schlafkapazität:</b> 5 Personen (Startinsel)";
            txtSize.text = "<b>Größe:</b> 3 × 3 Grid-Felder";
            return;
        }

        if (d.producesVillagers)
        {
            txtProduces.text = "<b>Produziert:</b> Dorfbewohner";
        }
        else if (d.productionResourceId == "bevolkerung")
        {
            txtProduces.text = $"Erhöht Bevölkerungs-\nkapazität um {d.productionAmount}";
        }
        else
        {
            txtProduces.text = "<b>Produziert:</b> –";
        }

        txtConsumes.text = GetSleepCapacityDescription(d);
        txtSize.text = $"<b>Größe:</b> {d.width} × {d.height} Grid-Felder";

        if (d.producesVillagers)
        {
            btnToggleHutType.gameObject.SetActive(currentBuilding.IsConstructed());
            if (currentBuilding.isBuilderHut)
            {
                txtHutTypeBtnLabel.text = "🏗️ Typ: Bauarbeiter-Hütte (Kosten: 1 Weizen)";
                btnToggleHutType.GetComponent<Image>().color = GameUITheme.ButtonWarning;
            }
            else
            {
                txtHutTypeBtnLabel.text = "🏠 Typ: Wohnhaus (Kosten: 1 Weizen)";
                btnToggleHutType.GetComponent<Image>().color = btnNeutral;
            }
        }
    }

    private string GetSleepCapacityDescription(BuildingData d)
    {
        if (d.buildingName.Contains("Groß")) return "<b>Schlafkapazität:</b> maximal 4 Personen";
        return "<b>Schlafkapazität:</b> maximal 2 Personen";
    }

    // ── Progress-Balken Updaten ─────────────────────────────────────────────

    private void UpdateProgressBar()
    {
        if (currentBuilding == null || goProgressBG == null || rtProgressFill == null) return;

        BuildingData d = currentBuilding.data;
        if (d == null || currentBuilding.ProvidesSleep)
        {
            goProgressBG.SetActive(false);
            return;
        }
        
        // Show progress bar for standard production OR barracks recruitment!
        bool canProduce = (!string.IsNullOrEmpty(d.productionResourceId) || d.producesVillagers) && d.productionResourceId != "bevolkerung";
        bool showBar = currentBuilding.IsConstructed() && (canProduce || d.isBarracks) && !currentBuilding.IsProductionPaused;

        if (showBar)
        {
            goProgressBG.SetActive(true);
            float progress = currentBuilding.ProductionProgress;
            rtProgressFill.anchorMax = new Vector2(progress, 1f);

            // Reddish color for military recruitment, warning-orange for economic production
            rtProgressFill.GetComponent<Image>().color = d.isBarracks ? GameUITheme.StatusBad : GameUITheme.StatusWarning;
        }
        else
        {
            goProgressBG.SetActive(false);
        }
    }

    // ── Panel-Aufbau (alles per Code) ────────────────────────────────────────

    private void BuildPanel()
    {
        // Canvas suchen / erstellen
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            var cGO = new GameObject("InfoPanel_Canvas");
            canvas = cGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            cGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cGO.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            cGO.AddComponent<GraphicRaycaster>();
        }

        // ── Äußerer Rahmen (Deutlich vergrößert für premium Look) ─────────────
        panelRoot = MakeImage("BuildingInfoPanel", canvas.transform, borderColor);
        rootRT = panelRoot.GetComponent<RectTransform>();
        rootRT.anchorMin        = new Vector2(1f, 0.5f);
        rootRT.anchorMax        = new Vector2(1f, 0.5f);
        rootRT.pivot            = new Vector2(1f, 0.5f);
        rootRT.sizeDelta        = new Vector2(410f, 780f); // Groß und übersichtlich
        
        // Schatten / Gold-Outline hinzufügen
        var panelOutline = panelRoot.AddComponent<Outline>();
        panelOutline.effectColor = GameUITheme.ShadowOutline;
        panelOutline.effectDistance = new Vector2(4f, -4f);

        // ── Innerer Hintergrund ──────────────────────────────────────────────
        var inner = MakeImage("InnerBG", panelRoot.transform, bgColor);
        var innerRT = inner.GetComponent<RectTransform>();
        innerRT.anchorMin = Vector2.zero; innerRT.anchorMax = Vector2.one;
        innerRT.offsetMin = new Vector2(3, 3); innerRT.offsetMax = new Vector2(-3, -3);

        var vl = inner.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(20, 20, 20, 20); // Mehr Spacing/Padding für Premium-Haptik
        vl.spacing = 14f;
        vl.childAlignment = TextAnchor.UpperLeft;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        // ── Header-Zeile (Name + Schließen-X) ───────────────────────────────
        var headerRow = MakeLayoutRow("HeaderRow", inner.transform, 0f);

        txtName = MakeTMP("NameLabel", headerRow.transform, "", 23f, FontStyles.Bold, valueColor, TextAlignmentOptions.Left);
        var nameLE = txtName.gameObject.AddComponent<LayoutElement>();
        nameLE.flexibleWidth = 1f;

        var closeBtn = MakeButton("CloseBtn", headerRow.transform, "✕", btnDanger, 34f, 34f, 18f);
        closeBtn.onClick.AddListener(Hide);

        // ── Trennlinie ───────────────────────────────────────────────────────
        MakeDivider(inner.transform);

        // ── Status ───────────────────────────────────────────────────────────
        txtStatus = MakeTMP("StatusLabel", inner.transform, "", 16f, FontStyles.Normal, labelColor);
        AddLE(txtStatus.gameObject, minH: 24f);

        // ── Progress Bar (Fortschrittsbalken) ────────────────────────────────
        goProgressBG = MakeImage("ProgressBarBG", inner.transform, GameUITheme.BarBackground);
        AddLE(goProgressBG, minH: 16f);
        var bgOutline = goProgressBG.AddComponent<Outline>();
        bgOutline.effectColor = GameUITheme.ShadowOutline;
        bgOutline.effectDistance = new Vector2(1f, -1f);

        var fillGo = MakeImage("ProgressBarFill", goProgressBG.transform, GameUITheme.StatusWarning);
        rtProgressFill = fillGo.GetComponent<RectTransform>();
        rtProgressFill.anchorMin = Vector2.zero;
        rtProgressFill.anchorMax = new Vector2(0f, 1f);
        rtProgressFill.pivot     = new Vector2(0f, 0.5f);
        rtProgressFill.offsetMin = Vector2.zero;
        rtProgressFill.offsetMax = Vector2.zero;

        // ── Trennlinie ───────────────────────────────────────────────────────
        MakeDivider(inner.transform);

        // ── Stats-Block ──────────────────────────────────────────────────────
        txtProduces      = MakeStatRow(inner.transform, "");
        txtConsumes      = MakeStatRow(inner.transform, "");
        txtTotalProduced = MakeStatRow(inner.transform, "");

        MakeDivider(inner.transform);

        txtWorkers = MakeStatRow(inner.transform, "");
        txtSize    = MakeStatRow(inner.transform, "");

        MakeDivider(inner.transform);
        txtSchedule = MakeStatRow(inner.transform, "");

        btnToggleSchedule = MakeButton("ToggleScheduleBtn", inner.transform, "📅 Plan ändern", btnNeutral, 350f, 40f, 14f);
        txtScheduleBtnLabel = btnToggleSchedule.GetComponentInChildren<TextMeshProUGUI>();
        btnToggleSchedule.onClick.AddListener(OnToggleScheduleClicked);
        AddLE(btnToggleSchedule.gameObject, minW: 350f, minH: 40f);

        // Hut type toggle button for houses
        btnToggleHutType = MakeButton("ToggleHutTypeBtn", inner.transform, "🏠 Typ: Wohnhaus", btnNeutral, 350f, 40f, 14f);
        txtHutTypeBtnLabel = btnToggleHutType.GetComponentInChildren<TextMeshProUGUI>();
        btnToggleHutType.onClick.AddListener(OnToggleHutTypeClicked);
        AddLE(btnToggleHutType.gameObject, minW: 350f, minH: 40f);

        // ── Barracks UI ──────────────────────────────────────────────────────
        BuildBarracksUI(inner.transform);

        // ── Spacer ───────────────────────────────────────────────────────────
        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(inner.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;

        MakeDivider(inner.transform);

        // ── Buttons ──────────────────────────────────────────────────────────
        var btnRow = MakeLayoutRow("BtnRow", inner.transform, 16f);

        btnPause = MakeButton("PauseBtn", btnRow.transform, "⏸  Pausieren", btnWarning, 170f, 48f, 15f);
        txtPauseLabel = btnPause.GetComponentInChildren<TextMeshProUGUI>();
        btnPause.onClick.AddListener(OnPauseClicked);
        AddLE(btnPause.gameObject, minW: 170f, minH: 48f);

        btnDemolish = MakeButton("DemolishBtn", btnRow.transform, "🔨 Abreißen", btnDanger, 170f, 48f, 15f);
        btnDemolish.onClick.AddListener(OnDemolishClicked);
        AddLE(btnDemolish.gameObject, minW: 170f, minH: 48f);
    }

    // ── Button-Callbacks ─────────────────────────────────────────────────────

    private void OnPauseClicked()
    {
        if (currentBuilding == null) return;
        var buildings = GetBuildingsForBatch();
        foreach (var b in buildings)
        {
            if (b != null) b.ToggleProduction();
        }
        RefreshStats();
    }

    private void OnDemolishClicked()
    {
        if (currentBuilding == null) return;
        var buildings = GetBuildingsForBatch();
        Hide();
        for (int i = buildings.Count - 1; i >= 0; i--)
        {
            if (buildings[i] != null) buildings[i].Demolish();
        }
    }

    private void OnToggleScheduleClicked()
    {
        if (currentBuilding == null) return;
        
        var buildings = GetBuildingsForBatch();
        foreach (var b in buildings)
        {
            if (b == null) continue;
            int current = (int)b.currentSchedule;
            current = (current + 1) % 3;
            b.currentSchedule = (BuildingInstance.ScheduleMode)current;
        }
        RefreshStats();
    }

    private List<BuildingInstance> GetBuildingsForBatch()
    {
        if (SelectionManager.Instance == null)
            return new List<BuildingInstance> { currentBuilding };
        var sel = SelectionManager.Instance.GetSelectedBuildings();
        bool containsCurrent = false;
        foreach (var b in sel)
        {
            if (b == currentBuilding) { containsCurrent = true; break; }
        }
        if (sel.Count > 1 && containsCurrent)
            return new List<BuildingInstance>(sel);
        return new List<BuildingInstance> { currentBuilding };
    }

    private void OnToggleHutTypeClicked()
    {
        if (currentBuilding == null) return;
        var buildings = GetBuildingsForBatch();
        bool anySuccess = false;
        foreach (var b in buildings)
        {
            if (b != null && b.ToggleHutType())
                anySuccess = true;
        }
        if (anySuccess)
        {
            RefreshStats();
        }
        else
        {
            GameObject textGO = new GameObject("HutTypeErrorText");
            textGO.transform.position = currentBuilding.transform.position + Vector3.up * 1.2f;
            var textMesh = textGO.AddComponent<TextMesh>();
            textMesh.text = "⚠️ Weizen fehlt!";
            textMesh.fontSize = 22;
            textMesh.characterSize = 0.07f;
            textMesh.color = GameUITheme.StatusBad;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textGO.AddComponent<BuildingErrorTextMover>();
            Destroy(textGO, 1.8f);
        }
    }

    // ── Helper-Methoden ──────────────────────────────────────────────────────

    private string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    private void SetTowerRangeVisible(BuildingInstance building, bool visible)
    {
        if (building == null || building.data == null || !building.data.isDefenseTower) return;

        DefenseTower tower = building.GetComponent<DefenseTower>();
        if (tower != null)
        {
            tower.SetRangeVisible(visible);
        }
    }

    private static GameObject MakeImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static TextMeshProUGUI MakeTMP(string name, Transform parent, string text,
        float size, FontStyles style, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
        tmp.color = color; tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    private TextMeshProUGUI MakeStatRow(Transform parent, string text)
    {
        var lbl = MakeTMP("Stat", parent, text, 15f, FontStyles.Normal, valueColor);
        AddLE(lbl.gameObject, minH: 32f);
        return lbl;
    }

    private static void MakeDivider(Transform parent)
    {
        var div = MakeImage("Divider", parent, GameUITheme.OutlineGold);
        AddLE(div, minH: 2f);
    }

    private static GameObject MakeLayoutRow(string name, Transform parent, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = spacing;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandHeight = false;
        hl.childForceExpandWidth = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 40f;
        return go;
    }

    private Button MakeButton(string name, Transform parent, string label,
        Color bg, float w, float h, float fontSize)
    {
        var go = MakeImage(name, parent, bg);
        var btn = go.AddComponent<Button>();

        var cols = btn.colors;
        cols.highlightedColor = new Color(bg.r * 1.3f, bg.g * 1.3f, bg.b * 1.3f);
        cols.pressedColor     = new Color(bg.r * 0.7f, bg.g * 0.7f, bg.b * 0.7f);
        btn.colors = cols;
        btn.targetGraphic = go.GetComponent<Image>();

        var outline = go.AddComponent<Outline>();
        outline.effectColor    = borderColor;
        outline.effectDistance = new Vector2(2f, -2f);

        var txt = MakeTMP("Label", go.transform, label, fontSize, FontStyles.Bold, valueColor, TextAlignmentOptions.Center);
        var txtRT = txt.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        txt.raycastTarget = false;

        return btn;
    }

    private static void AddLE(GameObject go, float minW = 0f, float minH = 0f)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        if (minW > 0) le.minWidth  = minW;
        if (minH > 0) le.minHeight = minH;
    }
    private static void AddLE(Component c, float minW = 0f, float minH = 0f) => AddLE(c.gameObject, minW, minH);

    private void BuildBarracksUI(Transform parent)
    {
        barracksContainer = new GameObject("BarracksContainer", typeof(RectTransform));
        barracksContainer.transform.SetParent(parent, false);

        var vl = barracksContainer.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 8f;
        vl.padding = new RectOffset(0, 0, 4, 4);
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.childAlignment = TextAnchor.UpperLeft;

        var barLE = barracksContainer.AddComponent<LayoutElement>();
        barLE.flexibleWidth = 1f;

        // ── Section 1: Soldatentypen ──────────────────────────────────────
        MakeDivider(barracksContainer.transform);

        var txtTypesTitle = MakeTMP("TypesTitle", barracksContainer.transform,
            "<b>⚔ Ausbildungstypen:</b>", 13f, FontStyles.Normal, labelColor);
        AddLE(txtTypesTitle.gameObject, minH: 20f);

        var typesRow = MakeBarracksRow("TypesRow", barracksContainer.transform);
        btnToggleSpear  = MakeBarracksBtn("SpearToggle",  typesRow.transform, "🏹 Speer",   btnNeutral);
        btnToggleShield = MakeBarracksBtn("ShieldToggle", typesRow.transform, "🛡 Schild",  btnNeutral);
        btnToggleSword  = MakeBarracksBtn("SwordToggle",  typesRow.transform, "⚔ Schwert", btnNeutral);
        btnToggleBow    = MakeBarracksBtn("BowToggle",    typesRow.transform, "🏹 Bogen",   btnNeutral);

        btnToggleSpear .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.spearSelected  = !currentBuilding.spearSelected;  RefreshStats(); } });
        btnToggleShield.onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.shieldSelected = !currentBuilding.shieldSelected; RefreshStats(); } });
        btnToggleSword .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.swordSelected  = !currentBuilding.swordSelected;  RefreshStats(); } });
        btnToggleBow   .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.bowSelected    = !currentBuilding.bowSelected;    RefreshStats(); } });

        // ── Section 2: Ausrüstungs-Material ──────────────────────────────
        MakeDivider(barracksContainer.transform);

        var txtMaterialTitle = MakeTMP("MaterialTitle", barracksContainer.transform,
            "<b>🪨 Ausrüstungs-Qualität:</b>", 13f, FontStyles.Normal, labelColor);
        AddLE(txtMaterialTitle.gameObject, minH: 20f);

        var resRow  = MakeBarracksRow("ResRow", barracksContainer.transform);
        btnResWood  = MakeBarracksBtn("ResWood",  resRow.transform, "🪵 Holz",  btnNeutral);
        btnResStone = MakeBarracksBtn("ResStone", resRow.transform, "🪨 Stein", btnNeutral);
        btnResGold  = MakeBarracksBtn("ResGold",  resRow.transform, "🥇 Gold",  btnNeutral);
        btnResIron  = MakeBarracksBtn("ResIron",  resRow.transform, "⛏ Eisen", btnNeutral);

        btnResWood .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.selectedResource = BuildingInstance.BarracksResource.Wood;  RefreshStats(); } });
        btnResStone.onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.selectedResource = BuildingInstance.BarracksResource.Stone; RefreshStats(); } });
        btnResGold .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.selectedResource = BuildingInstance.BarracksResource.Gold;  RefreshStats(); } });
        btnResIron .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.selectedResource = BuildingInstance.BarracksResource.Iron;  RefreshStats(); } });

        // ── Section 3: Autorekrutierung ───────────────────────────────────
        MakeDivider(barracksContainer.transform);

        var txtAutoTitle = MakeTMP("AutoTitle", barracksContainer.transform,
            "<b>🤖 Automatisierung:</b>", 13f, FontStyles.Normal, labelColor);
        AddLE(txtAutoTitle.gameObject, minH: 20f);

        btnAutoRecruit = MakeButton("AutoRecruitBtn", barracksContainer.transform,
            "Autorekrutierung (20%): AUS", btnWarning, 360f, 36f, 13f);
        AddLE(btnAutoRecruit.gameObject, minH: 36f);
        btnAutoRecruit.onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.autoRecruit = !currentBuilding.autoRecruit; RefreshStats(); } });

        // ── Section 4: Soldaten bestellen ─────────────────────────────────
        MakeDivider(barracksContainer.transform);

        var txtOrderTitle = MakeTMP("OrderTitle", barracksContainer.transform,
            "<b>📋 Auftrag erteilen:</b>", 13f, FontStyles.Normal, labelColor);
        AddLE(txtOrderTitle.gameObject, minH: 20f);

        var orderRow  = MakeBarracksRow("OrderRow", barracksContainer.transform);
        btnOrderSpear  = MakeBarracksBtn("OrdSpear",  orderRow.transform, "+Speer",   btnNeutral);
        btnOrderShield = MakeBarracksBtn("OrdShield", orderRow.transform, "+Schild",  btnNeutral);
        btnOrderSword  = MakeBarracksBtn("OrdSword",  orderRow.transform, "+Schwert", btnNeutral);
        btnOrderBow    = MakeBarracksBtn("OrdBow",    orderRow.transform, "+Bogen",   btnNeutral);

        btnOrderSpear .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.OrderSoldier(SoldierType.Spear);  RefreshStats(); } });
        btnOrderShield.onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.OrderSoldier(SoldierType.Shield); RefreshStats(); } });
        btnOrderSword .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.OrderSoldier(SoldierType.Sword);  RefreshStats(); } });
        btnOrderBow   .onClick.AddListener(() => { if (currentBuilding != null) { currentBuilding.OrderSoldier(SoldierType.Bow);    RefreshStats(); } });

        // ── Queue Status ──────────────────────────────────────────────────
        txtQueueStatus = MakeTMP("QueueStatus", barracksContainer.transform,
            "Warteschlange: Leer", 13f, FontStyles.Italic, valueColor);
        AddLE(txtQueueStatus.gameObject, minH: 22f);
    }

    /// <summary>Creates a horizontal row suited for barracks button groups.</summary>
    private static GameObject MakeBarracksRow(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth  = false;
        hl.childForceExpandHeight = false;

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 34f;
        le.flexibleWidth = 1f;

        var csf = go.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return go;
    }

    /// <summary>Creates a small barracks-style button with explicit fixed size.</summary>
    private Button MakeBarracksBtn(string name, Transform parent, string label, Color bg)
    {
        var go = MakeImage(name, parent, bg);

        var le = go.AddComponent<LayoutElement>();
        le.minWidth  = 85f;
        le.minHeight = 32f;
        le.preferredWidth  = 85f;
        le.preferredHeight = 32f;

        var btn = go.AddComponent<Button>();
        var cols = btn.colors;
        cols.highlightedColor = new Color(Mathf.Min(bg.r * 1.35f, 1f), Mathf.Min(bg.g * 1.35f, 1f), Mathf.Min(bg.b * 1.35f, 1f));
        cols.pressedColor     = new Color(bg.r * 0.7f, bg.g * 0.7f, bg.b * 0.7f);
        btn.colors = cols;
        btn.targetGraphic = go.GetComponent<Image>();

        var outline = go.AddComponent<Outline>();
        outline.effectColor    = borderColor;
        outline.effectDistance = new Vector2(2f, -2f);

        var txt = MakeTMP("Label", go.transform, label, 11f, FontStyles.Bold, valueColor, TextAlignmentOptions.Center);
        var txtRT = txt.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        txt.raycastTarget = false;
        txt.textWrappingMode = TextWrappingModes.NoWrap;

        return btn;
    }

    private void UpdateButtonToggleState(Button btn, bool isSelected)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (isSelected)
        {
            img.color = GameUITheme.ButtonConfirm;
            if (txt != null) txt.color = Color.white;
        }
        else
        {
            img.color = GameUITheme.ButtonPressed;
            if (txt != null) txt.color = GameUITheme.LabelSecondary;
        }
    }

    private void UpdateButtonResourceState(Button btn, bool isActive, Color activeColor)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (isActive)
        {
            img.color = activeColor;
            if (txt != null) txt.color = Color.white;
            
            var o = btn.GetComponent<Outline>();
            if (o == null) o = btn.gameObject.AddComponent<Outline>();
            o.effectColor = GameUITheme.OutlineGoldStrong;
            o.effectDistance = new Vector2(2f, -2f);
        }
        else
        {
            img.color = GameUITheme.ButtonPressed; // dunkle Inaktiv-Fläche
            if (txt != null) txt.color = GameUITheme.LabelPlaceholder;
            
            var o = btn.GetComponent<Outline>();
            if (o != null) Destroy(o);
        }
    }
}

// Procedural text animator for error alerts in the game scene
public class BuildingErrorTextMover : MonoBehaviour
{
    private void Update()
    {
        transform.Translate(Vector3.up * Time.deltaTime * 0.6f);
    }
}
