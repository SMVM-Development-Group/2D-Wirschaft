using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;

public class DayNightManager : MonoBehaviour
{
    public static DayNightManager Instance;

    [Header("Day-Night Cycle Settings")]
    public float cycleDuration = 90f; // 90 seconds for a full 24h day-night cycle
    public float currentHour = 8f;   // Start at 8:00 AM
    public int currentDay = 1;

    [Header("Night Overlay Color")]
    public Color nightColor = new Color(0.04f, 0.04f, 0.15f, 0.45f); // Indigo blue night color

    private Image nightOverlay;
    private TextMeshProUGUI txtClockDay;
    private TextMeshProUGUI txtClockTime;
    private GameObject clockPanel;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        ApplyLobbySettings();
        CreateNightOverlay();
        CreateClockUI();
    }

    private void ApplyLobbySettings()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbySettingsKeys.DayLength, out object durationObj))
        {
            cycleDuration = System.Convert.ToSingle(durationObj);
        }
    }

    private void Update()
    {
        // Advance time
        currentHour += (Time.deltaTime / cycleDuration) * 24f;
        if (currentHour >= 24f)
        {
            currentHour -= 24f;
            currentDay++;
        }

        // Apply night overlay alpha
        if (nightOverlay != null)
        {
            float targetAlpha = GetNightAlpha();
            Color col = nightColor;
            col.a = targetAlpha;
            nightOverlay.color = col;
        }

        // Update Clock UI
        UpdateClockText();
    }

    private float GetNightAlpha()
    {
        // 6:00 to 8:00 - Sunrise (Fade out)
        if (currentHour >= 6f && currentHour < 8f)
        {
            return Mathf.Lerp(nightColor.a, 0f, (currentHour - 6f) / 2f);
        }
        // 8:00 to 17:00 - Day (Clear)
        else if (currentHour >= 8f && currentHour < 17f)
        {
            return 0f;
        }
        // 17:00 to 19:00 - Sunset (Fade in)
        else if (currentHour >= 17f && currentHour < 19f)
        {
            return Mathf.Lerp(0f, nightColor.a, (currentHour - 17f) / 2f);
        }
        // 19:00 to 6:00 - Night (Dark)
        else
        {
            return nightColor.a;
        }
    }

    private void CreateNightOverlay()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        // Create overlay GameObject
        GameObject overlayGO = new GameObject("NightOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlayGO.transform.SetParent(canvas.transform, false);
        overlayGO.transform.SetAsFirstSibling(); // Put behind other UI elements

        RectTransform rect = overlayGO.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        nightOverlay = overlayGO.GetComponent<Image>();
        nightOverlay.color = new Color(0, 0, 0, 0);
        nightOverlay.raycastTarget = false; // MUST be false so players can click through it!
    }

    private void CreateClockUI()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        // Create medieval styled dark box in top right
        clockPanel = new GameObject("ClockPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        clockPanel.transform.SetParent(canvas.transform, false);

        RectTransform panelRT = clockPanel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1f, 1f);
        panelRT.anchorMax = new Vector2(1f, 1f);
        panelRT.pivot = new Vector2(1f, 1f);
        panelRT.sizeDelta = new Vector2(160f, 65f);
        panelRT.anchoredPosition = new Vector2(-20f, -80f); // Positioned nicely below resource bar

        // Setup background styling
        Image bgImg = clockPanel.GetComponent<Image>();
        bgImg.color = GameUITheme.PanelBackground; // Sleek dark brown matching other UI
        
        var outline = clockPanel.AddComponent<Outline>();
        outline.effectColor = GameUITheme.AccentGold; // Gold outline
        outline.effectDistance = new Vector2(2f, -2f);

        // Text: Day
        GameObject dayTextGO = new GameObject("DayText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        dayTextGO.transform.SetParent(clockPanel.transform, false);
        txtClockDay = dayTextGO.GetComponent<TextMeshProUGUI>();
        txtClockDay.fontSize = 13f;
        txtClockDay.alignment = TextAlignmentOptions.Center;
        txtClockDay.color = GameUITheme.LabelParchment; // Gold-parchment text color
        txtClockDay.fontStyle = FontStyles.Bold;

        RectTransform dayRT = dayTextGO.GetComponent<RectTransform>();
        dayRT.anchorMin = new Vector2(0f, 0.6f);
        dayRT.anchorMax = new Vector2(1f, 1f);
        dayRT.sizeDelta = Vector2.zero;

        // Text: Time
        GameObject timeTextGO = new GameObject("TimeText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        timeTextGO.transform.SetParent(clockPanel.transform, false);
        txtClockTime = timeTextGO.GetComponent<TextMeshProUGUI>();
        txtClockTime.fontSize = 18f;
        txtClockTime.alignment = TextAlignmentOptions.Center;
        txtClockTime.color = GameUITheme.LabelPrimary; // Bright ivory text color
        txtClockTime.fontStyle = FontStyles.Bold;

        RectTransform timeRT = timeTextGO.GetComponent<RectTransform>();
        timeRT.anchorMin = new Vector2(0f, 0f);
        timeRT.anchorMax = new Vector2(1f, 0.6f);
        timeRT.sizeDelta = Vector2.zero;
    }

    private void UpdateClockText()
    {
        if (txtClockDay == null || txtClockTime == null) return;

        int hour = Mathf.FloorToInt(currentHour);
        int minute = Mathf.FloorToInt((currentHour - hour) * 60f);

        txtClockDay.text = $"TAG {currentDay}";

        string timePeriod = "";
        if (currentHour >= 6f && currentHour < 10f) timePeriod = "Morgen";
        else if (currentHour >= 10f && currentHour < 14f) timePeriod = "Mittag";
        else if (currentHour >= 14f && currentHour < 18f) timePeriod = "Nachmittag";
        else if (currentHour >= 18f && currentHour < 22f) timePeriod = "Abend";
        else timePeriod = "Nacht";

        txtClockTime.text = $"{hour:00}:{minute:00} <size=11>({timePeriod})</size>";
    }
}
