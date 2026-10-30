using UnityEngine;
using System.Collections.Generic;
using Photon.Pun;

public enum IslandType { Plains, Desert, Jungle, Stone }
public enum ResourceType { Wood, Stone, Iron, Gold, Animal, Fruit, Wheat, None }

public class IslandManager : MonoBehaviour
{
    public static IslandManager Instance;
    private static Dictionary<Vector2, ResourceType> resourceNodes = new Dictionary<Vector2, ResourceType>();

    // Track node SpriteRenderers so we can hide them in fog
    private List<(Vector2 pos, SpriteRenderer sr)> resourceNodeRenderers = new List<(Vector2, SpriteRenderer)>();

    public static ResourceType GetResourceType(Vector2 pos)
    {
        Vector2 snapped = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
        return resourceNodes.TryGetValue(snapped, out ResourceType type) ? type : ResourceType.None;
    }

    /// <summary>
    /// Entfernt eine Rohstoffquelle am angegebenen Punkt (zerstört das Icon).
    /// </summary>
    public static void RemoveResourceNodeAt(Vector2 pos)
    {
        Vector2 snapped = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
        if (resourceNodes.ContainsKey(snapped))
        {
            resourceNodes.Remove(snapped);
        }

        if (Instance != null)
        {
            for (int i = Instance.resourceNodeRenderers.Count - 1; i >= 0; i--)
            {
                var entry = Instance.resourceNodeRenderers[i];
                if (new Vector2(Mathf.Round(entry.pos.x), Mathf.Round(entry.pos.y)) == snapped)
                {
                    if (entry.sr != null && entry.sr.gameObject != null)
                    {
                        Destroy(entry.sr.gameObject);
                    }
                    Instance.resourceNodeRenderers.RemoveAt(i);
                }
            }
        }
    }

    [Header("Island Settings")]
    [SerializeField] private Material islandMaterial;
    [SerializeField] private int islandCount = 15;
    [SerializeField] private int blocksPerIsland = 1000;
    [SerializeField] private float mapMargin = 45f;
    [SerializeField] private float minDistanceBetweenIslands = 50f;

    private List<Vector2> islandPositions = new List<Vector2>();
    private List<IslandType> islandTypes = new List<IslandType>();
    private static HashSet<Vector2> allLandCells = new HashSet<Vector2>();
    private static HashSet<Vector2> shallowWaterCells = new HashSet<Vector2>();
    private static Sprite defaultNodeSprite;

