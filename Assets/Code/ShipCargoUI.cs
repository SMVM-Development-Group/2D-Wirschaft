using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Ship Cargo UI Panel - im Stil des BuildingInfoPanel (rechts eingeschoben).
/// Zeigt Schiffs-Slots und 3 Lade-Buttons: Soldaten, Bauarbeiter, Material.
/// </summary>
public class ShipCargoUI : MonoBehaviour
{
    public static ShipCargoUI Instance { get; private set; }

    // ── Stil-Farben (zentrale Palette: GameUITheme) ──────────────────────
    private readonly Color bgColor     = GameUITheme.PanelBackground;
    private readonly Color borderColor = GameUITheme.OutlineGold;
    private readonly Color labelColor  = GameUITheme.LabelParchment;
    private readonly Color valueColor  = GameUITheme.LabelPrimary;
    private readonly Color btnNeutral  = GameUITheme.ButtonConfirm;
    private readonly Color slotColor   = GameUITheme.BarBackground;

    // ── Panel-Objekte ────────────────────────────────────────────────────
    private GameObject panelRoot;
    private RectTransform rootRT;
    private TextMeshProUGUI txtName;
    private TextMeshProUGUI txtCrew;
    private TextMeshProUGUI txtCapacity;
    private Transform slotContainer;
    private GameObject itemSlotPrefab;

    // ── Sub-UI ───────────────────────────────────────────────────────────
    private GameObject subUIRoot;
    private TextMeshProUGUI subUITitle;
    private Transform subUIContent;
    private Button subUIClose;

    // ── Animation ────────────────────────────────────────────────────────
    private float targetPosX = 480f;
    private float currentPosX = 480f;
    private const float AnimSpeed = 12f;
    private bool isPanelActive = false;

    // ── State ────────────────────────────────────────────────────────────
    private Ship currentShip;
    private List<GameObject> slotObjects = new List<GameObject>();

