using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fog-of-War-Gedaechtnis:
/// Objekte in aufgedeckten, aber aktuell NICHT sichtbaren Nebelgebieten werden
/// als eingefrorener Schnappschuss an der ZULETZT gesehenen Stelle angezeigt –
/// leicht ausgegraut und ohne Live-Updates (Bewegung, Sprite-/Farbaenderungen,
/// Zerstoerung etc. bleiben im Nebel unsichtbar).
///
/// Zustaende pro Objekt:
///   – Unexplored : komplett hinter dem dichten Nebel -> Renderer aus
///   – Memory     : aufgedeckt, aber nicht im Sichtkreis -> Renderer aus,
///                  Ghost mit dem letzten gesehenen Aussehen bleibt stehen
///   – Live       : im Sichtkreis -> Renderer an, saemtliche Updates laufen normal
/// </summary>
public class FogMemory : MonoBehaviour
{
    public static void Add(GameObject go)
    {
        if (go == null || go.GetComponent<FogMemory>() != null) return;
        go.AddComponent<FogMemory>();
    }

    private enum FovState { Unexplored, Memory, Live }

    private const float PollInterval = 0.3f;
    private const float GhostRevealCheckInterval = 0.5f;

    /// <summary>Transform, dessen Position die Fog-Pruefung bestimmt (Standard: eigener).</summary>
    [Tooltip("Position, an der die Nebel-Sichtbarkeit geprueft wird. Null = eigene Transform.")]
    public Transform target;

    private FovState state = FovState.Unexplored;
    private Renderer[] sources;
    private readonly List<Ghost> ghosts = new List<Ghost>();
    private Vector3 lastSeenPos;
    private float pollTimer;

    private class Ghost
    {
        public GameObject obj;
        public Vector3 pos;
    }

    /// <summary>Container, in dem alle Ghosts leben (unabhaengig vom Original – bleibt auch nach dessen Zerstoerung bestehen).</summary>
    private static Transform ghostRoot;

    private static Transform GhostRoot
    {
        get
        {
            if (ghostRoot == null)
            {
                GameObject root = new GameObject("FogMemoryGhosts");
                ghostRoot = root.transform;
            }
            return ghostRoot;
        }
    }

    private void Awake()
    {
        if (target == null) target = transform;
    }

    private void OnEnable()
    {
        // Sofort pollen, damit frisch gespawnte im Nebel nie kurz aufblitzen
        pollTimer = 0f;
        Poll();
        pollTimer = Random.Range(0.05f, PollInterval); // Poll-Intervall ueber Frames verteilen
    }

    private void Update()
    {
        pollTimer -= Time.deltaTime;
        if (pollTimer > 0f) return;
        pollTimer = PollInterval;
        Poll();
    }

    private void Poll()
    {
        if (target == null) return;
        if (sources == null || sources.Length == 0) CollectSources();

        Vector2 pos = target.position;

        // 1) Noch nie aufgedeckt -> komplett unsichtbar
        if (!FogProjector.IsExplored(pos))
        {
            if (state != FovState.Unexplored)
            {
                state = FovState.Unexplored;
                HideGhosts();
                SetSourcesEnabled(false);
            }
            return;
        }

        // 2) Aktuell im Sichtkreis -> Live, alles normal
        //    (1 Zelle Hysterese gegen Flackern genau an der Sichtkante)
        if (FogProjector.VisibilityDepth(pos) > -1f)
        {
            lastSeenPos = target.position;

            if (state != FovState.Live)
            {
                state = FovState.Live;
                HideGhosts();
                // Nur beim Uebergang einschalten – ansonsten bleibt die eigene
                // enabled-Steuerung des Objekts unangetastet (z.B. Nodes).
                SetSourcesEnabled(true);
            }
            return;
        }

        // 3) Aufgedeckt, aber nicht mehr sichtbar -> Gedhaechtnis/Snapshot
        if (state == FovState.Live)
        {
            // Beim Verlassen des Sichtkreises einfrieren: Ghost zeigt,
            // wie das Objekt zuletzt gesehen wurde (wird danach nicht mehr geupdated).
            CreateGhosts();
        }
        state = FovState.Memory;
        SetSourcesEnabled(false);
    }

