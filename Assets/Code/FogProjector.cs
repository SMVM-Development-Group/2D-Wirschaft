using UnityEngine;
using System.Collections.Generic;

public class FogProjector : MonoBehaviour
{
    private static List<FogRevealer> revealers = new List<FogRevealer>();

    [Header("Fog Settings")]
    [SerializeField] private Material fogMaterial;
    [Tooltip("Aufloesung der Sicht-/Aufdeck-Maske. Hoeher = schaerferer Nebelrand beim Reinzoomen.")]
    [SerializeField] private int maskResolution = 2048;
    [Tooltip("Abgedeckte Weltgroesse. <= 0 = automatisch aus der Grid-Groesse berechnen.")]
    [SerializeField] private float mapSize = 0f;

    private RenderTexture maskTexture;
    private Camera maskCamera;
    private GameObject fogQuad;

    // Aufdeck-Maske: wird direkt (CPU) in eine Texture2D geschrieben.
    // Der frühere Kamera->RenderTexture-Weg (Layer-30-Mesh + zweite Kamera) hat
    // mit URP/RenderGraph nicht zuverlässig in die Textur gerendert — die Folge war,
    // dass aufgedeckte Gebiete im Spiel weiter wie unexplored aussahen.
    private static Texture2D exploredTexture;
    private static bool exploredDirty;
    private static int exploredResolution = 1024;
    private static float exploredWorldSize = 2000f;

    private static HashSet<Vector2> exploredCells = new HashSet<Vector2>();
    private static int explorationVersion = 0;
    public static int ExplorationVersion => explorationVersion;

    private const float MaskRenderInterval = 0.06f; // ~16 FPS reicht völlig für die Maske
    private float maskRenderTimer = 0f;
    private const float ExploredApplyInterval = 0.15f;
    private float exploredApplyTimer = 0f;

