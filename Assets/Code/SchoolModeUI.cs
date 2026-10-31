using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Menü oben rechts mit umschaltbarem "Schulmodus".
///
/// Schulmodus:
///   • Maus 1 Sekunde nicht bewegt  -> grauer Balken erscheint über dem Zeiger
///   • Balken füllt sich in 3 Sekunden
///   • ist er voll, wird ein Linksklick ausgelöst
///
/// Wird automatisch von Player_UI.BuildUI() am HUD-Canvas installiert.
/// </summary>
public class SchoolModeUI : MonoBehaviour
{
    private const string EnabledPrefKey = "SchoolMode.Enabled";

    [Header("Schulmodus")]
    [Tooltip("Zeit, die die Maus still stehen muss, bevor der Balken erscheint.")]
    [SerializeField] private float idleDelay = 1f;
    [Tooltip("Zeit, bis der Balken voll ist und der Linksklick ausgelöst wird.")]
    [SerializeField] private float fillDuration = 3f;
    [Tooltip("Wenn aktiv, wiederholt sich der Klick, solange die Maus still steht.")]
    [SerializeField] private bool repeatWhileIdle = true;

    [Header("Balken-Optik")]
    [SerializeField] private float barWidth = 80f;
    [SerializeField] private float barHeight = 10f;
    [SerializeField] private float barOffsetY = 26f;
    [SerializeField] private Color barBackgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.9f);
    [SerializeField] private Color barFillColor = new Color(0.70f, 0.70f, 0.70f, 1f);

    private RectTransform canvasRect;
    private GameObject dropdown;
    private GameObject blocker;
    private GameObject cursorBar;
    private RectTransform cursorBarRect;
    private RectTransform fillRect;
    private Image toggleStateImage;
    private TextMeshProUGUI toggleStateLabel;

    private bool built;
    private bool schoolModeEnabled;
    private bool armed = true;
    private bool isClicking;
    private float idleTime;
    private Vector2 lastMousePosition;
    private Coroutine clickRoutine;

    // ── Installation ─────────────────────────────────────────────────────────

    /// <summary>Erzeugt das Menü einmalig am übergebenen HUD-Canvas.</summary>
    public static SchoolModeUI Install(Transform canvasTransform)
    {
        if (canvasTransform == null) return null;

        SchoolModeUI ui = canvasTransform.GetComponent<SchoolModeUI>();
        if (ui == null)
        {
            ui = canvasTransform.gameObject.AddComponent<SchoolModeUI>();
        }

        ui.BuildUI(canvasTransform);
        return ui;
    }

    // ── UI-Aufbau ────────────────────────────────────────────────────────────

    private void BuildUI(Transform canvasTransform)
    {
        if (built || canvasTransform == null) return;
        built = true;

        canvasRect = canvasTransform as RectTransform;

        schoolModeEnabled = PlayerPrefs.GetInt(EnabledPrefKey, 0) == 1;
        lastMousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        BuildBlocker(canvasTransform);
        BuildMenu(canvasTransform);
        BuildCursorBar(canvasTransform);

        RefreshToggleVisual();
        HideCursorBar();
    }

    private void BuildBlocker(Transform canvasTransform)
    {
        blocker = new GameObject("SchoolMode_Blocker",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        blocker.transform.SetParent(canvasTransform, false);

        RectTransform rt = blocker.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = blocker.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f); // unsichtbar, blockt aber Klicks
        img.raycastTarget = true;

        Button btn = blocker.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(CloseDropdown);

        blocker.SetActive(false);
    }

    private void BuildMenu(Transform canvasTransform)
    {
        // ── Menü-Button oben rechts (links neben der Uhr) ────────────────────
        GameObject menuRoot = new GameObject("SchoolMode_Menu",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        menuRoot.transform.SetParent(canvasTransform, false);

        RectTransform menuRT = menuRoot.GetComponent<RectTransform>();
        menuRT.anchorMin = new Vector2(1f, 1f);
        menuRT.anchorMax = new Vector2(1f, 1f);
        menuRT.pivot = new Vector2(1f, 1f);
        menuRT.anchoredPosition = new Vector2(-190f, -16f);
        menuRT.sizeDelta = new Vector2(150f, 46f);

        Image menuImg = menuRoot.GetComponent<Image>();
        menuImg.color = GameUITheme.SlotWood;

        Outline menuOutline = menuRoot.AddComponent<Outline>();
        menuOutline.effectColor = GameUITheme.AccentGold;
        menuOutline.effectDistance = new Vector2(2f, -2f);

        Button menuButton = menuRoot.GetComponent<Button>();
        menuButton.targetGraphic = menuImg;
        ApplyButtonColors(menuButton);
        menuButton.onClick.AddListener(() =>
        {
            AudioManager.Instance?.PlaySelectSound();
            ToggleDropdown();
        });

        TextMeshProUGUI menuLabel = CreateText(menuRoot.transform, "Label", "Menü", 20f,
            GameUITheme.LabelPrimary, TextAlignmentOptions.Center);
        Stretch(menuLabel.rectTransform, 6f, 4f);

        // ── Dropdown ────────────────────────────────────────────────────────
        dropdown = new GameObject("SchoolMode_Dropdown",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dropdown.transform.SetParent(menuRoot.transform, false);

        RectTransform dropRT = dropdown.GetComponent<RectTransform>();
        dropRT.anchorMin = new Vector2(1f, 1f);
        dropRT.anchorMax = new Vector2(1f, 1f);
        dropRT.pivot = new Vector2(1f, 1f);
        dropRT.anchoredPosition = new Vector2(0f, -50f);
        dropRT.sizeDelta = new Vector2(300f, 150f);

        Image dropImg = dropdown.GetComponent<Image>();
        dropImg.color = GameUITheme.PanelBackground;

        Outline dropOutline = dropdown.AddComponent<Outline>();
        dropOutline.effectColor = GameUITheme.AccentGold;
        dropOutline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI title = CreateText(dropdown.transform, "Title", "Einstellungen", 22f,
            GameUITheme.LabelParchment, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        RectTransform titleRT = title.rectTransform;
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -12f);
        titleRT.sizeDelta = new Vector2(-32f, 30f);

        GameObject divider = new GameObject("Divider",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        divider.transform.SetParent(dropdown.transform, false);
        Image dividerImg = divider.GetComponent<Image>();
        dividerImg.color = GameUITheme.OutlineGold;
        dividerImg.raycastTarget = false;
        RectTransform dividerRT = divider.GetComponent<RectTransform>();
        dividerRT.anchorMin = new Vector2(0f, 1f);
        dividerRT.anchorMax = new Vector2(1f, 1f);
        dividerRT.pivot = new Vector2(0.5f, 1f);
        dividerRT.anchoredPosition = new Vector2(0f, -46f);
        dividerRT.sizeDelta = new Vector2(-32f, 1f);

        TextMeshProUGUI modeLabel = CreateText(dropdown.transform, "SchoolModeLabel", "Schulmodus", 19f,
            GameUITheme.LabelPrimary, TextAlignmentOptions.Left);
        modeLabel.fontStyle = FontStyles.Bold;
        RectTransform modeRT = modeLabel.rectTransform;
        modeRT.anchorMin = new Vector2(0f, 0.5f);
        modeRT.anchorMax = new Vector2(1f, 0.5f);
        modeRT.pivot = new Vector2(0f, 0.5f);
        modeRT.anchoredPosition = new Vector2(16f, 8f);
        modeRT.sizeDelta = new Vector2(-130f, 30f);

        // ── AN/AUS-Button ───────────────────────────────────────────────────
        GameObject toggleGO = new GameObject("SchoolModeToggle",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        toggleGO.transform.SetParent(dropdown.transform, false);

        RectTransform toggleRT = toggleGO.GetComponent<RectTransform>();
        toggleRT.anchorMin = new Vector2(1f, 0.5f);
        toggleRT.anchorMax = new Vector2(1f, 0.5f);
        toggleRT.pivot = new Vector2(1f, 0.5f);
        toggleRT.anchoredPosition = new Vector2(-16f, 8f);
        toggleRT.sizeDelta = new Vector2(82f, 36f);

        toggleStateImage = toggleGO.GetComponent<Image>();
        toggleStateImage.color = GameUITheme.ButtonNormal;

        Outline toggleOutline = toggleGO.AddComponent<Outline>();
        toggleOutline.effectColor = GameUITheme.AccentGold;
        toggleOutline.effectDistance = new Vector2(1.5f, -1.5f);

        Button toggleButton = toggleGO.GetComponent<Button>();
        toggleButton.targetGraphic = toggleStateImage;
        ApplyButtonColors(toggleButton);
        toggleButton.onClick.AddListener(() =>
        {
            AudioManager.Instance?.PlaySelectSound();
            SetSchoolMode(!schoolModeEnabled);
        });

        toggleStateLabel = CreateText(toggleGO.transform, "Label", "AUS", 17f,
            GameUITheme.LabelPrimary, TextAlignmentOptions.Center);
        toggleStateLabel.fontStyle = FontStyles.Bold;
        Stretch(toggleStateLabel.rectTransform, 4f, 2f);

        TextMeshProUGUI hint = CreateText(dropdown.transform, "Hint",
            "Klickt automatisch bei Mausstillstand.", 12f,
            GameUITheme.LabelSecondary, TextAlignmentOptions.Left);
        RectTransform hintRT = hint.rectTransform;
        hintRT.anchorMin = new Vector2(0f, 0f);
        hintRT.anchorMax = new Vector2(1f, 0f);
        hintRT.pivot = new Vector2(0.5f, 0f);
        hintRT.anchoredPosition = new Vector2(0f, 10f);
        hintRT.sizeDelta = new Vector2(-32f, 24f);

        dropdown.SetActive(false);
    }

    private void BuildCursorBar(Transform canvasTransform)
    {
        cursorBar = new GameObject("SchoolMode_ProgressBar",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        cursorBar.transform.SetParent(canvasTransform, false);

        cursorBarRect = cursorBar.GetComponent<RectTransform>();
        // Anker in der Canvas-Mitte; positioniert wird über localPosition.
        cursorBarRect.anchorMin = new Vector2(0.5f, 0.5f);
        cursorBarRect.anchorMax = new Vector2(0.5f, 0.5f);
        cursorBarRect.pivot = new Vector2(0.5f, 0f);
        cursorBarRect.sizeDelta = new Vector2(barWidth, barHeight);

        Image bg = cursorBar.GetComponent<Image>();
        bg.color = barBackgroundColor;
        bg.raycastTarget = false;

        Outline outline = cursorBar.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
        outline.effectDistance = new Vector2(1f, -1f);

        GameObject fillGO = new GameObject("Fill",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGO.transform.SetParent(cursorBar.transform, false);

        fillRect = fillGO.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = new Vector2(2f, 0f);
        fillRect.sizeDelta = new Vector2(0f, -4f);

        Image fillImg = fillGO.GetComponent<Image>();
        fillImg.color = barFillColor;
        fillImg.raycastTarget = false;

        cursorBar.SetActive(false);
    }

    // ── Menü-Logik ───────────────────────────────────────────────────────────

    private void ToggleDropdown()
    {
        if (dropdown == null) return;

        bool open = !dropdown.activeSelf;
        dropdown.SetActive(open);
        if (blocker != null) blocker.SetActive(open);

        ResetCycle();
        armed = true;
    }

    private void CloseDropdown()
    {
        if (dropdown != null) dropdown.SetActive(false);
        if (blocker != null) blocker.SetActive(false);
    }

    private void SetSchoolMode(bool enabled)
    {
        schoolModeEnabled = enabled;

        PlayerPrefs.SetInt(EnabledPrefKey, enabled ? 1 : 0);
        PlayerPrefs.Save();

        if (Mouse.current != null)
        {
            lastMousePosition = Mouse.current.position.ReadValue();
        }

        ResetCycle();
        armed = true;
        RefreshToggleVisual();
    }

    private void RefreshToggleVisual()
    {
        if (toggleStateImage != null)
        {
            toggleStateImage.color = schoolModeEnabled
                ? GameUITheme.ButtonConfirm
                : GameUITheme.ButtonNormal;
        }

        if (toggleStateLabel != null)
        {
            toggleStateLabel.text = schoolModeEnabled ? "AN" : "AUS";
        }
    }

    // ── Schulmodus-Logik ─────────────────────────────────────────────────────

    private void Update()
    {
        if (!built || !schoolModeEnabled)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            ResetCycle();
            return;
        }

        Vector2 mousePosition = mouse.position.ReadValue();

        // Maus bewegt -> Füllstand abbrechen und wieder scharf schalten.
        if ((mousePosition - lastMousePosition).sqrMagnitude > 0.25f)
        {
            lastMousePosition = mousePosition;
            ResetCycle();
            armed = true;
            return;
        }

        if (!armed || isClicking)
        {
            return;
        }

        idleTime += Time.unscaledDeltaTime;

        if (idleTime < idleDelay)
        {
            HideCursorBar();
            return;
        }

        float progress = Mathf.Clamp01((idleTime - idleDelay) / Mathf.Max(0.01f, fillDuration));
        ShowCursorBar(mousePosition, progress);

        if (progress < 1f)
        {
            return;
        }

        // Balken voll -> Klick auslösen.
        ResetCycle();
        if (!repeatWhileIdle)
        {
            armed = false;
        }

        PerformLeftClick();
    }

    private void ResetCycle()
    {
        idleTime = 0f;
        HideCursorBar();
    }

    private void ShowCursorBar(Vector2 screenPosition, float progress)
    {
        if (cursorBar == null) return;

        cursorBar.SetActive(true);

        if (canvasRect != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPosition, null, out Vector2 local))
        {
            Rect r = canvasRect.rect;
            float halfWidth = barWidth * 0.5f;

            local.x = Mathf.Clamp(local.x, r.xMin + halfWidth + 4f, r.xMax - halfWidth - 4f);
            local.y = Mathf.Clamp(local.y, r.yMin + 2f, r.yMax - barOffsetY - barHeight - 4f);

            // localPosition ist relativ zum Pivot des Canvas und passt damit
            // unabhängig von dessen Pivot direkt zum Mauspunkt.
            cursorBarRect.localPosition = new Vector3(local.x, local.y + barOffsetY, 0f);
        }

        float fillWidth = Mathf.Max(0f, barWidth - 4f) * Mathf.Clamp01(progress);
        fillRect.sizeDelta = new Vector2(fillWidth, -4f);
    }

    private void HideCursorBar()
    {
        if (cursorBar != null) cursorBar.SetActive(false);
        if (fillRect != null) fillRect.sizeDelta = new Vector2(0f, -4f);
    }

    private void PerformLeftClick()
    {
        if (isClicking || clickRoutine != null) return;
        clickRoutine = StartCoroutine(SimulateLeftClick());
    }

    /// <summary>
    /// Simuliert einen echten Linksklick über das Input System
    /// (Press in einem Frame, Release im nächsten).
    /// </summary>
    private IEnumerator SimulateLeftClick()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            clickRoutine = null;
            yield break;
        }

        isClicking = true;

        using (StateEvent.From(mouse, out var pressEvent))
        {
            mouse.leftButton.WriteValueIntoEvent(1f, pressEvent);
            InputSystem.QueueEvent(pressEvent);
        }

        yield return null;

        using (StateEvent.From(mouse, out var releaseEvent))
        {
            mouse.leftButton.WriteValueIntoEvent(0f, releaseEvent);
            InputSystem.QueueEvent(releaseEvent);
        }

        yield return null;

        isClicking = false;
        clickRoutine = null;
    }

    // ── UI-Helfer ────────────────────────────────────────────────────────────

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text,
        float fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static void Stretch(RectTransform rt, float horizontalPadding, float verticalPadding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(horizontalPadding, verticalPadding);
        rt.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
    }

    private static void ApplyButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.selectedColor = Color.white;
        button.colors = colors;
    }
}
