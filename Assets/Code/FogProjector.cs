using UnityEngine;
using System.Collections.Generic;

public class FogProjector : MonoBehaviour
{
    private static List<FogRevealer> revealers = new List<FogRevealer>();

    [Header("Fog Settings")]
    [SerializeField] private Material fogMaterial;
    [SerializeField] private int maskResolution = 1024;
    [SerializeField] private float mapSize = 2000f;

    private RenderTexture maskTexture;
    private RenderTexture exploredTexture;
    private Camera maskCamera;
    private Camera exploredCamera;
    private GameObject fogQuad;

    private static HashSet<Vector2> exploredCells = new HashSet<Vector2>();
    private static int explorationVersion = 0;
    public static int ExplorationVersion => explorationVersion;

    // ── Chunked explored-mesh (kein Full-Rebuild mehr — der größte Lag-Verursacher) ──
    private const int ChunkSize = 32;
    private static readonly HashSet<Vector2Int> dirtyChunks = new HashSet<Vector2Int>();
    private static readonly Dictionary<Vector2Int, List<Vector2>> chunkCells = new Dictionary<Vector2Int, List<Vector2>>();
    private static readonly Dictionary<Vector2Int, MeshFilter> chunkFilters = new Dictionary<Vector2Int, MeshFilter>();

    private GameObject exploredMeshObj;
    private const float ExploredRebuildInterval = 0.25f;
    private float exploredRebuildTimer = 0f;
    private const float MaskRenderInterval = 0.06f; // ~16 FPS reicht völlig für die Maske
    private float maskRenderTimer = 0f;

