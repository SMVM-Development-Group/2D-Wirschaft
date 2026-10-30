using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using System.Collections.Generic;
using System.Linq;

public enum PlacementRule { LandOnly, WaterOnly, Pier, Ship }

public class BuildingManager : MonoBehaviour, IOnEventCallback
{
    public static BuildingManager Instance;
    private static HashSet<Vector2> occupiedBuildingCells = new HashSet<Vector2>();

    public static bool IsOccupied(Vector2 pos)
    {
        Vector2 snapped = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
        return occupiedBuildingCells.Contains(snapped) || IsStegAt(snapped);
    }

    public static bool IsStegAt(Vector2 pos)
    {
        Vector2Int grid = new Vector2Int(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y));
        return stegGrids.Contains(grid);
    }

    public static bool IsWalkable(Vector2 pos)
    {
        // Wasser (auch flaches Wasser) ist fuer Einheiten NICHT begehbar.
        // Inseln sind nur ueber Stege verbunden — siehe IsStegAt.
        Vector2Int grid = new Vector2Int(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y));
        return IslandManager.IsLand(grid) || IsStegAt(grid);
    }

    public static bool CanPlacePierAt(Vector2Int grid)
    {
        if (IslandManager.IsLand(grid)) return false;
        return HasLandAdjacency(grid) || HasPierAdjacency(grid);
    }

    public static void RegisterOccupancy(Vector2 center, int width, int height)
    {
        float startX = -(width - 1) / 2f;
        float startY = -(height - 1) / 2f;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                occupiedBuildingCells.Add(center + new Vector2(startX + x, startY + y));
            }
        }
    }

    /// <summary>Gibt die Gebaeudezellen nach Zerstoerung wieder frei (war vorher ein Leak!).</summary>
    public static void FreeOccupancy(Vector2 center, int width, int height)
    {
        float startX = -(width - 1) / 2f;
        float startY = -(height - 1) / 2f;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                occupiedBuildingCells.Remove(center + new Vector2(startX + x, startY + y));
            }
        }
    }

    [Header("Building Prefabs")]
    [SerializeField] private Material warehouseMaterial;
    [Header("Steg Prefab")]
    public GameObject stegPrefab;

    [Header("Ship Settings")]
    [Tooltip("Standard Schiff-Stufe für neue Schiffe")]
    public int defaultShipLevel = 1;
    [Tooltip("Segel-Reichweite")]
    public float maxSailDistance = 100f;
    [Tooltip("Pfadfindungs-Suchradius für Schiffe")]
    public int shipPathSearchRadius = 50;

    // aktive Stege
    private static HashSet<Steg> stegs = new HashSet<Steg>();
    // O(1)-Grid fuer Stege (Pfadfindung fraegt das sehr hauefig ab)
    private static HashSet<Vector2Int> stegGrids = new HashSet<Vector2Int>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        occupiedBuildingCells.Clear();
        stegGrids.Clear();
    }

    // --- Steg-Management (1x1 Stege im Wasser) ---
    public static void RegisterSteg(Steg s)
    {
        if (s == null) return;
        stegs.Add(s);
        stegGrids.Add(s.GridPosition);
    }

    public static void UnregisterSteg(Steg s)
    {
        if (s == null) return;
        stegs.Remove(s);
        stegGrids.Remove(s.GridPosition);
    }

    /// <summary>Bindet die O(1)-Grid-Zelle eines bereits registrierten Stegs um (bei Reposition).</summary>
    public static void StegGridChanged(Steg s, Vector2Int oldGrid, Vector2Int newGrid)
    {
        if (s == null) return;
        stegGrids.Remove(oldGrid);
        stegGrids.Add(newGrid);
    }

    // Platzieren eines Stegs an einer World-Position (X,Y). Gibt true zurück, wenn platziert.
    public bool PlaceStegAt(Vector2 position, bool isLocal = true, bool skipValidation = false, BuildingData data = null)
    {
        if (stegPrefab == null) { Debug.LogWarning("BuildingManager: stegPrefab fehlt"); return false; }

        Vector2Int grid = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
        if (!skipValidation)
        {
            if (!IsWaterAt(grid)) return false;
            if (IsOccupied(position)) return false;
            if (!HasLandAdjacency(grid) && !HasPierAdjacency(grid)) return false;
        }
        else
        {
            if (IslandManager.IsLand(grid)) return false;
            if (IsOccupied(position)) return false;
        }

        GameObject go = Instantiate(stegPrefab);
        go.transform.localScale = new Vector3(1.3f, 1.3f, 1f);

        Steg steg = go.GetComponent<Steg>();
        if (steg == null)
            steg = go.AddComponent<Steg>();
        steg.Initialize(new Vector3(position.x, position.y, -0.21f));

        if (data != null)
        {
            BuildingInstance instance = go.GetComponent<BuildingInstance>();
            if (instance == null) instance = go.AddComponent<BuildingInstance>();
            instance.data = data;
            instance.isLocal = isLocal;
        }

        RegisterOccupancy(position, 1, 1);
        return true;
    }

    public static void OnStegRemoved(Steg removed)
    {
        UnregisterSteg(removed);
        if (Instance != null) Instance.ReevaluateSupports();
    }

    void ReevaluateSupports()
    {
        var all = stegs.ToList();
        HashSet<Steg> supported = new HashSet<Steg>();

        foreach (var s in all)
        {
            if (IsAdjacentToLand(s.GridPosition)) supported.Add(s);
        }

        Queue<Steg> q = new Queue<Steg>(supported);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var n in GetStegNeighbors(cur))
            {
                if (!supported.Contains(n)) { supported.Add(n); q.Enqueue(n); }
            }
        }

        foreach (var s in all)
        {
            if (!supported.Contains(s))
            {
                stegs.Remove(s);
                if (s != null) Destroy(s.gameObject);
            }
        }
    }

    static bool IsWaterAt(Vector2Int grid)
    {
        // The world is generated by IslandManager: cells present in IslandManager are land,
        // everything else is water. Use that logical map instead of tags.
        return !IslandManager.IsLand(grid);
    }

    public static bool HasLandAdjacency(Vector2Int grid)
    {
        Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        foreach (var d in dirs)
        {
            Vector2Int n = grid + d;
            if (IslandManager.IsLand(n)) return true;
        }
        return false;
    }

    public static bool HasPierAdjacency(Vector2Int grid)
    {
        return
            stegGrids.Contains(grid + new Vector2Int(1, 0)) ||
            stegGrids.Contains(grid + new Vector2Int(-1, 0)) ||
            stegGrids.Contains(grid + new Vector2Int(0, 1)) ||
            stegGrids.Contains(grid + new Vector2Int(0, -1));
    }

    public static List<Vector2Int> GetStegPositions()
    {
        List<Vector2Int> positions = new List<Vector2Int>();
        foreach (var steg in stegs)
        {
            if (steg != null)
            {
                positions.Add(steg.GridPosition);
            }
        }
        return positions;
    }

    public static List<Vector2> FindPath(Vector2 start, Vector2 destination)
    {
        List<Vector2> empty = new List<Vector2>();
        Vector2Int startGrid = new Vector2Int(Mathf.RoundToInt(start.x), Mathf.RoundToInt(start.y));
        Vector2Int destGrid = new Vector2Int(Mathf.RoundToInt(destination.x), Mathf.RoundToInt(destination.y));

        if (!IsWalkable(startGrid) || !IsWalkable(destGrid))
        {
            return empty;
        }

        if (startGrid == destGrid)
        {
            empty.Add(destination);
            return empty;
        }

        PriorityQueue frontier = new PriorityQueue();
        frontier.Enqueue(startGrid, 0f);

        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, float> costSoFar = new Dictionary<Vector2Int, float>
        {
            [startGrid] = 0f
        };

        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        int iterations = 0;

        while (frontier.Count > 0 && iterations++ < 12000)
        {
            Vector2Int current = frontier.Dequeue();
            if (current == destGrid)
            {
                return ReconstructPath(cameFrom, destGrid);
            }

            foreach (var dir in directions)
            {
                Vector2Int next = current + dir;
                if (!IsWalkable(next)) continue;

                // Stege und Land sind gleich schnell; Wasser ist gar nicht begehbar.
                float stepCost = 1f;
                float newCost = costSoFar[current] + stepCost;
                if (costSoFar.TryGetValue(next, out float existingCost) && newCost >= existingCost)
                {
                    continue;
                }

                costSoFar[next] = newCost;
                cameFrom[next] = current;
                frontier.Enqueue(next, newCost + Heuristic(next, destGrid));
            }
        }

        return empty;
    }

    private static List<Vector2> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int destination)
    {
        List<Vector2> path = new List<Vector2> { destination };
        Vector2Int current = destination;

        while (cameFrom.TryGetValue(current, out Vector2Int previous))
        {
            current = previous;
            path.Add(current);
        }

        path.Reverse();
        if (path.Count > 0)
        {
            path.RemoveAt(0);
        }

        return path;
    }

    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private sealed class PriorityQueue
    {
        private readonly List<(Vector2Int node, float priority)> items = new List<(Vector2Int, float)>();

        public int Count => items.Count;

        public void Enqueue(Vector2Int node, float priority)
        {
            items.Add((node, priority));
        }

        public Vector2Int Dequeue()
        {
            int bestIndex = 0;
            float bestPriority = items[0].priority;
            for (int i = 1; i < items.Count; i++)
            {
                if (items[i].priority < bestPriority)
                {
                    bestPriority = items[i].priority;
                    bestIndex = i;
                }
            }
            Vector2Int result = items[bestIndex].node;
            items.RemoveAt(bestIndex);
            return result;
        }
    }

    static bool IsAdjacentToLand(Vector2Int grid)
    {
        Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        foreach (var d in dirs)
        {
            Vector2Int n = grid + d;
            if (IslandManager.IsLand(new Vector2(n.x, n.y))) return true;
        }
        return false;
    }

    IEnumerable<Steg> GetStegNeighbors(Steg s)
    {
        foreach (var d in Neighbors4(s.GridPosition))
        {
            if (stegGrids.Contains(d))
            {
                foreach (var other in stegs)
                {
                    if (other.GridPosition == d) yield return other;
                }
            }
        }
    }

    private static IEnumerable<Vector2Int> Neighbors4(Vector2Int g)
    {
        yield return g + new Vector2Int(1, 0);
        yield return g + new Vector2Int(-1, 0);
        yield return g + new Vector2Int(0, 1);
        yield return g + new Vector2Int(0, -1);
    }

    private void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code == 2)
        {
            object[] data = (object[])photonEvent.CustomData;
            string buildingName = (string)data[0];
            Vector2 pos = (Vector2)data[1];
            int rotationDegrees = data.Length > 2 ? (int)data[2] : 0;
            int occupiedWidth = data.Length > 3 ? (int)data[3] : -1;
            int occupiedHeight = data.Length > 4 ? (int)data[4] : -1;
            int shipId = data.Length > 5 ? (int)data[5] : 0;

            BuildingData bData = GetBuildingDataByName(buildingName);
            if (bData != null)
            {
                SpawnBuilding(bData, pos, false, rotationDegrees, occupiedWidth, occupiedHeight,
                    shipId > 0 ? shipId : 0);

                // Ressourcen-Nodes unter dem Gebaeude entfernen (war vorher nicht gesynced!)
                int w = occupiedWidth > 0 ? occupiedWidth : bData.width;
                int h = occupiedHeight > 0 ? occupiedHeight : bData.height;
                float startX = -(w - 1) / 2f;
                float startY = -(h - 1) / 2f;
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        IslandManager.RemoveResourceNodeAt(pos + new Vector2(startX + x, startY + y));
                    }
                }
            }
        }
    }

    public BuildingData GetBuildingDataByName(string name)
    {
        BuildingData[] allData = Resources.FindObjectsOfTypeAll<BuildingData>();
        foreach (var d in allData)
        {
            if (d.buildingName == name) return d;
        }

        BuildingData[] resourceData = Resources.LoadAll<BuildingData>("BuildingData");
        foreach (var d in resourceData)
        {
            if (d.buildingName == name) return d;
        }

        return null;
    }

    public BuildingInstance SpawnBuilding(BuildingData data, Vector2 position, bool isLocal = true, int rotationDegrees = 0, int occupiedWidthOverride = -1, int occupiedHeightOverride = -1, int shipId = 0)
    {
        if (data.prefab == null && !data.isWarehouseType) return null;

        GameObject building;
        if (data.prefab != null)
        {
            building = Instantiate(data.prefab, new Vector3(position.x, position.y, -0.21f), Quaternion.identity);
        }
        else
        {
            building = GameObject.CreatePrimitive(PrimitiveType.Quad);
            building.transform.position = new Vector3(position.x, position.y, -0.21f);

            Renderer rend = building.GetComponent<Renderer>();
            if (warehouseMaterial != null)
            {
                rend.material = new Material(warehouseMaterial);
            }
            else
            {
                Shader warehouseShader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
                rend.material = new Material(warehouseShader);
            }

            Texture2D tex = Resources.Load<Texture2D>("warehouse_texture") ?? Resources.Load<Texture2D>("Textures/warehouse_texture");
            if (tex != null)
            {
                rend.material.mainTexture = tex;
            }
            else
            {
                rend.material.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            }

            MeshCollider meshCollider = building.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                DestroyImmediate(meshCollider);
            }
        }

        building.name = data.buildingName;
        building.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);

        int occupiedWidth = occupiedWidthOverride > 0 ? occupiedWidthOverride : data.width;
        int occupiedHeight = occupiedHeightOverride > 0 ? occupiedHeightOverride : data.height;

        // Scale the visual so the texture covers exactly the building's grid footprint.
        // We measure the natural rendered size first (independent of any prefab scale),
        // then compute a factor so the result is exactly data.width × data.height world units.
        building.transform.localScale = Vector3.one; // reset before measuring

        float naturalW = 1f, naturalH = 1f;

        SpriteRenderer buildingSR = building.GetComponentInChildren<SpriteRenderer>();
        if (buildingSR != null && buildingSR.sprite != null)
        {
            // sprite.bounds is in local space at scale 1 – exactly what we need
            naturalW = buildingSR.sprite.bounds.size.x;
            naturalH = buildingSR.sprite.bounds.size.y;
        }
        else
        {
            Renderer buildingRend = building.GetComponentInChildren<Renderer>();
            if (buildingRend != null)
            {
                // bounds at scale (1,1,1) gives the natural world-unit size
                naturalW = buildingRend.bounds.size.x;
                naturalH = buildingRend.bounds.size.y;
            }
        }

        if (naturalW <= 0f) naturalW = 1f;
        if (naturalH <= 0f) naturalH = 1f;

        int rot = Mathf.RoundToInt(Mathf.Repeat(rotationDegrees, 360f));
        float scaleX, scaleY;
        if (rot == 90 || rot == 270)
        {
            scaleX = occupiedHeight / naturalW;
            scaleY = occupiedWidth / naturalH;
        }
        else
        {
            scaleX = occupiedWidth / naturalW;
            scaleY = occupiedHeight / naturalH;
        }

        building.transform.localScale = new Vector3(scaleX, scaleY, 1f);

        // Add building instance logic
        BuildingInstance instance = building.AddComponent<BuildingInstance>();
        instance.data = data;
        instance.isLocal = isLocal;
        instance.footprintWidthOverride = occupiedWidth;
        instance.footprintHeightOverride = occupiedHeight;

        if (data.isWarehouseType)
        {
            Warehouse warehouse = building.GetComponent<Warehouse>();
            if (warehouse == null) warehouse = building.AddComponent<Warehouse>();
            warehouse.isLocal = isLocal;
            warehouse.isMainWarehouse = false;
            warehouse.maxHealth = data.maxHP;
            warehouse.currentHealth = data.maxHP;
            warehouse.storageCapacity = 50;

            if (WarehouseManager.Instance != null)
            {
                WarehouseManager.Instance.RegisterWarehouse(warehouse);
            }
        }

        if (data.placementRule == PlacementRule.Pier)
        {
            Steg steg = building.GetComponent<Steg>();
            if (steg == null)
            {
                steg = building.AddComponent<Steg>();
            }
            steg.Initialize(new Vector3(position.x, position.y, -0.21f));
        }

        if (data.placementRule == PlacementRule.Ship)
        {
            Ship ship = building.GetComponent<Ship>();
            if (ship == null) ship = building.AddComponent<Ship>();
            ship.isLocal = isLocal;
            ship.spawnOrigin = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
            if (data.shipData != null) ship.shipData = data.shipData;
            if (data.buildingName.Contains("Small")) ship.shipLevel = 1;
            else if (data.buildingName.Contains("Medium")) ship.shipLevel = 2;
            else if (data.buildingName.Contains("Large")) ship.shipLevel = 3;

            if (shipId > 0)
            {
                ship.AssignNetId(shipId); // ID kommt vom Besitzer (Event 2 / WorldState)
            }
            else if (isLocal)
            {
                ship.AssignNetId(NetCombat.NextId(NetCombat.LocalActorId)); // Besitzer vergibt selbst
            }

            ship.SyncFromData();
        }

        if (data.isDefenseTower)
        {
            DefenseTower tower = building.GetComponent<DefenseTower>();
            if (tower == null) tower = building.AddComponent<DefenseTower>();
        }

        FogRevealer revealer = building.GetComponent<FogRevealer>();
        if (revealer == null) revealer = building.AddComponent<FogRevealer>();
        revealer.isLocalPlayer = isLocal;
        if (data.fogRevealRadius > 0f) revealer.radius = data.fogRevealRadius;
        if (data.placementRule == PlacementRule.Ship) revealer.radius = Mathf.Max(revealer.radius, 12f);

        // Fog-Gedaechtnis: bleibt als ausgegrauter Snapshot stehen, wenn kein
        // Sichtfeld mehr drauf ist (Gebäude dort werden nicht mehr mitgeupdated).
        FogMemory.Add(building);

        // Apply red tint to enemy buildings
        if (!isLocal)
        {
            Color enemyTint = GameUITheme.EnemyTint;
            foreach (Renderer r in building.GetComponentsInChildren<Renderer>())
            {
                if (r.gameObject.name != "FogMask") // Don't tint the fog mask
                    r.material.color = enemyTint;
            }

            Ship enemyShip = building.GetComponent<Ship>();
            if (enemyShip != null) enemyShip.SetBaseColor(enemyTint);
        }

        RegisterOccupancy(position, occupiedWidth, occupiedHeight);
        return instance;
    }

    public void SpawnMainWarehouse(Vector2 position, bool isLocal = true)
    {
        GameObject warehouse = GameObject.CreatePrimitive(PrimitiveType.Quad);
        warehouse.name = isLocal ? "MyWarehouse" : "EnemyWarehouse";
        warehouse.transform.position = new Vector3(position.x, position.y, -0.21f);
        warehouse.transform.localScale = new Vector3(3f, 3f, 1f); 

        FogRevealer revealer = warehouse.AddComponent<FogRevealer>();
        revealer.radius = 10f;
        revealer.isLocalPlayer = isLocal;

        FogMemory.Add(warehouse);

        Renderer rend = warehouse.GetComponent<Renderer>();

        if (warehouseMaterial != null)
        {
            rend.material = new Material(warehouseMaterial);
        }
        else
        {
            Shader warehouseShader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            if (warehouseShader == null)
            {
                Debug.LogWarning("[BuildingManager] Keine Warehouse-Shader gefunden. Verwende Standardmaterial.");
                rend.material = new Material(Shader.Find("Sprites/Default"));
            }
            else
            {
                rend.material = new Material(warehouseShader);
            }
        }

        Texture2D tex = Resources.Load<Texture2D>("warehouse_texture");
        if (tex == null)
        {
            tex = Resources.Load<Texture2D>("Textures/warehouse_texture");
        }

        if (tex != null)
        {
            rend.material.mainTexture = tex;
        }
        else
        {
            Debug.LogWarning("[BuildingManager] warehouse_texture nicht gefunden. Verwende graues Ersatzmaterial.");
            rend.material.color = new Color(0.75f, 0.75f, 0.75f, 1f);
        }

        if (!isLocal)
        {
            Color enemyColor = GameUITheme.EnemyTint;
            rend.material.color = enemyColor;
        }

        RegisterOccupancy(position, 3, 3);
        var meshCollider = warehouse.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            DestroyImmediate(meshCollider);
        }

        // Add Warehouse component (for island ownership tracking)
        Warehouse wh = warehouse.AddComponent<Warehouse>();
        wh.isLocal = isLocal;
        wh.isMainWarehouse = true;
        wh.storageCapacity = 50;

        BuildingInstance lodging = warehouse.AddComponent<BuildingInstance>();
        lodging.isLocal = isLocal;
        lodging.isPreBuiltLodging = true;
        lodging.displayNameOverride = "Hauptlager";
        lodging.sleepCapacityOverride = 5;
        
        Debug.Log($"[BuildingManager] Spawned main warehouse at {position}, isLocal={isLocal}");
        
        // Force registration with WarehouseManager (in case Start() hasn't run yet)
        if (WarehouseManager.Instance != null)
        {
            WarehouseManager.Instance.RegisterWarehouse(wh);
        }
    }

    // ── Ship Water Pathfinding ───────────────────────────────────────────

    public static List<Vector2> FindWaterPath(Vector2 start, Vector2 destination, int maxIterations = 5000)
    {
        Vector2Int startGrid = new Vector2Int(Mathf.RoundToInt(start.x), Mathf.RoundToInt(start.y));
        Vector2Int destGrid = new Vector2Int(Mathf.RoundToInt(destination.x), Mathf.RoundToInt(destination.y));
        var empty = new List<Vector2>();

        if (startGrid == destGrid)
        {
            empty.Add(destination);
            return empty;
        }

        var frontier = new PriorityQueue();
        frontier.Enqueue(startGrid, 0f);
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var costSoFar = new Dictionary<Vector2Int, float> { [startGrid] = 0f };
        Vector2Int[] dirs = { new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) };
        int iterations = 0;

        while (frontier.Count > 0 && iterations++ < maxIterations)
        {
            Vector2Int current = frontier.Dequeue();
            if (current == destGrid)
                return ReconstructWaterPath(cameFrom, destGrid);

            foreach (var d in dirs)
            {
                Vector2Int next = current + d;
                if (IslandManager.IsLand(next)) continue; // Ships can't go over land
                if (next.x < -200 || next.x > 200 || next.y < -200 || next.y > 200) continue;

                float newCost = costSoFar[current] + 1f;
                if (costSoFar.TryGetValue(next, out float existingCost) && newCost >= existingCost)
                    continue;

                costSoFar[next] = newCost;
                cameFrom[next] = current;
                frontier.Enqueue(next, newCost + Manhattan(next, destGrid));
            }
        }

        return empty;
    }

    private static List<Vector2> ReconstructWaterPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int destination)
    {
        var path = new List<Vector2> { new Vector2(destination.x, destination.y) };
        Vector2Int current = destination;
        while (cameFrom.TryGetValue(current, out Vector2Int prev))
        {
            current = prev;
            path.Add(new Vector2(current.x, current.y));
        }
        path.Reverse();
        if (path.Count > 0) path.RemoveAt(0);
        return path;
    }

    private static float Manhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    // ── Island Flood-Fill (für Fog-Enthüllung) ───────────────────────────

    public static HashSet<Vector2Int> FloodFillIsland(Vector2Int start, int maxCells = 10000)
    {
        var island = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        island.Add(start);
        Vector2Int[] dirs = { new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) };

        while (queue.Count > 0 && island.Count < maxCells)
        {
            Vector2Int current = queue.Dequeue();
            foreach (var d in dirs)
            {
                Vector2Int next = current + d;
                if (!island.Contains(next) && IslandManager.IsLand(next))
                {
                    island.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        return island;
    }
}