    private void CollectSources()
    {
        List<Renderer> list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (IsExcluded(r)) continue;
            list.Add(r);
        }
        sources = list.ToArray();
    }

    /// <summary>Renderer, die NICHT ins Gedaechtnis gehoeren (Maske/Explored-Mesh/eigene Fabrik-Helper).</summary>
    private static bool IsExcluded(Renderer r)
    {
        if (r == null) return true;
        if (r.gameObject.name == "FogMask" || r.gameObject.name == "MaskIndicator") return true;
        if (r.gameObject.layer == 31 || r.gameObject.layer == 30) return true; // Fog-Masken-Layer
        return false;
    }

    private void SetSourcesEnabled(bool on)
    {
        if (sources == null) return;
        foreach (Renderer r in sources)
        {
            if (r == null) continue;
            r.enabled = on;
        }
    }

    // ── Ghosts ──────────────────────────────────────────────────────────────

    private void CreateGhosts()
    {
        HideGhosts();

        // Verschiebung: Das Objekt kann seit dem letzten Live-Poll weitergedriftet
        // sein; der Snapshot soll bei der ZULETZT gesehenen Position stehen.
        Vector3 shift = lastSeenPos - target.position;

        foreach (Renderer r in sources)
        {
            if (r == null || IsExcluded(r)) continue;

            GameObject g;
            Vector3 gPos = r.transform.position + shift;
            Quaternion gRot = r.transform.rotation;
            Vector3 gScale = r.transform.lossyScale;

            SpriteRenderer sr = r as SpriteRenderer;
            if (sr != null && sr.sprite != null)
            {
                g = new GameObject("FogGhost_" + r.name);
                SpriteRenderer gs = g.AddComponent<SpriteRenderer>();
                gs.sprite = sr.sprite;
                gs.color = Dim(sr.color);
                gs.sortingLayerID = sr.sortingLayerID;
                gs.sortingOrder = sr.sortingOrder;
            }
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Collider col = g.GetComponent<Collider>();
                if (col != null) Destroy(col);

                MeshRenderer gr = g.GetComponent<MeshRenderer>();
                Material mat = r.sharedMaterial;
                Texture tex = mat != null ? mat.mainTexture : null;
                Color col2 = mat != null ? mat.color : Color.white;

                Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
                Material gm = new Material(sh != null ? sh : Shader.Find("Unlit/Color"));
                if (tex != null) gm.mainTexture = tex;
                gm.color = Dim(col2);
                gr.material = gm;
            }

            g.transform.SetParent(GhostRoot, false);
            g.transform.position = gPos;
            g.transform.rotation = gRot;
            g.transform.localScale = gScale;

            Ghost ghost = new Ghost { obj = g, pos = gPos };
            FogGhostPurge purge = g.AddComponent<FogGhostPurge>();
            purge.pos = gPos;
            ghosts.Add(ghost);
        }
    }

    private void HideGhosts()
    {
        foreach (Ghost g in ghosts)
        {
            if (g.obj != null) Destroy(g.obj);
        }
        ghosts.Clear();
    }

    /// <summary>"Leicht ausgegraut": entsaettigt + etwas abgedunkelt, aber noch klar erkennbar.</summary>
    private static Color Dim(Color c)
    {
        float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
        Color g = new Color(lum, lum, lum, c.a);
        Color d = Color.Lerp(c, g, 0.5f);
        d.r *= 0.8f;
        d.g *= 0.8f;
        d.b *= 0.8f;
        return d;
    }

    /// <summary>
    /// Eigenstaendige Purge-Komponente auf jedem Ghost:
    /// Sobald der Spieler die Ghost-Stelle wieder DIRKT sieht, weicht das
    /// Gedaechtnis der Realitaet (leer) – Ghost verschwindet.
    /// </summary>
    private class FogGhostPurge : MonoBehaviour
    {
        public Vector3 pos;
        private float timer;

        private void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = GhostRevealCheckInterval;

            // Erst aufloesen, wenn die Stelle DEUTLICH im Sichtfeld liegt (> 3 Zellen
            // Abstand zum Sichtrand). Geister direkt AM Rand bleiben stehen – sonst
            // wuerde der Snapshot genau in dem Moment geloescht, in dem er entsteht
            // (die letzte gesehene Position liegt ja per Definition am Sichtrand).
            if (FogProjector.VisibilityDepth(pos) > 3f) Destroy(gameObject);
        }
    }
}
