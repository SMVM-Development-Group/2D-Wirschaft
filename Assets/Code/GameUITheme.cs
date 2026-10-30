using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Zentrale UI-Palette für ALLE Menü-/HUD-Elemente.
/// Ziel: Einheitliche Optik — Farben/Schriften/Abstände werden hier geändert
/// und wirken projektweit, statt in jeder UI-Datei eigene Farbwerte zu haben.
///
/// Bonus: Wer z.B. die Panel-Form dunkler will, ändert NUR diese Datei.
/// </summary>
public static class GameUITheme
{
    // ── Panels ───────────────────────────────────────────────────────────
    /// <summary>Haupt-Panelhintergrund (dunkles Eichenbraun, fast deckend)</summary>
    public static readonly Color PanelBackground       = new Color(0.12f, 0.08f, 0.04f, 0.95f);
    /// <summary>Sekundäres Panel (etwas transparenter, z.B. trading/ca-Bereiche)</summary>
    public static readonly Color PanelBackgroundSoft   = new Color(0.12f, 0.08f, 0.04f, 0.85f);
    /// <summary>Dunkles Eichenbraun (Ressourcenbars)</summary>
    public static readonly Color BarBackground         = new Color(0.18f, 0.10f, 0.03f, 0.96f);
    /// <summary>Warme Holz-Braun (Slots/Buttons)</summary>
    public static readonly Color SlotWood              = new Color(0.42f, 0.27f, 0.09f, 0.92f);

    // ── Text ─────────────────────────────────────────────────────────────
    /// <summary>Pergament-Beige (Labels, Titel)</summary>
    public static readonly Color LabelParchment        = new Color(0.90f, 0.78f, 0.52f, 0.85f);
    /// <summary>Sehr heller Text (Werte, Titelzeilen)</summary>
    public static readonly Color LabelPrimary          = new Color(0.96f, 0.96f, 0.96f, 1f);
    /// <summary>Grauer Sekundärtext (Beschreibungen)</summary>
    public static readonly Color LabelSecondary        = new Color(0.78f, 0.78f, 0.80f, 0.9f);
    /// <summary>Dezent dunkler Placeholder</summary>
    public static readonly Color LabelPlaceholder      = new Color(0.60f, 0.50f, 0.40f, 0.85f);

    // ── Rahmen / Akzent ─────────────────────────────────────────────────
    /// <summary>Gold-Akzent der Outlines</summary>
    public static readonly Color OutlineGold           = new Color(0.72f, 0.52f, 0.18f, 0.35f);
    /// <summary>Starker Gold-Akzent (Notification, aktive Auswahl)</summary>
    public static readonly Color OutlineGoldStrong     = new Color(0.95f, 0.86f, 0.56f, 0.65f);
    /// <summary>Parchment-Gold wie bei Menü-Titeln</summary>
    public static readonly Color AccentGold            = new Color(0.76f, 0.70f, 0.45f, 0.9f);

    // ── Buttons ──────────────────────────────────────────────────────────
    public static readonly Color ButtonNormal          = new Color(0.14f, 0.17f, 0.22f, 0.95f);
    public static readonly Color ButtonChatIcon        = new Color(0.16f, 0.20f, 0.26f, 0.95f);
    public static readonly Color ButtonPressed         = new Color(0.24f, 0.20f, 0.16f, 0.85f);
    public static readonly Color InputBackground       = new Color(0.12f, 0.14f, 0.18f, 0.95f);
    public static readonly Color InputOutline          = new Color(0.50f, 0.50f, 0.55f, 0.7f);

    // ── Statusfarben ─────────────────────────────────────────────────────
    public static readonly Color StatusGood            = new Color(0.35f, 1f, 0.45f, 1f);   // alles > 0
    public static readonly Color StatusBad             = new Color(1f, 0.35f, 0.35f, 1f);   // Defizit / Fehler
    public static readonly Color StatusWarning         = new Color(1f, 0.65f, 0.25f, 1f);   // knapp / Warnung
    public static readonly Color StatusNeutral         = new Color(0.66f, 0.66f, 0.66f, 1f);// 0 / neutral

