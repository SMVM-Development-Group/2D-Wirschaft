using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class TradingUI : MonoBehaviour
{
    public static TradingUI Instance;

    private GameObject tradingPanel;
    private Transform offerListContainer;
    private Transform tabsContainer;
    private Transform createTabsContainer;
    
    private string currentSelectedResource = "holz";
    private string createSelectedResource = "holz";

    private TMP_InputField createAmountInput;
    private TMP_InputField createPriceInput;

    // All tradeable resources in the game
    private readonly string[] tradeableResources = { "holz", "stein", "eisen", "weizen", "wüstenfrucht", "fleisch" };

    // Beautiful UI Colors (zentrale Palette: GameUITheme)
    private Color barColor = GameUITheme.BarBackground;                 // Dunkles Eichenbraun
    private Color slotColor = GameUITheme.SlotWood;                     // Warmes Holzbraun
    private Color borderColor = GameUITheme.AccentGold;                 // Goldene Umrandung
    private Color valueColor = GameUITheme.LabelPrimary;                // Helles Werte-Gold
    private Color selectedColor = GameUITheme.SelectionGold;            // Aktive Auswahl
    private Color buyButtonColor = GameUITheme.ButtonConfirm;           // Dunkelgrün für Kauf
    private Color cancelBtnColor = GameUITheme.ButtonDanger;            // Dunkelrot für Abbruch

    private Dictionary<string, Image> filterTabImages = new Dictionary<string, Image>();
    private Dictionary<string, Image> createTabImages = new Dictionary<string, Image>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (TradingManager.Instance != null)
        {
            TradingManager.Instance.OnOffersUpdated += RefreshOffers;
        }

        BuildUI();
    }

    private void OnDestroy()
    {
        if (TradingManager.Instance != null)
        {
            TradingManager.Instance.OnOffersUpdated -= RefreshOffers;
        }
    }

    public void TogglePanel()
    {
        if (tradingPanel != null)
        {
            bool isActive = !tradingPanel.activeSelf;
            tradingPanel.SetActive(isActive);
            if (isActive)
            {
                RefreshOffers();
                UpdateTabHighlights();
            }
        }
    }

    private void BuildUI()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        // --- Main Panel Wrapper ---
        tradingPanel = new GameObject("TradingScreen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        tradingPanel.transform.SetParent(canvas.transform, false);
        var panelRT = tradingPanel.GetComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.sizeDelta = Vector2.zero;
        
        var bgImage = tradingPanel.GetComponent<Image>();
        bgImage.color = GameUITheme.OverlayVeil; // Semi-transparent dark overlay

        // Close when clicking outside
        var btn = tradingPanel.GetComponent<Button>();
        btn.onClick.AddListener(TogglePanel);

        // --- Window ---
        var windowGO = new GameObject("Window", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        windowGO.transform.SetParent(tradingPanel.transform, false);
        var winRT = windowGO.GetComponent<RectTransform>();
        winRT.anchorMin = new Vector2(0.5f, 0.5f);
        winRT.anchorMax = new Vector2(0.5f, 0.5f);
        winRT.pivot = new Vector2(0.5f, 0.5f);
        winRT.sizeDelta = new Vector2(1050f, 750f);

        // Block click-through
        windowGO.GetComponent<Button>().onClick.AddListener(() => {}); 

        var winImg = windowGO.GetComponent<Image>();
        winImg.color = barColor;

        var outline = windowGO.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(3, -3);

        var vl = windowGO.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(35, 35, 30, 35);
        vl.spacing = 18f;
        vl.childAlignment = TextAnchor.UpperCenter;

        // --- Title ---
        var titleGO = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(windowGO.transform, false);
        var titleTxt = titleGO.GetComponent<TextMeshProUGUI>();
        titleTxt.text = "MARKTPLATZ / AUKTIONSHAUS";
        titleTxt.fontSize = 28f;
        titleTxt.fontStyle = FontStyles.Bold;
        titleTxt.color = borderColor;
        titleTxt.alignment = TextAlignmentOptions.Center;

        // --- Resource Tabs for Filtering ---
        var tabsGO = new GameObject("Tabs", typeof(RectTransform));
        tabsGO.transform.SetParent(windowGO.transform, false);
        var tabsHL = tabsGO.AddComponent<HorizontalLayoutGroup>();
        tabsHL.spacing = 8f;
        tabsHL.childAlignment = TextAnchor.MiddleCenter;
        tabsHL.childForceExpandHeight = false;
        tabsHL.childForceExpandWidth = false;
        tabsContainer = tabsGO.transform;

        foreach (string res in tradeableResources)
        {
            string resName = res;
            Sprite icon = Player_UI.Instance != null ? Player_UI.Instance.GetIcon(resName) : null;
            string displayName = GetDisplayName(resName);

            // Fit resources side by side (width = 140f)
            var tabBtnGO = CreateIconButton(tabsContainer, displayName, icon, () => {
                currentSelectedResource = resName;
                UpdateTabHighlights();
                RefreshOffers();
            }, 140f, 42f);

            filterTabImages[resName] = tabBtnGO.GetComponent<Image>();
        }

        // --- Table Headers ---
        var headerGO = new GameObject("TableHeader", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        headerGO.transform.SetParent(windowGO.transform, false);
        var hImg = headerGO.GetComponent<Image>();
        hImg.color = GameUITheme.PanelBackground;
        var hLE = headerGO.AddComponent<LayoutElement>();
        hLE.preferredHeight = 35f;
        hLE.flexibleWidth = 1f;

        var hOutline = headerGO.AddComponent<Outline>();
        hOutline.effectColor = borderColor;
        hOutline.effectDistance = new Vector2(1, -1);

        var hHL = headerGO.AddComponent<HorizontalLayoutGroup>();
        hHL.padding = new RectOffset(20, 20, 5, 5);
        hHL.spacing = 15f;
        hHL.childAlignment = TextAnchor.MiddleLeft;
        hHL.childForceExpandWidth = false;
        hHL.childControlWidth = true;

        CreateRowText(headerGO.transform, "VERKÄUFER", 200f, FontStyles.Bold, borderColor);
        CreateRowText(headerGO.transform, "MENGE", 120f, FontStyles.Bold, borderColor);
        CreateRowText(headerGO.transform, "PREIS (GESAMT)", 150f, FontStyles.Bold, borderColor);
        CreateRowText(headerGO.transform, "STÜCKPREIS", 150f, FontStyles.Bold, borderColor);

        // --- Offer List Area ---
        var listWrapper = new GameObject("ListWrapper", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        listWrapper.transform.SetParent(windowGO.transform, false);
        var lwImg = listWrapper.GetComponent<Image>();
        lwImg.color = GameUITheme.OverlayVeil;
        var lwOutline = listWrapper.AddComponent<Outline>();
        lwOutline.effectColor = borderColor;
        lwOutline.effectDistance = new Vector2(1, -1);
        
        var lwLE = listWrapper.AddComponent<LayoutElement>();
        lwLE.flexibleHeight = 1f;
        lwLE.flexibleWidth = 1f;

        var scrollRect = listWrapper.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.viewport = listWrapper.GetComponent<RectTransform>();
        listWrapper.AddComponent<RectMask2D>();

        var contentGO = new GameObject("Content", typeof(RectTransform));
        contentGO.transform.SetParent(listWrapper.transform, false);
        var cRT = contentGO.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(0f, 1f);
        cRT.anchorMax = new Vector2(1f, 1f);
        cRT.pivot = new Vector2(0.5f, 1f);
        cRT.anchoredPosition = Vector2.zero;
        cRT.sizeDelta = new Vector2(0, 0);

        scrollRect.content = cRT;
        offerListContainer = contentGO.transform;

        var cVL = contentGO.AddComponent<VerticalLayoutGroup>();
        cVL.padding = new RectOffset(10, 10, 10, 10);
        cVL.spacing = 8f;
        cVL.childAlignment = TextAnchor.UpperCenter;
        cVL.childForceExpandHeight = false;
        cVL.childForceExpandWidth = true;

        var cCSF = contentGO.AddComponent<ContentSizeFitter>();
        cCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // --- Create Offer Area ---
        var createAreaGO = new GameObject("CreateArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        createAreaGO.transform.SetParent(windowGO.transform, false);
        var caImg = createAreaGO.GetComponent<Image>();
        caImg.color = GameUITheme.PanelBackgroundSoft;
        var caOutline = createAreaGO.AddComponent<Outline>();
        caOutline.effectColor = borderColor;
        caOutline.effectDistance = new Vector2(1, -1);

        var caVL = createAreaGO.AddComponent<VerticalLayoutGroup>();
        caVL.padding = new RectOffset(20, 20, 15, 15);
        caVL.spacing = 10f;
        caVL.childAlignment = TextAnchor.MiddleCenter;
        caVL.childForceExpandHeight = false;
        caVL.childForceExpandWidth = true;

        var caLE = createAreaGO.AddComponent<LayoutElement>();
        caLE.preferredHeight = 120f;
        caLE.flexibleHeight = 0;

        // Selection Label & Sub-layout
        var caTopRow = new GameObject("TopRow", typeof(RectTransform));
        caTopRow.transform.SetParent(createAreaGO.transform, false);
        var caTopHL = caTopRow.AddComponent<HorizontalLayoutGroup>();
        caTopHL.spacing = 15f;
        caTopHL.childAlignment = TextAnchor.MiddleLeft;

        var createLabel = new GameObject("CreateLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        createLabel.transform.SetParent(caTopRow.transform, false);
        var clTxt = createLabel.GetComponent<TextMeshProUGUI>();
        clTxt.text = "Ressource verkaufen:";
        clTxt.fontSize = 16f;
        clTxt.fontStyle = FontStyles.Bold;
        clTxt.color = valueColor;
        createLabel.AddComponent<LayoutElement>().preferredWidth = 180f;

        // Resource selector for creation
        var createTabsGO = new GameObject("CreateTabs", typeof(RectTransform));
        createTabsGO.transform.SetParent(caTopRow.transform, false);
        var ctHL = createTabsGO.AddComponent<HorizontalLayoutGroup>();
        ctHL.spacing = 6f;
        createTabsContainer = createTabsGO.transform;

        foreach (string res in tradeableResources)
        {
            string resName = res;
            Sprite icon = Player_UI.Instance != null ? Player_UI.Instance.GetIcon(resName) : null;

            var createTabBtnGO = CreateIconButton(createTabsContainer, "", icon, () => {
                createSelectedResource = resName;
                UpdateTabHighlights();
            }, 50f, 38f);

            createTabImages[resName] = createTabBtnGO.GetComponent<Image>();
        }

        // Action Inputs and Buttons Row
        var caBottomRow = new GameObject("BottomRow", typeof(RectTransform));
        caBottomRow.transform.SetParent(createAreaGO.transform, false);
        var caBottomHL = caBottomRow.AddComponent<HorizontalLayoutGroup>();
        caBottomHL.spacing = 20f;
        caBottomHL.childAlignment = TextAnchor.MiddleCenter;

        // Amount Input
        createAmountInput = CreateInputField(caBottomRow.transform, "Menge", 150f);
        createAmountInput.contentType = TMP_InputField.ContentType.IntegerNumber;

        // Price Input
        createPriceInput = CreateInputField(caBottomRow.transform, "Gesamtpreis (Gold)", 180f);
        createPriceInput.contentType = TMP_InputField.ContentType.IntegerNumber;

        // Create Button
        CreateButton(caBottomRow.transform, "ANGEBOT ERSTELLEN", OnCreateOfferClicked, 220f, 40f, selectedColor, borderColor);

        UpdateTabHighlights();
        tradingPanel.SetActive(false);
    }

    private void UpdateTabHighlights()
    {
        foreach (var pair in filterTabImages)
        {
            if (pair.Key == currentSelectedResource)
            {
                pair.Value.color = selectedColor;
                var outline = pair.Value.GetComponent<Outline>();
                if (outline != null) outline.effectColor = valueColor;
            }
            else
            {
                pair.Value.color = slotColor;
                var outline = pair.Value.GetComponent<Outline>();
                if (outline != null) outline.effectColor = borderColor;
            }
        }

        foreach (var pair in createTabImages)
        {
            if (pair.Key == createSelectedResource)
            {
                pair.Value.color = selectedColor;
                var outline = pair.Value.GetComponent<Outline>();
                if (outline != null) outline.effectColor = valueColor;
            }
            else
            {
                pair.Value.color = slotColor;
                var outline = pair.Value.GetComponent<Outline>();
                if (outline != null) outline.effectColor = borderColor;
            }
        }
    }

    private GameObject CreateIconButton(Transform parent, string text, Sprite icon, UnityEngine.Events.UnityAction onClick, float width, float height)
    {
        var btnGO = new GameObject($"Btn_{text}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);

        var le = btnGO.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;

        var img = btnGO.GetComponent<Image>();
        img.color = slotColor;

        var outline = btnGO.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.selectedColor = Color.white;
        btn.colors = colors;

        btn.onClick.AddListener(() =>
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySelectSound();
            onClick?.Invoke();
        });

        // Horizontal Layout for Icon + Text
        var hl = btnGO.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(6, 6, 4, 4);
        hl.spacing = 6f;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;

        if (icon != null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGO.transform.SetParent(btnGO.transform, false);
            var iImg = iconGO.GetComponent<Image>();
            iImg.sprite = icon;
            iImg.preserveAspect = true;
            iImg.raycastTarget = false;
            
            var iLE = iconGO.AddComponent<LayoutElement>();
            iLE.preferredWidth = height - 10f;
            iLE.preferredHeight = height - 10f;
        }

        if (!string.IsNullOrEmpty(text))
        {
            var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            txtGO.transform.SetParent(btnGO.transform, false);
            var tmp = txtGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 13f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = valueColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }

        return btnGO;
    }

    private void CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick, float width, float height, Color bgCol, Color borderCol)
    {
        var btnGO = new GameObject($"Btn_{text}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);

        var le = btnGO.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;

        var img = btnGO.GetComponent<Image>();
        img.color = bgCol;

        var outline = btnGO.AddComponent<Outline>();
        outline.effectColor = borderCol;
        outline.effectDistance = new Vector2(2, -2);

        var btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.selectedColor = Color.white;
        btn.colors = colors;

        btn.onClick.AddListener(() =>
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySelectSound();
            onClick?.Invoke();
        });

        var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(btnGO.transform, false);
        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;

        var tmp = txtGO.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = valueColor;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    private TMP_InputField CreateInputField(Transform parent, string placeholderText, float width)
    {
        var inputGO = new GameObject("Input_" + placeholderText, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        inputGO.transform.SetParent(parent, false);
        
        var le = inputGO.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 40f;

        var img = inputGO.GetComponent<Image>();
        img.color = GameUITheme.PanelBackground;
        var outline = inputGO.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1, -1);

        var inputField = inputGO.AddComponent<TMP_InputField>();
        
        // Text Component
        var textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(inputGO.transform, false);
        var txtRT = textGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(10f, 0f);
        txtRT.offsetMax = new Vector2(-10f, 0f);
        
        var txt = textGO.GetComponent<TextMeshProUGUI>();
        txt.fontSize = 15f;
        txt.color = valueColor;
        txt.alignment = TextAlignmentOptions.Left;
        
        // Placeholder
        var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        phGO.transform.SetParent(inputGO.transform, false);
        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(10f, 0f);
        phRT.offsetMax = new Vector2(-10f, 0f);
        
        var ph = phGO.GetComponent<TextMeshProUGUI>();
        ph.text = placeholderText;
        ph.fontSize = 14f;
        ph.color = GameUITheme.LabelPlaceholder;
        ph.alignment = TextAlignmentOptions.Left;

        inputField.textComponent = txt;
        inputField.placeholder = ph;
        inputField.textViewport = txtRT;

        return inputField;
    }

    public void RefreshOffers()
    {
        if (TradingManager.Instance == null || offerListContainer == null) return;

        foreach (Transform child in offerListContainer)
        {
            Destroy(child.gameObject);
        }

        List<TradingOffer> allOffers = TradingManager.Instance.GetActiveOffers();
        
        var filteredOffers = allOffers
            .Where(o => o.resourceType == currentSelectedResource)
            .OrderBy(o => (float)o.totalPrice / o.amount)
            .ThenBy(o => o.timestamp)
            .ToList();

        foreach (var offer in filteredOffers)
        {
            CreateOfferRow(offer);
        }
    }

    private void CreateOfferRow(TradingOffer offer)
    {
        var rowGO = new GameObject("OfferRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rowGO.transform.SetParent(offerListContainer, false);
        
        var img = rowGO.GetComponent<Image>();
        img.color = slotColor;
        
        var outline = rowGO.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1, -1);

        var hl = rowGO.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(20, 20, 8, 8);
        hl.spacing = 15f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        hl.childControlWidth = true;

        var le = rowGO.AddComponent<LayoutElement>();
        le.preferredHeight = 55f;

        float pricePerUnit = (float)offer.totalPrice / offer.amount;

        // Seller (Fixed width = 200f)
        CreateRowText(rowGO.transform, offer.sellerName, 200f, FontStyles.Normal, valueColor);

        // Resource details: Amount with Icon (Fixed width = 120f)
        Sprite resIcon = Player_UI.Instance != null ? Player_UI.Instance.GetIcon(offer.resourceType) : null;
        CreateAmountWithIcon(rowGO.transform, offer.amount, resIcon, 120f);

        // Price details: Price with Gold Icon (Fixed width = 150f)
        Sprite goldIcon = Player_UI.Instance != null ? Player_UI.Instance.GetIcon("gold") : null;
        CreateValueWithIcon(rowGO.transform, offer.totalPrice, goldIcon, 150f);

        // Price per unit with Gold Icon (Fixed width = 150f)
        CreateValueWithIcon(rowGO.transform, (float)System.Math.Round(pricePerUnit, 2), goldIcon, 150f);

        // Flexible space
        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(rowGO.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

        bool isOwn = offer.sellerActorNumber == Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber;
        string btnText = isOwn ? "Abbrechen" : "Kaufen";
        Color btnCol = isOwn ? cancelBtnColor : buyButtonColor;

        // Buy button width = 120f
        CreateButton(rowGO.transform, btnText, () => {
            if (isOwn) TradingManager.Instance.CancelOffer(offer.offerId);
            else TradingManager.Instance.BuyOffer(offer.offerId);
        }, 120f, 36f, btnCol, borderColor);
    }

    private void CreateRowText(Transform parent, string text, float width, FontStyles fontStyle, Color color)
    {
        var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(parent, false);
        
        var le = txtGO.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;

        var tmp = txtGO.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 15f;
        tmp.fontStyle = fontStyle;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Left;
    }

    private void CreateAmountWithIcon(Transform parent, int amount, Sprite icon, float width)
    {
        var wrapper = new GameObject("AmountWrapper", typeof(RectTransform));
        wrapper.transform.SetParent(parent, false);
        var le = wrapper.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;

        var hl = wrapper.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;

        if (icon != null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGO.transform.SetParent(wrapper.transform, false);
            var iImg = iconGO.GetComponent<Image>();
            iImg.sprite = icon;
            iImg.preserveAspect = true;
            var iLE = iconGO.AddComponent<LayoutElement>();
            iLE.preferredWidth = 24f;
            iLE.preferredHeight = 24f;
        }

        var txtGO = new GameObject("AmountText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(wrapper.transform, false);
        var tmp = txtGO.GetComponent<TextMeshProUGUI>();
        tmp.text = amount.ToString();
        tmp.fontSize = 15f;
        tmp.color = valueColor;
    }

    private void CreateValueWithIcon(Transform parent, float value, Sprite icon, float width)
    {
        var wrapper = new GameObject("ValueWrapper", typeof(RectTransform));
        wrapper.transform.SetParent(parent, false);
        var le = wrapper.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;

        var hl = wrapper.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;

        var txtGO = new GameObject("ValueText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(wrapper.transform, false);
        var tmp = txtGO.GetComponent<TextMeshProUGUI>();
        tmp.text = value.ToString("F2").Replace(".00", "");
        tmp.fontSize = 15f;
        tmp.color = valueColor;

        if (icon != null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGO.transform.SetParent(wrapper.transform, false);
            var iImg = iconGO.GetComponent<Image>();
            iImg.sprite = icon;
            iImg.preserveAspect = true;
            var iLE = iconGO.AddComponent<LayoutElement>();
            iLE.preferredWidth = 20f;
            iLE.preferredHeight = 20f;
        }
    }

    private string GetDisplayName(string resourceId)
    {
        switch (resourceId)
        {
            case "holz": return "Holz";
            case "stein": return "Stein";
            case "eisen": return "Eisen";
            case "weizen": return "Weizen";
            case "fruechte": return "Früchte";
            case "wüstenfrucht": return "Wüstenfrucht";
            case "fleisch": return "Fleisch";
            default: return resourceId;
        }
    }

    private void OnCreateOfferClicked()
    {
        if (TradingManager.Instance == null) return;

        if (int.TryParse(createAmountInput.text, out int amount) && 
            int.TryParse(createPriceInput.text, out int price))
        {
            if (amount > 0 && price > 0)
            {
                bool success = TradingManager.Instance.CreateOffer(createSelectedResource, amount, price);
                if (success)
                {
                    createAmountInput.text = "";
                    createPriceInput.text = "";
                }
                else
                {
                    Debug.Log("Failed to create offer (not enough resources?)");
                }
            }
        }
    }
}