    public static bool IsLand(Vector2 pos)
    {
        return allLandCells.Contains(new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y)));
    }

    public static bool IsShallowWater(Vector2 pos)
    {
        return shallowWaterCells.Contains(new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y)));
    }

    private void CreateShallowWaterVisual()
    {
        if (shallowWaterCells.Count == 0) return;

        GameObject swObj = new GameObject("ShallowWater");
        swObj.transform.SetParent(transform);
        swObj.transform.position = new Vector3(0, 0, -0.08f);

        MeshFilter mf = swObj.AddComponent<MeshFilter>();
        MeshRenderer mr = swObj.AddComponent<MeshRenderer>();
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.3f, 0.6f, 1f, 0.35f);
        mr.material = mat;
        mr.sortingOrder = -1;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        Vector2[] offsets = {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f)
        };

        foreach (Vector2 cell in shallowWaterCells)
        {
            int vi = verts.Count;
            foreach (var off in offsets)
                verts.Add(new Vector3(cell.x + off.x, cell.y + off.y, 0));
            tris.Add(vi); tris.Add(vi + 1); tris.Add(vi + 2);
            tris.Add(vi); tris.Add(vi + 2); tris.Add(vi + 3);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mf.mesh = mesh;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        ApplyLobbySettings();

        allLandCells.Clear();
        resourceNodes.Clear();
        resourceNodeRenderers.Clear();

        if (defaultNodeSprite == null)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            defaultNodeSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1.0f);
        }
    }

    private void ApplyLobbySettings()
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbySettingsKeys.WorldSize, out object worldSizeObj))
        {
            string preset = worldSizeObj.ToString();
            switch (preset)
            {
                case "Kompakt":
                    islandCount = 8;
                    blocksPerIsland = 900;
                    minDistanceBetweenIslands = 35f;
                    mapMargin = 30f;
                    break;
                case "Standard":
                    islandCount = 10;
                    blocksPerIsland = 1200;
                    minDistanceBetweenIslands = 30f;
                    mapMargin = 25f;
                    break;
                case "Gross":
                    islandCount = 15;
                    blocksPerIsland = 1500;
                    minDistanceBetweenIslands = 35f;
                    mapMargin = 30f;
                    break;
                default:
                    islandCount = 10;
                    blocksPerIsland = 1200;
                    minDistanceBetweenIslands = 30f;
                    mapMargin = 25f;
                    break;
            }
        }
    }

    private void Start()
    {
        GenerateIslands();
    }

    private void Update()
    {
        // Hide resource nodes that are still covered by fog.
        // Throttle: nur wenn sich die Exploration geändert hat und max. alle 0.5s.
        // (Vorher: alle N Nodes pro Frame = ständiger CPU-Fraß auf großen Maps.)
        if (resourceNodeRenderers.Count == 0) return;

        fogPollTimer -= Time.deltaTime;
        if (fogPollTimer <= 0f)
        {
            fogPollTimer = 0.5f;
            int currentVersion = FogProjector.ExplorationVersion;
            if (currentVersion != lastFogVersion)
            {
                lastFogVersion = currentVersion;
                foreach (var entry in resourceNodeRenderers)
                {
                    if (entry.sr == null) continue;
                    entry.sr.enabled = FogProjector.IsExplored(entry.pos);
                }
            }
        }
    }

    private float fogPollTimer = 0f;
    private int lastFogVersion = -1;

    public bool IsGenerated { get; private set; } = false;

    public void GenerateIslands()
    {
        if (IsGenerated) return;
        IsGenerated = true;

        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbySettingsKeys.MapSeed, out object seedObj))
        {
            int seed = System.Convert.ToInt32(seedObj);
            Random.InitState(seed);
            Debug.Log($"[IslandManager] Initializing procedural generation with synchronized seed: {seed}");
        }
        else
        {
            int fallbackSeed = Random.Range(1, 1000000);
            Random.InitState(fallbackSeed);
            Debug.Log($"[IslandManager] Offline/No seed found, generated local seed: {fallbackSeed}");
        }

        float range = (GridManager.Instance != null) ? (GridManager.Instance.GetGridSize() / 2f) - mapMargin : 1000f;

        int playerCount = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbySettingsKeys.InitialPlayerCount, out object ipcObj))
        {
            playerCount = System.Convert.ToInt32(ipcObj);
        }

        IslandType[] nonPlainsTypes = { IslandType.Desert, IslandType.Jungle, IslandType.Stone };

        // Test-Modus: exakt das fixe, deterministiche 2-Inseln-Layout (nebeneinander).
        bool testMode = TestModeManager.IsTestRoom() && TestModeManager.TestIslandLayout != null
                        && TestModeManager.TestIslandLayout.Length > 0;
        if (testMode)
        {
            islandCount = TestModeManager.TestIslandLayout.Length;
            blocksPerIsland = 1100;
            Debug.Log("[IslandManager] TEST-MODUS: 2 fixe Inseln nebeneinander.");
        }

        int attempts = 0;
        while (islandPositions.Count < islandCount && attempts < 5000)
        {
            attempts++;
            Vector2 newPos;
            if (testMode)
            {
                newPos = TestModeManager.TestIslandLayout[islandPositions.Count];
            }
            else
            {
                float x = Mathf.Round(Random.Range(-range, range));
                float y = Mathf.Round(Random.Range(-range, range));
                newPos = new Vector2(x, y);
            }

            bool tooClose = false;
            if (!testMode)
            {
                foreach (Vector2 p in islandPositions)
                {
                    if (Vector2.Distance(p, newPos) < minDistanceBetweenIslands)
                    {
                        tooClose = true;
                        break;
                    }
                }
            }

            if (!tooClose)
            {
                IslandType type;
                if (testMode || islandPositions.Count < playerCount)
                    type = IslandType.Plains;
                else
                    type = nonPlainsTypes[Random.Range(0, nonPlainsTypes.Length)];

                islandPositions.Add(newPos);
                islandTypes.Add(type);
                CreateIslandMesh(newPos, type);
            }
        }

        // Shallow-Water-Ring um jede Insel generieren
        Vector2Int[] cardDirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        List<Vector2> landList = new List<Vector2>(allLandCells);
        foreach (Vector2 landCell in landList)
        {
            foreach (var dir in cardDirs)
            {
                Vector2 neighbor = new Vector2(landCell.x + dir.x, landCell.y + dir.y);
                if (!allLandCells.Contains(neighbor))
                    shallowWaterCells.Add(neighbor);
            }
        }
        CreateShallowWaterVisual();
    }

    private void CreateIslandMesh(Vector2 startPos, IslandType type)
    {
        HashSet<Vector2> occupiedCells = new HashSet<Vector2>();
        occupiedCells.Add(startPos);
        List<Vector2> edgeGrowthCells = new List<Vector2> { startPos };

        Vector2 biasDir = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;

        int attempts = 0;
        while (occupiedCells.Count < blocksPerIsland && attempts < blocksPerIsland * 10)
        {
            attempts++;
            if (attempts % (blocksPerIsland / 4) == 0)
                biasDir = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;

            int index = (Random.value > 0.6f) ? edgeGrowthCells.Count - 1 : Random.Range(0, edgeGrowthCells.Count);
            Vector2 current = edgeGrowthCells[index];

            Vector2[] neighbors = { current + Vector2.up, current + Vector2.down, current + Vector2.left, current + Vector2.right };

            float[] scores = new float[4];
            for (int i = 0; i < 4; i++)
            {
                scores[i] = Vector2.Dot((neighbors[i] - current), biasDir) + Random.Range(-0.5f, 0.5f);
            }
            
            System.Array.Sort(scores, neighbors);
            System.Array.Reverse(neighbors);

            bool addedAny = false;
            foreach (Vector2 next in neighbors)
            {
                if (!occupiedCells.Contains(next))
                {
                    occupiedCells.Add(next);
                    edgeGrowthCells.Add(next);
                    addedAny = true;
                    if (Random.value > 0.3f) break;
                }
            }

            // Remove interior cells from growth list to optimize
            if (!addedAny)
            {
                bool completelySurrounded = true;
                foreach (Vector2 n in neighbors)
                {
                    if (!occupiedCells.Contains(n))
                    {
                        completelySurrounded = false;
                        break;
                    }
                }
                if (completelySurrounded)
                {
                    edgeGrowthCells.RemoveAt(index);
                }
            }
        }

        // Fill holes
        for (int iteration = 0; iteration < 2; iteration++)
        {
            List<Vector2> holesToFill = new List<Vector2>();
            foreach (Vector2 cell in occupiedCells)
            {
                foreach (Vector2 n in new Vector2[] { cell + Vector2.up, cell + Vector2.down, cell + Vector2.left, cell + Vector2.right })
                {
                    if (!occupiedCells.Contains(n))
                    {
                        int landCount = 0;
                        foreach (Vector2 n2 in new Vector2[] { n + Vector2.up, n + Vector2.down, n + Vector2.left, n + Vector2.right })
                            if (occupiedCells.Contains(n2)) landCount++;
                        if (landCount >= 3) holesToFill.Add(n);
                    }
                }
            }
            foreach (Vector2 hole in holesToFill) occupiedCells.Add(hole);
        }

        // 1. Add logical land cells
        foreach (Vector2 cell in occupiedCells) allLandCells.Add(cell);

        // Distribute resources based on logical occupiedCells
        DistributeResources(occupiedCells, type);

        // 2. Visuelle Inselkacheln ohne weichen Übergang: nur echte Landzellen
        HashSet<Vector2> visualCells = new HashSet<Vector2>(occupiedCells);

        // 3. Bounding Box für UV-Koordinaten berechnen
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector2 cell in visualCells)
        {
            if (cell.x < minX) minX = cell.x;
            if (cell.x > maxX) maxX = cell.x;
            if (cell.y < minY) minY = cell.y;
            if (cell.y > maxY) maxY = cell.y;
        }

        float boundingWidth = (maxX - minX) + 1f;
        float boundingHeight = (maxY - minY) + 1f;

        // Build visual mesh
        GameObject islandObj = new GameObject($"Island_{type}_{startPos}");
        islandObj.transform.SetParent(transform);
        // Weisen jedem Insel-Mesh eine geringfügig unterschiedliche Z-Ebene zu (transform.childCount),
        // damit sich überlappende Inseln ohne Z-Fighting perfekt rendern und mischen lassen!
        float zOffset = -0.1f - (transform.childCount * 0.005f);
        islandObj.transform.position = new Vector3(0, 0, zOffset);

        MeshFilter meshFilter = islandObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = islandObj.AddComponent<MeshRenderer>();

        // Material für die gesamte Insel wählen: nur eine Textur pro Insel
        List<string> texNames = GetTextureNamesForBiome(type);
        int textureIndex = 0;
        if (texNames.Count > 1)
        {
            textureIndex = Mathf.Abs((int)(startPos.x * 73856093) ^ (int)(startPos.y * 19349663)) % texNames.Count;
        }
        string selectedTexture = texNames[textureIndex];

        Texture2D texture = Resources.Load<Texture2D>($"Textures/{selectedTexture}");
        Material mat = new Material(Shader.Find("Sprites/Default"));
        if (texture != null)
        {
            mat.mainTexture = texture;
        }
        else
        {
            Debug.LogError($"[IslandManager] Hintergrund-Textur nicht gefunden: Textures/{selectedTexture}");
        }
        meshRenderer.material = mat;

        List<Vector3> vertices = new List<Vector3>();
        List<Color> colors = new List<Color>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        int vIndex = 0;

        foreach (Vector2 cell in visualCells)
        {
            Color cellColor = new Color(1f, 1f, 1f, 1f);

            Vector3 v0 = new Vector3(cell.x - 0.5f, cell.y - 0.5f, 0f);
            Vector3 v1 = new Vector3(cell.x - 0.5f, cell.y + 0.5f, 0f);
            Vector3 v2 = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
            Vector3 v3 = new Vector3(cell.x + 0.5f, cell.y - 0.5f, 0f);

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            float tileSize = 2f;
            uvs.Add(new Vector2(v0.x / tileSize, v0.y / tileSize));
            uvs.Add(new Vector2(v1.x / tileSize, v1.y / tileSize));
            uvs.Add(new Vector2(v2.x / tileSize, v2.y / tileSize));
            uvs.Add(new Vector2(v3.x / tileSize, v3.y / tileSize));

            for (int j = 0; j < 4; j++) colors.Add(cellColor);

            triangles.Add(vIndex);
            triangles.Add(vIndex + 1);
            triangles.Add(vIndex + 2);
            triangles.Add(vIndex);
            triangles.Add(vIndex + 2);
            triangles.Add(vIndex + 3);
            vIndex += 4;
        }

        Mesh mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.colors = colors.ToArray();
        mesh.subMeshCount = 1;
        mesh.SetTriangles(triangles.ToArray(), 0);
        
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;
    }

    private List<string> GetTextureNamesForBiome(IslandType type)
    {
        List<string> texNames = new List<string>();
        switch (type)
        {
            case IslandType.Plains:
                texNames.Add("Plains_Background");
                texNames.Add("Plains_Background2");
                texNames.Add("Plains_Background3");
                texNames.Add("Plains_Background4");
                texNames.Add("Plains_Background5");
                break;
            case IslandType.Desert:
                texNames.Add("Wueste_Background");
                texNames.Add("Wueste_Background1");
                texNames.Add("Wueste_Background2");
                texNames.Add("Wueste_Background3");
                texNames.Add("Wueste_Background5");
                break;
            case IslandType.Jungle:
                texNames.Add("Jungle_Background");
                texNames.Add("Jungle_Background1");
                texNames.Add("Jungle_Background2");
                texNames.Add("Jungle_Background3");
                texNames.Add("Jungle_Background4");
                texNames.Add("Jungle_Background5");
                break;
            case IslandType.Stone:
                texNames.Add("Stone_Background");
                texNames.Add("Stone_Background2");
                texNames.Add("Stone_Background3");
                texNames.Add("Stone_Background4");
                texNames.Add("Stone_Background5");
                break;
        }
        return texNames;
    }

    // -------------------------------------------------------------------------
    // Resource distribution (biome-aware, every island gets ALL resource types)
    // -------------------------------------------------------------------------

    private struct ResourceConfig
    {
        public ResourceType type;
        public int clusterCount;
        public int clusterRadius;
        public float spawnChance;
        public ResourceConfig(ResourceType t, int count, int radius, float chance)
        { type = t; clusterCount = count; clusterRadius = radius; spawnChance = chance; }
    }

    private void DistributeResources(HashSet<Vector2> cells, IslandType biome)
    {
        List<Vector2> cellList = new List<Vector2>(cells);
        if (cellList.Count == 0) return;

        // Deterministische Sortierung, da HashSet-Reihenfolge nicht garantiert ist
        cellList.Sort((a, b) => {
            int cmpX = a.x.CompareTo(b.x);
            if (cmpX != 0) return cmpX;
            return a.y.CompareTo(b.y);
        });

        ResourceConfig[] configs = GetResourceConfigsForBiome(biome);
        foreach (var cfg in configs)
        {
            for (int h = 0; h < cfg.clusterCount; h++)
            {
                Vector2 hubCenter = cellList[Random.Range(0, cellList.Count)];
                CreateResourceCluster(hubCenter, cfg.type, cfg.clusterRadius, cfg.spawnChance);
            }
        }
    }

    private ResourceConfig[] GetResourceConfigsForBiome(IslandType biome)
    {
        switch (biome)
        {
            // Plains: viel Weizen, Holz, etwas von allem, wenig Eisen/Gold
            case IslandType.Plains:
                return new ResourceConfig[]
                {
                    new ResourceConfig(ResourceType.Wheat,  3, 6, 0.85f),
                    new ResourceConfig(ResourceType.Wood,   2, 4, 0.65f),
                    new ResourceConfig(ResourceType.Stone,  1, 3, 0.55f),
                    new ResourceConfig(ResourceType.Fruit,  1, 3, 0.50f),
                    new ResourceConfig(ResourceType.Animal, 1, 3, 0.50f),
                    new ResourceConfig(ResourceType.Iron,   1, 2, 0.40f),
                    new ResourceConfig(ResourceType.Gold,   1, 1, 0.30f),
                };
            // Wüste: viele Früchte für starke Zufriedenheits-Boosts, schwacher Ackerbau
            case IslandType.Desert:
                return new ResourceConfig[]
                {
                    new ResourceConfig(ResourceType.Fruit,  6, 6, 0.80f),
                    new ResourceConfig(ResourceType.Animal, 2, 4, 0.60f),
                    new ResourceConfig(ResourceType.Stone,  2, 5, 0.70f),
                    new ResourceConfig(ResourceType.Iron,   2, 3, 0.55f),
                    new ResourceConfig(ResourceType.Wheat,  1, 3, 0.40f),
                    new ResourceConfig(ResourceType.Wood,   1, 3, 0.35f),
                    new ResourceConfig(ResourceType.Gold,   1, 2, 0.35f),
                };
            // Jungle: viel Holz, viel Eisen, etwas Frucht, wenig Rest
            case IslandType.Jungle:
                return new ResourceConfig[]
                {
                    new ResourceConfig(ResourceType.Wood,   3, 6, 0.90f),
                    new ResourceConfig(ResourceType.Iron,   2, 4, 0.70f),
                    new ResourceConfig(ResourceType.Fruit,  2, 3, 0.55f),
                    new ResourceConfig(ResourceType.Stone,  1, 2, 0.40f),
                    new ResourceConfig(ResourceType.Animal, 1, 2, 0.40f),
                    new ResourceConfig(ResourceType.Wheat,  1, 2, 0.30f),
                    new ResourceConfig(ResourceType.Gold,   1, 1, 0.25f),
                };
            // Stein: viel Stein, Eisen, Gold – wenig organisches
            case IslandType.Stone:
                return new ResourceConfig[]
                {
                    new ResourceConfig(ResourceType.Stone,  3, 6, 0.90f),
                    new ResourceConfig(ResourceType.Iron,   3, 5, 0.80f),
                    new ResourceConfig(ResourceType.Gold,   2, 4, 0.70f),
                    new ResourceConfig(ResourceType.Wood,   1, 2, 0.30f),
                    new ResourceConfig(ResourceType.Wheat,  1, 1, 0.25f),
                    new ResourceConfig(ResourceType.Fruit,  1, 1, 0.25f),
                    new ResourceConfig(ResourceType.Animal, 1, 1, 0.25f),
                };
        }
        return new ResourceConfig[0];
    }

    private void CreateResourceCluster(Vector2 center, ResourceType type, int radius, float spawnChance = 0.8f)
    {
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    Vector2 pos = new Vector2(Mathf.Round(center.x + x), Mathf.Round(center.y + y));
                    if (IsLand(pos) && !resourceNodes.ContainsKey(pos))
                    {
                        if (Random.value < spawnChance) SpawnResourceNode(pos, type);
                    }
                }
            }
        }
    }

    private void SpawnResourceNode(Vector2 pos, ResourceType type)
    {
        resourceNodes[pos] = type;

        GameObject node = new GameObject($"Node_{type}");
        node.transform.position = new Vector3(pos.x, pos.y, -0.15f);
        node.transform.localScale = Vector3.one;

        SpriteRenderer sr = node.AddComponent<SpriteRenderer>();
        sr.sprite = Resources.Load<Sprite>($"{type}_Icon");
        if (sr.sprite == null) sr.sprite = Resources.Load<Sprite>($"{type}_Overlay");
        sr.sortingOrder = 11;

        if (sr.sprite == null)
        {
            sr.sprite = defaultNodeSprite;
            sr.color = GetColorForResource(type);
        }

        // Start hidden – Update() will reveal when explored
        sr.enabled = false;

        // Track for fog toggling
        resourceNodeRenderers.Add((pos, sr));
    }

    private Color GetColorForResource(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Wood:   return new Color(0.4f, 0.25f, 0.1f);
            case ResourceType.Stone:  return Color.gray;
            case ResourceType.Iron:   return new Color(0.7f, 0.7f, 0.8f);
            case ResourceType.Gold:   return new Color(1f, 0.85f, 0f);
            case ResourceType.Animal: return Color.white;
            case ResourceType.Fruit:  return Color.magenta;
            case ResourceType.Wheat:  return new Color(0.9f, 0.8f, 0.2f);
        }
        return Color.clear;
    }

    public Vector2 GetIslandPosition(int index)
    {
        if (islandPositions.Count == 0) return Vector2.zero;
        return islandPositions[index % islandPositions.Count];
    }

    public IslandType GetIslandType(int index)
    {
        if (islandTypes.Count == 0) return IslandType.Plains;
        return islandTypes[index % islandTypes.Count];
    }
}