    // ── Feind / Spieler ─────────────────────────────────────────────────
    public static readonly Color EnemyTint             = new Color(1f, 0.50f, 0.50f, 1f);   // roter Feind-Tint auf Gebäuden/Schiffen

    // ── Abstände (Einheitensystem) ───────────────────────────────────────
    /// <summary>Basis-Raster: 1 Einheit = 8 px (UI-Einzelschritt)</summary>
    public const float SpacingUnit = 8f;
    public  static float Spacing1 => SpacingUnit;        //  8 px
    public  static float Spacing2 => SpacingUnit * 2f;   // 16 px
    public  static float Spacing3 => SpacingUnit * 3f;   // 24 px

    // ── Backdrop / Off-Zustände ──────────────────────────────────────────
    /// <summary>Vollflächiger Menü-/Screen-Hintergrund (kein Panel)</summary>
    public static readonly Color ScreenBackdrop     = new Color(0.07f, 0.09f, 0.11f, 1f);
    /// <summary>Abdunkel-Overlay hinterhalb von modalen Fenstern / Vollbild-UIs</summary>
    public static readonly Color OverlayVeil        = new Color(0f, 0f, 0f, 0.5f);
    /// <summary>Schwarzer Schatten-Rahmen (Panel-Schatten, Progress-Ring)</summary>
    public static readonly Color ShadowOutline      = new Color(0f, 0f, 0f, 0.55f);

    // ── Button-Familie ───────────────────────────────────────────────────
    /// <summary>Destruktiver Button (Löschen, Verlassen, Abriss)</summary>
    public static readonly Color ButtonDanger       = new Color(0.40f, 0.17f, 0.12f, 1f);
    /// <summary>Bestätigender Button (Start, Anwenden, Rekrutieren)</summary>
    public static readonly Color ButtonConfirm      = new Color(0.20f, 0.36f, 0.17f, 1f);
    /// <summary>Warnend markierter Button (Bauhütte, schnelles Handeln)</summary>
    public static readonly Color ButtonWarning      = new Color(0.55f, 0.40f, 0.06f, 1f);

    // ── Zustands-Extras ──────────────────────────────────────────────────
    /// <summary>Aktive Auswahl (Tabs, Radio-Buttons, Einträge)</summary>
    public static readonly Color SelectionGold      = new Color(0.58f, 0.40f, 0.18f, 1f);

    // ── Helpers ─────────────────────────────────────────────────────────
    /// <summary>Farbe je Riot: negativ = rot, neutral = grau, positiv = grün.</summary>
    public static Color AmountToColor(float net)
    {
        if (net > 0.01f) return StatusGood;
        if (net < -0.01f) return StatusBad;
        return StatusNeutral;
    }

    // ── TMP-Markup-Farben (HTML-HEX) ─────────────────────────────────────
    /// <summary>HTML-HEX (#RRGGBB) einer Farbe — für TMP-Markup <color=#...>.</summary>
    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    /// <summary>Statusfarben als TMP-Markup-HEX (konsistent zu StatusGood/StatusBad/StatusNeutral).</summary>
    public static string HtmlGood    => Hex(StatusGood);
    public static string HtmlBad     => Hex(StatusBad);
    public static string HtmlNeutral => Hex(StatusNeutral);
    public static string HtmlWarning => Hex(StatusWarning);

    /// <summary>Wendet das Standard-Panel-Erscheinungsbild auf ein Image an.</summary>
    public static void ApplyPanel(Image image, bool soft = false)
    {
        if (image == null) return;
        image.color = soft ? PanelBackgroundSoft : PanelBackground;
    }

    /// <summary>Wendet den Standard-Gold-Rahmen auf ein Outline an.</summary>
    public static void ApplyOutline(Outline outline, bool strong = false)
    {
        if (outline == null) return;
        outline.effectColor = strong ? OutlineGoldStrong : OutlineGold;
    }
}