    // ────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (panelRoot == null) BuildPanel();
        if (rootRT != null)
        {
            if (!isPanelActive)
            {
                currentPosX = 480f;
                targetPosX = 480f;
            }
            rootRT.anchoredPosition = new Vector2(currentPosX, 0f);
        }
        if (!isPanelActive) panelRoot.SetActive(false);
    }

    private void Update()
    {
        currentPosX = Mathf.Lerp(currentPosX, targetPosX, Time.deltaTime * AnimSpeed);
        if (rootRT != null)
            rootRT.anchoredPosition = new Vector2(currentPosX, 0f);

        if (!isPanelActive && currentPosX >= 470f)
        {
            currentShip = null;
            panelRoot.SetActive(false);
            return;
        }

        if (currentShip != null && isPanelActive)
        {
            UpdateCrewStatus();
            UpdateCapacityDisplay();
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            HideShipUI();
    }

    // ── API ──────────────────────────────────────────────────────────────

    public void ShowShipUI(Ship ship)
    {
        if (ship == null) return;

        if (panelRoot == null)
        {
            BuildPanel();
            panelRoot.SetActive(false);
        }

        currentShip = ship;

        if (!ship.HasCrew())
        {
            ship.AssignFirstAvailableVillager();
        }

        isPanelActive = true;
        subUIRoot?.SetActive(false);
        panelRoot.SetActive(true);

        if (currentPosX >= 470f) currentPosX = 480f;
        targetPosX = -30f;

        if (txtName != null) txtName.text = ship.GetShipName().ToUpper();
        UpdateCrewStatus();
        UpdateCapacityDisplay();
        RefreshSlots();
    }

    public void HideShipUI()
    {
        targetPosX = 480f;
        isPanelActive = false;
        subUIRoot?.SetActive(false);
    }

    public bool IsVisible => isPanelActive;

    // ── Panel bauen ──────────────────────────────────────────────────────

    private void BuildPanel()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            var cGO = new GameObject("ShipCargo_Canvas");
            canvas = cGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            cGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cGO.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            cGO.AddComponent<GraphicRaycaster>();
        }

        panelRoot = MakeImage("ShipCargoPanel", canvas.transform, borderColor);
        rootRT = panelRoot.GetComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(1f, 0.5f);
        rootRT.anchorMax = new Vector2(1f, 0.5f);
        rootRT.pivot = new Vector2(1f, 0.5f);
        rootRT.sizeDelta = new Vector2(520f, 640f);

        var outline = panelRoot.AddComponent<Outline>();
        outline.effectColor = GameUITheme.ShadowOutline;
        outline.effectDistance = new Vector2(4f, -4f);

        var inner = MakeImage("InnerBG", panelRoot.transform, bgColor);
        var innerRT = inner.GetComponent<RectTransform>();
        innerRT.anchorMin = Vector2.zero; innerRT.anchorMax = Vector2.one;
        innerRT.offsetMin = new Vector2(3, 3); innerRT.offsetMax = new Vector2(-3, -3);

        var vl = inner.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(16, 16, 16, 16);
        vl.spacing = 10f;
        vl.childAlignment = TextAnchor.UpperLeft;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        // Header
        var headerRow = MakeLayoutRow("HeaderRow", inner.transform, 0f, 40f);
        txtName = MakeTMP("ShipName", headerRow.transform, "", 20f, FontStyles.Bold, valueColor, TextAlignmentOptions.Left);
        var nameLE = txtName.gameObject.AddComponent<LayoutElement>();
        nameLE.flexibleWidth = 1f;

        var btnClose = MakeButton("CloseBtn", headerRow.transform, "✕", GameUITheme.ButtonDanger, 34f, 34f, 16f);
        btnClose.onClick.AddListener(HideShipUI);

        MakeDivider(inner.transform);

        // Crew & Capacity
        txtCrew = MakeTMP("CrewStatus", inner.transform, "", 15f, FontStyles.Normal, labelColor);
        AddLE(txtCrew.gameObject, minH: 24f);

        txtCapacity = MakeTMP("Capacity", inner.transform, "", 15f, FontStyles.Normal, labelColor);
        AddLE(txtCapacity.gameObject, minH: 24f);

        MakeDivider(inner.transform);

        // Slot container (GridLayoutGroup)
        var slotRow = new GameObject("SlotRow", typeof(RectTransform));
        slotRow.transform.SetParent(inner.transform, false);
        var slotVL = slotRow.AddComponent<VerticalLayoutGroup>();
        slotVL.spacing = 4f;
        slotVL.childAlignment = TextAnchor.UpperLeft;
        slotVL.childForceExpandWidth = true;
        slotVL.childForceExpandHeight = false;
        AddLE(slotRow, minH: 140f, flexW: 1f, flexH: 1f);

        var slotContGO = new GameObject("Slots", typeof(RectTransform));
        slotContGO.transform.SetParent(slotRow.transform, false);
        var slotGrid = slotContGO.AddComponent<GridLayoutGroup>();
        slotGrid.cellSize = new Vector2(60f, 60f);
        slotGrid.spacing = new Vector2(4f, 4f);
        slotGrid.constraint = GridLayoutGroup.Constraint.Flexible;
        slotGrid.childAlignment = TextAnchor.UpperLeft;
        slotGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        slotGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
        var slotLE = slotContGO.AddComponent<LayoutElement>();
        slotLE.flexibleWidth = 1f;
        slotLE.flexibleHeight = 1f;
        slotLE.minHeight = 64f;
        slotContainer = slotContGO.transform;

        // Spacer
        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(inner.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;

        MakeDivider(inner.transform);

        // Action Buttons Row
        var btnRow = MakeLayoutRow("BtnRow", inner.transform, 8f, 44f);

        var btnSoldiers = MakeButton("SoldiersBtn", btnRow.transform, "Soldaten", btnNeutral, 140f, 40f, 13f);
        btnSoldiers.onClick.AddListener(OnLoadSoldiersClicked);

        var btnBuilders = MakeButton("BuildersBtn", btnRow.transform, "Bauarbeiter", btnNeutral, 140f, 40f, 13f);
        btnBuilders.onClick.AddListener(OnLoadBuildersClicked);

        MakeDivider(inner.transform);

        // Sail + Unload buttons
        var actionRow = MakeLayoutRow("ActionRow", inner.transform, 8f, 44f);

        var btnSail = MakeButton("SailBtn", actionRow.transform, "Segeln", btnNeutral, 140f, 40f, 14f);
        btnSail.onClick.AddListener(OnSailButtonClicked);

        var btnUnload = MakeButton("UnloadBtn", actionRow.transform, "Entladen", btnNeutral, 140f, 40f, 13f);
        btnUnload.onClick.AddListener(OnUnloadClicked);

        // Item-Slot Prefab
        itemSlotPrefab = new GameObject("ItemSlotTemplate");
        itemSlotPrefab.transform.SetParent(panelRoot.transform, false);
        itemSlotPrefab.SetActive(false);
        var slotRT = itemSlotPrefab.AddComponent<RectTransform>();
        slotRT.sizeDelta = new Vector2(60f, 60f);
        var slotImg = itemSlotPrefab.AddComponent<Image>();
        slotImg.color = slotColor;
        var slotOutline2 = itemSlotPrefab.AddComponent<Outline>();
        slotOutline2.effectColor = borderColor;
        slotOutline2.effectDistance = new Vector2(2f, -2f);
        var slotLE2 = itemSlotPrefab.AddComponent<LayoutElement>();
        slotLE2.preferredWidth = 60;
        slotLE2.preferredHeight = 60;

        // Sub-UI (overlay)
        BuildSubUI(canvas.transform);

        Debug.Log("[ShipCargoUI] Panel erstellt.");
    }

    private void BuildSubUI(Transform canvasParent)
    {
        subUIRoot = MakeImage("SubUI_Overlay", canvasParent, GameUITheme.OverlayVeil);
        var subRT = subUIRoot.GetComponent<RectTransform>();
        subRT.anchorMin = Vector2.zero; subRT.anchorMax = Vector2.one;
        subRT.sizeDelta = Vector2.zero;
        subUIRoot.SetActive(false);

        var subFrame = MakeImage("SubUI_Frame", subUIRoot.transform, bgColor);
        var frameRT = subFrame.GetComponent<RectTransform>();
        frameRT.anchorMin = new Vector2(0.5f, 0.5f);
        frameRT.anchorMax = new Vector2(0.5f, 0.5f);
        frameRT.pivot = new Vector2(0.5f, 0.5f);
        frameRT.sizeDelta = new Vector2(400f, 300f);

        var frameVL = subFrame.AddComponent<VerticalLayoutGroup>();
        frameVL.padding = new RectOffset(16, 16, 16, 16);
        frameVL.spacing = 10f;
        frameVL.childAlignment = TextAnchor.UpperCenter;
        frameVL.childForceExpandWidth = true;

        var headerRow = MakeLayoutRow("SubHeader", subFrame.transform, 0f, 36f);
        subUITitle = MakeTMP("SubTitle", headerRow.transform, "", 18f, FontStyles.Bold, valueColor, TextAlignmentOptions.Left);
        AddLE(subUITitle.gameObject, flexW: 1f);
        subUIClose = MakeButton("SubClose", headerRow.transform, "✕", GameUITheme.ButtonDanger, 30f, 30f, 14f);
        subUIClose.onClick.AddListener(() => subUIRoot.SetActive(false));

        MakeDivider(subFrame.transform);

        subUIContent = new GameObject("SubContent", typeof(RectTransform)).transform;
        subUIContent.SetParent(subFrame.transform, false);
        var contentVL = subUIContent.gameObject.AddComponent<VerticalLayoutGroup>();
        contentVL.spacing = 6f;
        contentVL.childAlignment = TextAnchor.UpperCenter;
        contentVL.childForceExpandWidth = true;
        contentVL.childForceExpandHeight = false;
        AddLE(subUIContent.gameObject, flexW: 1f, flexH: 1f);
    }

    // ── Slots ────────────────────────────────────────────────────────────

    private void RefreshSlots()
    {
        foreach (var s in slotObjects) Destroy(s);
        slotObjects.Clear();

        if (currentShip == null || slotContainer == null) return;

        foreach (var slot in currentShip.slots)
        {
            CreateSlotUI(slot);
        }
    }

    private void CreateSlotUI(ShipSlot slot)
    {
        if (itemSlotPrefab == null) return;

        var obj = Instantiate(itemSlotPrefab, slotContainer);
        obj.SetActive(true);

        if (slot.IsEmpty)
        {
            obj.name = "EmptySlot";
            obj.GetComponent<Image>().color = GameUITheme.OverlayVeil;
            var dash = MakeTMP("Dash", obj.transform, "-", 18f, FontStyles.Normal, GameUITheme.LabelPlaceholder, TextAlignmentOptions.Center);
            var dRT = dash.GetComponent<RectTransform>();
            dRT.anchorMin = Vector2.zero; dRT.anchorMax = Vector2.one;
            dRT.sizeDelta = Vector2.zero;
        }
        else
        {
            string label = "";
            Color labelColor = valueColor;
            Sprite iconSprite = null;

            if (slot.content == ShipSlot.SlotContent.Material)
            {
                obj.name = $"Mat_{slot.resourceId}";
                label = slot.amount.ToString();
                iconSprite = LoadOverlaySprite(slot.resourceId);
            }
            else if (slot.content == ShipSlot.SlotContent.Builder)
            {
                obj.name = "Builder";
                label = "Bu";
                labelColor = GameUITheme.StatusWarning;
                iconSprite = LoadBuilderSprite();
            }
            else if (slot.content == ShipSlot.SlotContent.Soldier)
            {
                obj.name = $"Soldier_{slot.soldierType}";
                label = slot.soldierType.ToString().Substring(0, 2);
                labelColor = GameUITheme.StatusBad;
                iconSprite = LoadSoldierSprite(slot.soldierType);
            }

            if (iconSprite != null)
            {
                var iconGO = new GameObject("SlotIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGO.transform.SetParent(obj.transform, false);
                var iconImg = iconGO.GetComponent<Image>();
                iconImg.sprite = iconSprite;
                iconImg.preserveAspect = true;
                var iRT = iconGO.GetComponent<RectTransform>();
                iRT.anchorMin = new Vector2(0f, 0.3f);
                iRT.anchorMax = new Vector2(1f, 1f);
                iRT.offsetMin = Vector2.zero;
                iRT.offsetMax = Vector2.zero;
            }

            var txt = MakeTMP("Content", obj.transform, label, 14f, FontStyles.Bold, labelColor, TextAlignmentOptions.Center);
            var tRT = txt.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
            tRT.sizeDelta = Vector2.zero;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = obj.GetComponent<Image>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.5f, 0.8f, 1f, 1f);
            btn.colors = colors;
        }

        slotObjects.Add(obj);
    }

    // ── Sub-UI: Soldaten ─────────────────────────────────────────────────

    private void OnLoadSoldiersClicked()
    {
        if (currentShip == null || !currentShip.HasFreeSlot()) return;
        ShowSubUI("Soldaten einladen", (content) =>
        {
            foreach (SoldierType type in System.Enum.GetValues(typeof(SoldierType)))
            {
                var row = MakeLayoutRow($"Row_{type}", content, 6f, 36f);
                var lbl = MakeTMP("Label", row.transform, type.ToString(), 14f, FontStyles.Normal, valueColor, TextAlignmentOptions.Left);
                AddLE(lbl.gameObject, flexW: 1f);
                var btn = MakeButton("LoadBtn", row.transform, "Laden", btnNeutral, 100f, 32f, 12f);
                var capturedType = type;
                btn.onClick.AddListener(() =>
                {
                    if (currentShip != null && currentShip.TryLoadSoldier(capturedType))
                    {
                        RefreshSlots();
                        UpdateCapacityDisplay();
                        subUIRoot.SetActive(false);
                    }
                });
            }
        });
    }

    // ── Sub-UI: Bauarbeiter ──────────────────────────────────────────────

    private void OnLoadBuildersClicked()
    {
        if (currentShip == null || !currentShip.HasFreeSlot()) return;
        ShowSubUI("Bauarbeiter einladen", (content) =>
        {
            var lbl = MakeTMP("Info", content, "Baumeister ins Schiff setzen?", 14f, FontStyles.Normal, valueColor, TextAlignmentOptions.Center);
            AddLE(lbl.gameObject, minH: 40f);
            var btn = MakeButton("LoadBtn", content, "1 Bauarbeiter einladen", btnNeutral, 250f, 36f, 13f);
            btn.onClick.AddListener(() =>
            {
                if (currentShip != null && currentShip.TryLoadBuilder())
                {
                    RefreshSlots();
                    UpdateCapacityDisplay();
                    subUIRoot.SetActive(false);
                }
            });
        });
    }

    // ── Sub-UI: Material ─────────────────────────────────────────────────

    private void OnLoadMaterialsClicked()
    {
        if (currentShip == null || !currentShip.HasFreeSlot()) return;
        ShowSubUI("Material einladen", (content) =>
        {
            string[] resources = { "holz", "stein", "eisen", "gold", "weizen", "fruechte", "wüstenfrucht", "fleisch" };
            foreach (var res in resources)
            {
                var row = MakeLayoutRow($"Row_{res}", content, 4f, 34f);

                Sprite resIcon = LoadOverlaySprite(res);
                if (resIcon != null)
                {
                    var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    iconGO.transform.SetParent(row.transform, false);
                    var iconImg = iconGO.GetComponent<Image>();
                    iconImg.sprite = resIcon;
                    iconImg.preserveAspect = true;
                    var iconLE = iconGO.AddComponent<LayoutElement>();
                    iconLE.preferredWidth = 24f;
                    iconLE.preferredHeight = 24f;
                }

                var lbl = MakeTMP("Label", row.transform, res, 13f, FontStyles.Normal, valueColor, TextAlignmentOptions.Left);
                AddLE(lbl.gameObject, flexW: 1f);

                int playerAmount = Player_UI.Instance != null ? Player_UI.Instance.GetResource(res) : 0;
                var amt = MakeTMP("Amt", row.transform, $"({playerAmount})", 12f, FontStyles.Normal, labelColor, TextAlignmentOptions.Right);
                AddLE(amt.gameObject, minW: 40f);

                var input = MakeInputField("Amount", row.transform, "1", 50f, 28f);

                var btn = MakeButton("LoadBtn", row.transform, "Laden", btnNeutral, 60f, 28f, 11f);
                var capturedRes = res;
                btn.onClick.AddListener(() =>
                {
                    int amount = 1;
                    if (!string.IsNullOrEmpty(input.text))
                        int.TryParse(input.text, out amount);
                    if (amount < 1) amount = 1;

                    if (currentShip != null && ResourceManager.Instance != null)
                    {
                        int freeSlots = currentShip.GetFreeSlotCount();
                        int toLoad = Mathf.Min(amount, freeSlots);

                        if (ResourceManager.Instance.HasResource(capturedRes, toLoad) && toLoad > 0)
                        {
                            ResourceManager.Instance.SpendResource(capturedRes, toLoad);
                            currentShip.TryLoadMaterial(capturedRes, toLoad);
                            RefreshSlots();
                            UpdateCapacityDisplay();
                            subUIRoot.SetActive(false);
                        }
                    }
                });
            }
        });
    }

    // ── Sub-UI Helper ────────────────────────────────────────────────────

    private void ShowSubUI(string title, System.Action<Transform> buildContent)
    {
        if (subUIRoot == null) return;
        subUITitle.text = title;

        // Clear old content
        foreach (Transform child in subUIContent)
            Destroy(child.gameObject);

        buildContent(subUIContent);
        subUIRoot.SetActive(true);
    }

    // ── Button Handler ───────────────────────────────────────────────────

    private void OnSailButtonClicked()
    {
        if (currentShip == null) return;
        if (!currentShip.HasCrew())
        {
            NotificationManager.Instance?.Notify("ship_no_crew",
                "Ein Schiff braucht einen Dorfbewohner!", 5f);
            return;
        }
        HideShipUI();

        ShipSailingMode sailing = ShipSailingMode.Instance;
        if (sailing == null)
        {
            GameObject go = new GameObject("ShipSailingMode");
            sailing = go.AddComponent<ShipSailingMode>();
        }
        sailing.EnableSailingMode(currentShip);
    }

    private void OnUnloadClicked()
    {
        if (currentShip == null) return;
        currentShip.UnloadAllToIsland();
        RefreshSlots();
        UpdateCapacityDisplay();
    }

    // ── Updates ──────────────────────────────────────────────────────────

    private void UpdateCrewStatus()
    {
        if (currentShip == null || txtCrew == null) return;
        txtCrew.text = currentShip.HasCrew()
            ? $"<color={GameUITheme.HtmlGood}>Besatzung: ✓</color>"
            : $"<color={GameUITheme.HtmlBad}>Besatzung: ✗ (benötigt Dorfbewohner!)</color>";
    }

    private void UpdateCapacityDisplay()
    {
        if (currentShip == null || txtCapacity == null) return;
        int used = currentShip.GetUsedSlotCount();
        int total = currentShip.GetSlotCount();
        txtCapacity.text = $"Slots: {used}/{total}";
    }

    // ── Helper (identisch zu BuildingInfoPanel) ──────────────────────────

    private static string GetOverlayName(string resourceId)
    {
        switch (resourceId)
        {
            case "holz": return "Wood_Overlay";
            case "stein": return "Stone_Overlay";
            case "eisen": return "Iron_Overlay";
            case "gold": return "Gold_Overlay";
            case "weizen": return "Wheat_Overlay";
            case "fruechte": case "wüstenfrucht": return "Fruit_Overlay";
            case "fleisch": return "Meat_Overlay";
            default: return null;
        }
    }

    private TMP_InputField MakeInputField(string name, Transform parent, string placeholder, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = GameUITheme.InputBackground;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = w;
        le.preferredHeight = h;

        var inputField = go.AddComponent<TMP_InputField>();
        var rt = go.GetComponent<RectTransform>();

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(rt, false);
        var vpRT = viewport.GetComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = new Vector2(6, 4);
        vpRT.offsetMax = new Vector2(-6, -4);

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(vpRT, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;
        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = "";
        tmp.fontSize = 13f;
        tmp.color = valueColor;
        tmp.alignment = TextAlignmentOptions.Left;

        var placeholderGO = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        placeholderGO.transform.SetParent(vpRT, false);
        var pRT = placeholderGO.GetComponent<RectTransform>();
        pRT.anchorMin = Vector2.zero; pRT.anchorMax = Vector2.one;
        pRT.sizeDelta = Vector2.zero;
        var pTMP = placeholderGO.GetComponent<TextMeshProUGUI>();
        pTMP.text = placeholder;
        pTMP.fontSize = 12f;
        pTMP.color = GameUITheme.LabelPlaceholder;
        pTMP.alignment = TextAlignmentOptions.Left;

        inputField.textViewport = vpRT;
        inputField.textComponent = tmp;
        inputField.placeholder = pTMP;
        inputField.characterLimit = 4;
        inputField.contentType = TMP_InputField.ContentType.IntegerNumber;

        return inputField;
    }

    private static Sprite LoadOverlaySprite(string resourceId)
    {
        string name = GetOverlayName(resourceId);
        if (name == null) return null;
        Texture2D tex = Resources.Load<Texture2D>(name);
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        return null;
    }

    private static Sprite LoadBuilderSprite()
    {
        Texture2D tex = Resources.Load<Texture2D>("Textures/dorfbewohner");
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        return null;
    }

    private static Sprite LoadSoldierSprite(SoldierType type)
    {
        string path = null;
        switch (type)
        {
            case SoldierType.Spear: path = "Textures/speersoldat"; break;
            case SoldierType.Shield: path = "Textures/schildsoldat"; break;
            case SoldierType.Sword: path = "Textures/schwertkämpfer"; break;
            case SoldierType.Bow: path = "Textures/bogensoldat"; break;
        }
        if (path == null) return null;
        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        return null;
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

    private Button MakeButton(string name, Transform parent, string label,
        Color bg, float w, float h, float fontSize)
    {
        var go = MakeImage(name, parent, bg);
        var btn = go.AddComponent<Button>();
        var cols = btn.colors;
        cols.highlightedColor = new Color(Mathf.Min(bg.r * 1.3f, 1f), Mathf.Min(bg.g * 1.3f, 1f), Mathf.Min(bg.b * 1.3f, 1f));
        cols.pressedColor = new Color(bg.r * 0.7f, bg.g * 0.7f, bg.b * 0.7f);
        btn.colors = cols;
        btn.targetGraphic = go.GetComponent<Image>();
        var outline = go.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(2f, -2f);
        var txt = MakeTMP("Label", go.transform, label, fontSize, FontStyles.Bold, valueColor, TextAlignmentOptions.Center);
        var txtRT = txt.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        txt.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = w;
        le.preferredHeight = h;
        return btn;
    }

    private static GameObject MakeLayoutRow(string name, Transform parent, float spacing, float minH = 40f)
    {
        var row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = spacing;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = minH;
        le.flexibleWidth = 1f;
        return row;
    }

    private static void MakeDivider(Transform parent)
    {
        var div = MakeImage("Divider", parent, GameUITheme.OutlineGold);
        div.AddComponent<LayoutElement>().minHeight = 2f;
    }

    private static void AddLE(GameObject go, float minW = -1f, float minH = -1f,
        float flexW = -1f, float flexH = -1f)
    {
        var le = go.AddComponent<LayoutElement>();
        if (minW >= 0) le.minWidth = minW;
        if (minH >= 0) le.minHeight = minH;
        if (flexW >= 0) le.flexibleWidth = flexW;
        if (flexH >= 0) le.flexibleHeight = flexH;
    }
}
