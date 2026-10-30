using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Handles the animated log system in the Lobby.
/// Scene: LobbyScene
/// </summary>
public class JoinLogUI : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private GameObject logTextPrefab;
    [SerializeField] private Transform logContainer;
    [SerializeField] private int maxMessages = 5;
    [SerializeField] private float messageLifetime = 4f;
    [SerializeField] private float slideDuration = 0.3f;

    private Queue<GameObject> activeMessages = new Queue<GameObject>();

    /// <summary>
    /// Called externally to display a new join message.
    /// </summary>
    /// <param name="playerName">The name of the joining player.</param>
    public void LogPlayerJoin(string playerName)
    {
        if (logTextPrefab == null || logContainer == null) return;

        // Instantiate message
        GameObject newMessageObj = Instantiate(logTextPrefab, logContainer);
        TextMeshProUGUI tmpText = newMessageObj.GetComponent<TextMeshProUGUI>();

        if (tmpText != null)
        {
            tmpText.text = $"<color={GameUITheme.HtmlGood}>[+]</color> {playerName} joined the room";
        }

        // Manage queue limits
        activeMessages.Enqueue(newMessageObj);
        if (activeMessages.Count > maxMessages)
        {
            GameObject oldestMessage = activeMessages.Dequeue();
            Destroy(oldestMessage);
        }

        // Animate the text in and schedule fade out
        StartCoroutine(AnimateMessageIn(tmpText));
        StartCoroutine(FadeOutAndDestroy(newMessageObj, tmpText, messageLifetime));
    }

    private IEnumerator AnimateMessageIn(TextMeshProUGUI tmpText)
    {
        if (tmpText == null) yield break;

        RectTransform rect = tmpText.rectTransform;

        // Start position (shifted to the left)
        Vector2 originalAnchoredPos = rect.anchoredPosition;
        rect.anchoredPosition = new Vector2(-200f, originalAnchoredPos.y);

        // Ensure full alpha
        Color c = tmpText.color;
        c.a = 1f;
        tmpText.color = c;

        float time = 0f;
        while (time < slideDuration)
        {
            if (tmpText == null) yield break;

            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / slideDuration);
            // Simple Ease-Out
            t = 1f - Mathf.Pow(1f - t, 3f);

            rect.anchoredPosition = Vector2.Lerp(new Vector2(-200f, originalAnchoredPos.y), originalAnchoredPos, t);
            yield return null;
        }

        if (tmpText != null) rect.anchoredPosition = originalAnchoredPos;
    }

    private IEnumerator FadeOutAndDestroy(GameObject messageObj, TextMeshProUGUI tmpText, float delay)
    {
        yield return new WaitForSeconds(delay);

        float fadeDuration = 1f;
        float time = 0f;

        while (time < fadeDuration)
        {
            if (tmpText == null) break;

            time += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, time / fadeDuration);

            Color c = tmpText.color;
            c.a = alpha;
            tmpText.color = c;

            yield return null;
        }

        if (messageObj != null)
        {
            // Remove from queue if it hasn't been forcefully removed already
            if (activeMessages.Contains(messageObj))
            {
                // Reconstruct queue without this item
                Queue<GameObject> newQueue = new Queue<GameObject>();
                foreach (var item in activeMessages)
                {
                    if (item != messageObj) newQueue.Enqueue(item);
                }
                activeMessages = newQueue;
            }
            Destroy(messageObj);
        }
    }
}