    public static bool IsExplored(Vector2 pos)
    {
        return exploredCells.Contains(new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y)));
    }

    public static void RegisterExploration(Vector2 center, float radius)
    {
        int r = Mathf.CeilToInt(radius);
        for (int x = -r; x <= r; x++)
        {
            for (int y = -r; y <= r; y++)
            {
                if (x*x + y*y <= radius*radius)
                {
                    Vector2 cell = new Vector2(Mathf.Round(center.x + x), Mathf.Round(center.y + y));
                    if (exploredCells.Add(cell))
                    {
                        explorationVersion++;

                        // Zelle direkt in ihren Chunk einsortieren (Liste wird je Chunk einmal geführt)
                        Vector2Int chunk = new Vector2Int(
                            Mathf.FloorToInt(cell.x / ChunkSize),
                            Mathf.FloorToInt(cell.y / ChunkSize));
                        if (!chunkCells.TryGetValue(chunk, out List<Vector2> list))
                        {
                            list = new List<Vector2>();
                            chunkCells[chunk] = list;
                        }
                        list.Add(cell);
                        dirtyChunks.Add(chunk);
                    }
                }
            }
        }
    }

    public static void AddRevealer(FogRevealer r) => revealers.Add(r);
    public static void RemoveRevealer(FogRevealer r) => revealers.Remove(r);

    private void Start()
    {
        if (GridManager.Instance != null)
        {
            int gridSize = GridManager.Instance.GetGridSize();
            mapSize = Mathf.Max(mapSize, gridSize * 1.5f);
        }

        SetupFog();

        // Exploration ist static und überlebt Szenenwechsel — die Chunk-Meshes hier NICHT.
        // Deshalb: alle bereits bekannten Chunks als dirty markieren, damit sie neu gebacken werden.
        foreach (Vector2Int chunk in chunkCells.Keys)
        {
            dirtyChunks.Add(chunk);
        }
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

        exploredTexture = new RenderTexture(maskResolution, maskResolution, 0, RenderTextureFormat.ARGB32);
        exploredTexture.filterMode = FilterMode.Bilinear;
        exploredTexture.Create();
        RenderTexture.active = exploredTexture;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = null;

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

        CreateExploredCamera();
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

    private void CreateExploredCamera()
    {
        GameObject camObj = new GameObject("ExploredMaskCamera");
        camObj.transform.SetParent(transform);
        camObj.transform.position = new Vector3(0, 0, -49f);
        exploredCamera = camObj.AddComponent<Camera>();
        exploredCamera.orthographic = true;
        exploredCamera.orthographicSize = mapSize / 2f;
        exploredCamera.targetTexture = exploredTexture;
        exploredCamera.clearFlags = CameraClearFlags.Nothing;
        exploredCamera.backgroundColor = Color.black;
        exploredCamera.cullingMask = 1 << 30;
        exploredCamera.depth = 0;
        exploredCamera.enabled = false;
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
        // Nur rendern, wenn es überhaupt etwas zu rendern gibt
        if (exploredTexture == null || exploredCamera == null || maskCamera == null) return;

        // Chunk-Rebuilds gebündelt (max. alle 0.25s statt bei jeder neuen Zelle sofort)
        exploredRebuildTimer -= Time.deltaTime;
        if (dirtyChunks.Count > 0 && exploredRebuildTimer <= 0f)
        {
            exploredRebuildTimer = ExploredRebuildInterval;
            RebuildDirtyChunks();
            dirtyChunks.Clear();

            // Explored-Textur nach geändertem Mesh neu backen
            exploredCamera.Render();
        }

        // Mask-Kamera (aktuelles Sichtfeld) drosselt das pro-Frame-Rendern
        maskRenderTimer -= Time.deltaTime;
        if (maskRenderTimer <= 0f)
        {
            maskRenderTimer = MaskRenderInterval;
            maskCamera.Render();
        }
    }

    private void RebuildDirtyChunks()
    {
        if (exploredMeshObj == null)
        {
            exploredMeshObj = new GameObject("ExploredMesh");
            exploredMeshObj.layer = 30;
            exploredMeshObj.transform.SetParent(transform);
            exploredMeshObj.transform.position = Vector3.zero;
            exploredMeshObj.AddComponent<MeshFilter>();
            MeshRenderer mr = exploredMeshObj.AddComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Unlit/Color"));
            mr.material.color = Color.white;
        }

        foreach (Vector2Int chunk in dirtyChunks)
        {
            List<Vector2> cells = GetOrCreateChunkCells(chunk);

            if (!chunkFilters.TryGetValue(chunk, out MeshFilter filter) || filter == null)
            {
                GameObject chunkObj = new GameObject($"ExploredChunk_{chunk.x}_{chunk.y}");
                chunkObj.layer = 30;
                chunkObj.transform.SetParent(exploredMeshObj.transform, false);
                filter = chunkObj.AddComponent<MeshFilter>();
                chunkFilters[chunk] = filter;
            }

            // Mesh wiederverwenden statt jedes Mal neu zu erzeugen (vermeidet GC/Finalize-Druck)
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh();
                filter.sharedMesh = mesh;
            }
            mesh.Clear();

            int cellCount = cells.Count;
            Vector3[] verts = new Vector3[cellCount * 4];
            int[] tris = new int[cellCount * 6];

            for (int i = 0; i < cellCount; i++)
            {
                Vector2 cell = cells[i];
                int vi = i * 4;
                verts[vi]     = new Vector3(cell.x - 0.5f, cell.y - 0.5f, 0f);
                verts[vi + 1] = new Vector3(cell.x + 0.5f, cell.y - 0.5f, 0f);
                verts[vi + 2] = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
                verts[vi + 3] = new Vector3(cell.x - 0.5f, cell.y + 0.5f, 0f);

                int ti = i * 6;
                tris[ti]     = vi;
                tris[ti + 1] = vi + 1;
                tris[ti + 2] = vi + 2;
                tris[ti + 3] = vi;
                tris[ti + 4] = vi + 2;
                tris[ti + 5] = vi + 3;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }
    }

    private List<Vector2> GetOrCreateChunkCells(Vector2Int chunk)
    {
        if (!chunkCells.TryGetValue(chunk, out List<Vector2> cells))
        {
            // Sollte normalerweise nie passieren (RegisterExploration legt alles an) — Fallback leere Liste
            cells = new List<Vector2>();
            chunkCells[chunk] = cells;
        }
        return cells;
    }
}
