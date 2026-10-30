using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NotificationManager : MonoBehaviour
{
    private sealed class NotificationEntry
    {
        public string key;
        public string message;
    }

    public static NotificationManager Instance { get; private set; }

    private readonly Queue<NotificationEntry> queue = new Queue<NotificationEntry>();
    private readonly Dictionary<string, float> cooldowns = new Dictionary<string, float>();

    private RectTransform panel;
    private VerticalLayoutGroup layout;
    private bool isShowing;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        EnsureUi();
    }

    public void Notify(string key, string message, float cooldownSeconds = 8f)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (!string.IsNullOrEmpty(key) && cooldowns.TryGetValue(key, out float nextAllowed) && Time.time < nextAllowed)
        {
            return;
        }

        if (!string.IsNullOrEmpty(key))
        {
            cooldowns[key] = Time.time + cooldownSeconds;
        }

        queue.Enqueue(new NotificationEntry { key = key, message = message });
        if (!isShowing)
        {
            StartCoroutine(ShowQueueRoutine());
        }
    }

    private void EnsureUi()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        if (panel != null)
        {
            return;
        }

        GameObject panelGO = new GameObject("NotifierPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGO.transform.SetParent(canvas.transform, false);

        Image bg = panelGO.GetComponent<Image>();
        bg.color = new Color(0, 0, 0, 0); // Transparent - kein Hintergrund

        panel = panelGO.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0f, 0.5f);
        panel.anchorMax = new Vector2(0f, 0.5f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(18f, 0f);
        panel.sizeDelta = new Vector2(280f, 160f);

        layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
    }

    private IEnumerator ShowQueueRoutine()
    {
        isShowing = true;
        while (queue.Count > 0)
        {
            NotificationEntry entry = queue.Dequeue();
            SpawnNotification(entry.message);
            AudioManager.Instance?.PlayNotificationSound();
            yield return new WaitForSeconds(0.75f);
        }

        isShowing = false;
    }

    private void SpawnNotification(string message)
    {
        EnsureUi();
        if (panel == null)
        {
            return;
        }

        GameObject itemGO = new GameObject("Notification", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        itemGO.transform.SetParent(panel, false);

        Image itemBg = itemGO.GetComponent<Image>();
        itemBg.color = GameUITheme.PanelBackground;

        Outline itemOutline = itemGO.AddComponent<Outline>();
        itemOutline.effectColor = GameUITheme.OutlineGoldStrong;
        itemOutline.effectDistance = new Vector2(1f, -1f);

        LayoutElement le = itemGO.AddComponent<LayoutElement>();
        le.minHeight = 54f;

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(itemGO.transform, false);

        RectTransform textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(12f, 8f);
        textRT.offsetMax = new Vector2(-12f, -8f);

        TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = 18f;
        tmp.color = GameUITheme.LabelParchment;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;

        StartCoroutine(FadeAndDestroyRoutine(itemGO, itemBg, itemOutline, tmp));
    }

    private IEnumerator FadeAndDestroyRoutine(GameObject itemGO, Image itemBg, Outline itemOutline, TextMeshProUGUI tmp)
    {
        yield return new WaitForSeconds(5f);

        float duration = 0.75f;
        float elapsed = 0f;
        Color startBg = itemBg.color;
        Color startOutline = itemOutline.effectColor;
        Color startText = tmp.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            itemBg.color = Color.Lerp(startBg, new Color(startBg.r, startBg.g, startBg.b, 0f), t);
            itemOutline.effectColor = Color.Lerp(startOutline, new Color(startOutline.r, startOutline.g, startOutline.b, 0f), t);
            tmp.color = Color.Lerp(startText, new Color(startText.r, startText.g, startText.b, 0f), t);
            yield return null;
        }

        Destroy(itemGO);
    }
}