    public static bool IsExplored(Vector2 pos)
    {
        return exploredCells.Contains(new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y)));
    }

    /// <summary>
    /// Wie tief liegt die Position im Sichtfeld? > 0 = sichtbar, Wert = Abstand
    /// zum Rand des Sichtkreises (negativ = ausserhalb). Fuer Ghost-Aufraeumen:
    /// Geister direkt AM Sichtrand sollen stehen bleiben.
    /// </summary>
    public static float VisibilityDepth(Vector2 pos)
    {
        float best = float.NegativeInfinity;
        for (int i = revealers.Count - 1; i >= 0; i--)
        {
            FogRevealer r = revealers[i];
            if (r == null || !r.isLocalPlayer) continue;
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            float rad = r.radius * 1.25f;
            float depth = rad - Vector2.Distance(pos, r.transform.position);
            if (depth > best) best = depth;
        }
        return best;
    }

    /// <summary>
    /// Ist die Position JETZT direkt im Sichtfeld (im sichtbaren Kreis eines der
    /// lokalen Revealer)? Genau wie die Masken-Optik (Revealer ohne isLocalPlayer
    /// erzeugen keine Maske und zaehlen nicht).
    /// </summary>
    public static bool IsVisible(Vector2 pos)
    {
        return VisibilityDepth(pos) >= 0f;
    }

    public static void RegisterExploration(Vector2 center, float radius)
    {
        if (radius <= 0f) return;

        int r = Mathf.CeilToInt(radius);
        for (int x = -r; x <= r; x++)
        {
            for (int y = -r; y <= r; y++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    Vector2 cell = new Vector2(Mathf.Round(center.x + x), Mathf.Round(center.y + y));
                    if (exploredCells.Add(cell))
                    {
                        explorationVersion++;
                        PaintExploredCell(cell);
                    }
                }
            }
        }
    }

    public static void AddRevealer(FogRevealer r) => revealers.Add(r);
    public static void RemoveRevealer(FogRevealer r) => revealers.Remove(r);

    // ── Aufdeck-Textur (CPU) ────────────────────────────────────────────────

    /// <summary>Malt eine aufgedeckte Zelle weiss in die Aufdeck-Textur.</summary>
    private static void PaintExploredCell(Vector2 cell)
    {
        if (exploredTexture == null) return;

        float res = exploredResolution;
        float size = exploredWorldSize;

        // Weltkoordinate -> Pixel; die Zelle ist 1x1 Weltunits gross.
        int x0 = Mathf.FloorToInt(((cell.x - 0.5f) / size + 0.5f) * res);
        int x1 = Mathf.CeilToInt(((cell.x + 0.5f) / size + 0.5f) * res);
        int y0 = Mathf.FloorToInt(((cell.y - 0.5f) / size + 0.5f) * res);
        int y1 = Mathf.CeilToInt(((cell.y + 0.5f) / size + 0.5f) * res);

        // Mindestens 1 Pixel pro Zelle (sonst gehen kleine Zellen bei grober Textur verloren)
        if (x1 <= x0) x1 = x0 + 1;
        if (y1 <= y0) y1 = y0 + 1;

        x0 = Mathf.Clamp(x0, 0, exploredResolution - 1);
        x1 = Mathf.Clamp(x1, 0, exploredResolution);
        y0 = Mathf.Clamp(y0, 0, exploredResolution - 1);
        y1 = Mathf.Clamp(y1, 0, exploredResolution);

        for (int px = x0; px < x1; px++)
        {
            for (int py = y0; py < y1; py++)
            {
                exploredTexture.SetPixel(px, py, Color.white);
            }
        }
        exploredDirty = true;
    }

    /// <summary>Baut die komplette Aufdeck-Textur aus allen bekannten Zellen neu auf (z.B. nach Szenenwechsel).</summary>
    private static void RepaintAllExplored()
    {
        if (exploredTexture == null) return;

        int count = exploredResolution * exploredResolution;
        Color32[] black = new Color32[count]; // alle 0 (schwarz)
        exploredTexture.SetPixels32(black);

        foreach (Vector2 cell in exploredCells)
        {
            PaintExploredCell(cell);
        }

        exploredTexture.Apply(false);
        exploredDirty = false;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Start()
    {
        int gridSize = GridManager.Instance != null ? GridManager.Instance.GetGridSize() : 0;
        if (gridSize > 0)
        {
            // Nur so gross wie noetig um das Spielfeld herum aufspannen. Ein zu
            // grosses mapSize (z.B. 5000 bei einem 700er-Grid) verteilt die
            // Maskenpixel auf viel zu viele Weltunits: der Sichtrand wird zu einem
            // breiten, halbtransparenten Verlauf, durch den man beim Reinzoomen
            // Inseln und Einheiten sieht. 2x Grid laesst noch genug Rand zum Pannen.
            mapSize = gridSize * 2f;
        }
        else if (mapSize <= 0f)
        {
            mapSize = 2000f;
        }

        SetupFog();

        // Exploration ist static und überlebt Szenenwechsel — die Textur hier NICHT.
        // Deshalb: alle bereits bekannten Zellen einmal neu einbrennen.
        RepaintAllExplored();
    }

    private void SetupFog()
    {
        if (fogMaterial == null)
        {
            Shader fogShader = Shader.Find("Custom/CloudFogShader");
            Shader fallbackFog = fogShader ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Hidden/Internal-Colored");
            if (fallbackFog == null)
            {
                Debug.LogError("[FogProjector] Kein Nebel-Shader gefunden. FogProjector wird deaktiviert.");
                enabled = false;
                return;
            }

            if (fogShader != null)
            {
                fogMaterial = new Material(fogShader);
            }
            else
            {
                Debug.LogWarning("[FogProjector] Kein FogMaterial zugewiesen und Custom/CloudFogShader nicht gefunden. Verwende einen Fallback-Material.");
                fogMaterial = new Material(fallbackFog);
                fogMaterial.color = new Color(0.2f, 0.2f, 0.2f, 0.75f);
            }
        }

        maskTexture = new RenderTexture(maskResolution, maskResolution, 24);
        maskTexture.filterMode = FilterMode.Bilinear;

        // Aufdeck-Textur: normale Texture2D, wird per SetPixel befüllt.
        // 1024 reicht (der Aufdeck-Schleier ist ohnehin weich) und haelt den
        // Texture-Upload klein (2048^2 RGBA waeren 16 MB pro Apply).
        exploredResolution = Mathf.Min(maskResolution, 1024);
        exploredWorldSize = mapSize;
        exploredTexture = new Texture2D(exploredResolution, exploredResolution, TextureFormat.RGBA32, false);
        exploredTexture.filterMode = FilterMode.Bilinear;
        exploredTexture.wrapMode = TextureWrapMode.Clamp;

        CreateMaskCamera();

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.cullingMask &= ~(1 << 31);
        }
        else
        {
            Debug.LogWarning("[FogProjector] Keine Hauptkamera gefunden. Layer 31 wird nicht ausgeblendet.");
        }

        CreateFogOverlay();
    }

    private void CreateMaskCamera()
    {
        GameObject camObj = new GameObject("FogMaskCamera");
        camObj.transform.position = new Vector3(0, 0, -50f);
        maskCamera = camObj.AddComponent<Camera>();
        maskCamera.orthographic = true;
        maskCamera.orthographicSize = mapSize / 2f;
        maskCamera.targetTexture = maskTexture;
        maskCamera.clearFlags = CameraClearFlags.SolidColor;
        maskCamera.backgroundColor = Color.black;
        maskCamera.cullingMask = 1 << 31;
        maskCamera.depth = 1;
        // Wird manuell aus Update() in Intervallen gerendert, nicht jedes Frame automatisch
        maskCamera.enabled = false;
    }

    private void CreateFogOverlay()
    {
        fogQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fogQuad.name = "FogOverlay";
        fogQuad.transform.position = new Vector3(0, 0, -5f);
        fogQuad.transform.localScale = new Vector3(mapSize, mapSize, 1);
        Destroy(fogQuad.GetComponent<MeshCollider>());

        if (fogMaterial == null || fogQuad == null) return;

        Renderer fogRenderer = fogQuad.GetComponent<Renderer>();
        if (fogRenderer == null) return;

        Material instancedMat = fogRenderer.material = fogMaterial;

        if (instancedMat.HasProperty("_MaskTex")) instancedMat.SetTexture("_MaskTex", maskTexture);
        if (instancedMat.HasProperty("_ExploredTex")) instancedMat.SetTexture("_ExploredTex", exploredTexture);

        Texture2D fog1 = Resources.Load<Texture2D>("Textures/Fog1");
        Texture2D fog2 = Resources.Load<Texture2D>("Textures/Fog2");
        if (fog1 == null) fog1 = Resources.Load<Texture2D>("Fog1");
        if (fog2 == null) fog2 = Resources.Load<Texture2D>("Fog2");

        if (fog1 != null && instancedMat.HasProperty("_MainTex"))
            instancedMat.SetTexture("_MainTex", fog1);

        if (fog2 != null && instancedMat.HasProperty("_DetailTex"))
            instancedMat.SetTexture("_DetailTex", fog2);
    }

    private void Update()
    {
        // Mask-Kamera (aktuelles Sichtfeld) drosselt das pro-Frame-Rendern
        if (maskCamera != null)
        {
            maskRenderTimer -= Time.deltaTime;
            if (maskRenderTimer <= 0f)
            {
                maskRenderTimer = MaskRenderInterval;
                maskCamera.Render();
            }
        }

        // Aufdeck-Textur gebündelt zur GPU hochladen (nur wenn sich etwas geändert hat)
        if (exploredDirty && exploredTexture != null)
        {
            exploredApplyTimer -= Time.deltaTime;
            if (exploredApplyTimer <= 0f)
            {
                exploredApplyTimer = ExploredApplyInterval;
                exploredTexture.Apply(false);
                exploredDirty = false;
            }
        }
    }
}
