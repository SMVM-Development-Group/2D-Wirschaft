using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class PlacementManager : MonoBehaviour
{
    private struct PlacementPreview
    {
        public Vector2 center;
        public int rotationDegrees;
        public int occupiedWidth;
        public int occupiedHeight;
        public bool valid;
    }

    private struct ShipPlacementCandidate
    {
        public Vector2 center;
        public int rotationDegrees;
        public int occupiedWidth;
        public int occupiedHeight;
    }

    public static PlacementManager Instance;

    [Header("Settings")]
    [SerializeField] private Color canPlaceColor = new Color(0, 1, 0, 0.5f);
    [SerializeField] private Color cannotPlaceColor = new Color(1, 0, 0, 0.5f);

    private GameObject ghostParent;
    private List<Renderer> ghostRenderers = new List<Renderer>();
    private BuildingData currentBuilding;
    private bool isPlacing = false;
    private PlacementPreview currentPreview;

    // Pier drag state
    private bool isDraggingPier = false;
    private Vector2Int pierDragStart;
    private GameObject pierPreviewParent;

    private Camera cam;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        cam = Camera.main;
    }

    public void StartPlacement(BuildingData data)
    {
        if (!ResourceManager.Instance.CanAfford(data.woodCost, data.stoneCost, data.ironCost, data.goldCost))
        {
            Debug.Log($"Cannot afford {data.buildingName}!");
            return;
        }

        currentBuilding = data;
        isPlacing = true;

        if (data.placementRule == PlacementRule.Pier)
            return;

        if (ghostParent != null) Destroy(ghostParent);
        CreateGhost(data);
    }

    private void CreateGhost(BuildingData data)
    {
        ghostParent = new GameObject("PlacementGhost");
        ghostRenderers.Clear();

        GetBasePlacementDimensions(data, out int baseWidth, out int baseHeight);

        // Create a grid of quads representing the footprint
        float startX = -(baseWidth - 1) / 2f;
        float startY = -(baseHeight - 1) / 2f;

        for (int x = 0; x < baseWidth; x++)
        {
            for (int y = 0; y < baseHeight; y++)
            {
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Quad);
                part.transform.SetParent(ghostParent.transform);
                part.transform.localPosition = new Vector3(startX + x, startY + y, -0.2f);
                Destroy(part.GetComponent<MeshCollider>());
                
                Renderer rend = part.GetComponent<Renderer>();
                rend.material = new Material(Shader.Find("Sprites/Default"));
                ghostRenderers.Add(rend);
            }
        }
    }

    private void Update()
    {
        if (!isPlacing || currentBuilding == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -cam.transform.position.z));

        if (currentBuilding.placementRule == PlacementRule.Pier)
        {
            HandlePierPlacement(worldPos);
            return;
        }

        currentPreview = ResolvePreview(worldPos);

        ghostParent.transform.position = new Vector3(currentPreview.center.x, currentPreview.center.y, -0.2f);
        ghostParent.transform.rotation = Quaternion.Euler(0f, 0f, currentPreview.rotationDegrees);

        SetGhostColor(currentPreview.valid ? canPlaceColor : cannotPlaceColor);

        if (Mouse.current.leftButton.wasPressedThisFrame && currentPreview.valid)
        {
            PlaceBuilding(currentPreview);
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelPlacement();
        }
    }

    private PlacementPreview ResolvePreview(Vector3 worldPos)
    {
        if (currentBuilding.placementRule != PlacementRule.Ship)
        {
            GetBasePlacementDimensions(currentBuilding, out int baseWidth, out int baseHeight);
            Vector2 snappedCenter = SnapCenter(worldPos, baseWidth, baseHeight);
            bool valid = CheckPlacementValidity(snappedCenter, 0, baseWidth, baseHeight);
            return new PlacementPreview
            {
                center = snappedCenter,
                rotationDegrees = 0,
                occupiedWidth = baseWidth,
                occupiedHeight = baseHeight,
                valid = valid
            };
        }

        List<ShipPlacementCandidate> candidates = GetShipCandidates(worldPos);
        if (candidates.Count == 0)
        {
            GetBasePlacementDimensions(currentBuilding, out int baseWidth, out int baseHeight);
            Vector2 fallbackCenter = SnapCenter(worldPos, baseWidth, baseHeight);
            return new PlacementPreview
            {
                center = fallbackCenter,
                rotationDegrees = 0,
                occupiedWidth = baseWidth,
                occupiedHeight = baseHeight,
                valid = false
            };
        }

        PlacementPreview bestValid = default;
        PlacementPreview bestAny = default;
        bool foundValid = false;
        bool foundAny = false;

        foreach (ShipPlacementCandidate candidate in candidates)
        {
            bool valid = CheckPlacementValidity(candidate.center, candidate.rotationDegrees, candidate.occupiedWidth, candidate.occupiedHeight);
            PlacementPreview preview = new PlacementPreview
            {
                center = candidate.center,
                rotationDegrees = candidate.rotationDegrees,
                occupiedWidth = candidate.occupiedWidth,
                occupiedHeight = candidate.occupiedHeight,
                valid = valid
            };

            float distance = ((Vector2)worldPos - candidate.center).sqrMagnitude;
            if (!foundAny || distance < (((Vector2)worldPos - bestAny.center).sqrMagnitude))
            {
                bestAny = preview;
                foundAny = true;
            }

            if (valid && (!foundValid || distance < (((Vector2)worldPos - bestValid.center).sqrMagnitude)))
            {
                bestValid = preview;
                foundValid = true;
            }
        }

        return foundValid ? bestValid : bestAny;
    }

private bool CheckIslandOwnership(Vector2 center, int occupiedWidth = 0, int occupiedHeight = 0)
    {
        // Lagerhäuser und Lagerhaus-Typ Gebäude können überall gebaut werden
        if (currentBuilding.canBuildOnOtherIslands || currentBuilding.isWarehouseType)
        {
            if (currentBuilding.isWarehouseType && IsWarehouseOnIsland(new Vector2Int(Mathf.RoundToInt(center.x), Mathf.RoundToInt(center.y))))
            {
                NotificationManager.Instance?.Notify("island_has_warehouse",
                    "Auf dieser Insel steht bereits ein Lagerhaus!", 3f);
                return false;
            }

            // Prüfe ob mindestens 1 Arbeiter auf der Insel ist
            if (!HasWorkerOnIsland(center))
            {
                NotificationManager.Instance?.Notify("island_no_worker",
                    "Du brauchst mindestens 1 Arbeiter auf dieser Insel!", 3f);
                return false;
            }
            return true;
        }

        // Für Piers (Stege): Position ist im Wasser, prüfe ob anliegende Insel ein eigenes Lagerhaus hat
        if (currentBuilding.placementRule == PlacementRule.Pier)
        {
            return CheckPierIslandOwnership(center);
        }

        // Für Schiffe: Prüfe ob neben einem Steg platziert der zu einer Insel mit eigenem Lagerhaus gehört
        if (currentBuilding.placementRule == PlacementRule.Ship)
        {
            return CheckShipIslandOwnership(center, occupiedWidth, occupiedHeight);
        }

        // Für alle anderen Gebäude: Prüfe ob auf eigener Insel
        Vector2Int gridPos = new Vector2Int(Mathf.RoundToInt(center.x), Mathf.RoundToInt(center.y));
        
        // Finde die nächste eigene Insel (Register statt FindObjectsOfType pro Frame)
        if (WarehouseManager.Instance != null)
        {
            foreach (var wh in WarehouseManager.Instance.LocalWarehouses)
            {
                if (wh == null) continue;
                Vector2Int whGrid = new Vector2Int(
                    Mathf.RoundToInt(wh.transform.position.x),
                    Mathf.RoundToInt(wh.transform.position.y)
                );
                if (IsSameIsland(gridPos, whGrid))
                    return true;
            }
        }

        // Auch Lagerhaus-Typ Gebäude als Ankerpunkte (statisches Register)
        for (int i = 0; i < BuildingInstance.AllBuildings.Count; i++)
        {
            BuildingInstance bi = BuildingInstance.AllBuildings[i];
            if (bi != null && bi.isLocal && bi.data != null && bi.data.isWarehouseType)
            {
                Vector2Int biGrid = new Vector2Int(
                    Mathf.RoundToInt(bi.transform.position.x),
                    Mathf.RoundToInt(bi.transform.position.y)
                );
                if (IsSameIsland(gridPos, biGrid))
                    return true;
            }
        }

        NotificationManager.Instance?.Notify("island_no_warehouse",
            "Du brauchst zuerst ein Lagerhaus auf dieser Insel!", 3f);
        return false;
    }

    /// <summary>
    /// Für Schiffe: Prüft ob das Schiff neben einem Steg platziert wird der zu einer Insel mit eigenem Lagerhaus gehört.
    /// Vereinfachte Logik: Finde einen benachbarten Steg und prüfe ob die Kette zum Land mit eigenem Lagerhaus führt.
    /// </summary>
    private bool CheckShipIslandOwnership(Vector2 center, int occupiedWidth, int occupiedHeight)
    {
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        
        // Sammle alle Steg-Positionen und prüfe welche zu Inseln mit Lagerhaus gehören
        List<Vector2Int> allStegPositions = BuildingManager.GetStegPositions();
        if (allStegPositions.Count == 0)
        {
            Debug.Log("Placement Failed: No piers exist yet");
            return false;
        }
        
        // Finde alle Stege die zu einer Insel mit eigenem Lagerhaus gehören
        HashSet<Vector2Int> validStegPositions = new HashSet<Vector2Int>();
        
        // Sammle lokale Lagerhaus-Positionen (Register statt FindObjectsOfType)
        List<Vector2Int> localWarehousePositions = new List<Vector2Int>();
        if (WarehouseManager.Instance != null)
        {
            foreach (var wh in WarehouseManager.Instance.LocalWarehouses)
            {
                if (wh == null) continue;
                localWarehousePositions.Add(new Vector2Int(
                    Mathf.RoundToInt(wh.transform.position.x),
                    Mathf.RoundToInt(wh.transform.position.y)
                ));
            }
        }
        
        if (localWarehousePositions.Count == 0)
        {
            Debug.Log("Placement Failed: No local warehouse found");
            NotificationManager.Instance?.Notify("island_no_warehouse", 
                "Du brauchst zuerst ein Lagerhaus auf dieser Insel!", 3f);
            return false;
        }
        
        // Für jeden Steg: prüfe ob er zu Land gehört das ein Lagerhaus hat
        foreach (Vector2Int stegPos in allStegPositions)
        {
            // Finde Land das neben diesem Steg ist
            foreach (var dir in directions)
            {
                Vector2Int landPos = stegPos + dir;
                if (IslandManager.IsLand(landPos))
                {
                    // Ist dieses Land auf einer Insel mit eigenem Lagerhaus?
                    foreach (Vector2Int whPos in localWarehousePositions)
                    {
                        if (IsSameIsland(landPos, whPos))
                        {
                            validStegPositions.Add(stegPos);
                            break;
                        }
                    }
                }
            }
        }
        
        if (validStegPositions.Count == 0)
        {
            Debug.Log("Placement Failed: No pier found belonging to island with warehouse");
            return false;
        }
        
        // Prüfe ob das Schiff neben einem gültigen Steg platziert wird
        float startX = -(occupiedWidth - 1) / 2f;
        float startY = -(occupiedHeight - 1) / 2f;
        
        for (int x = 0; x < occupiedWidth; x++)
        {
            for (int y = 0; y < occupiedHeight; y++)
            {
                Vector2 cellPos = center + new Vector2(startX + x, startY + y);
                Vector2Int gridCell = new Vector2Int(Mathf.RoundToInt(cellPos.x), Mathf.RoundToInt(cellPos.y));
                
                foreach (var dir in directions)
                {
                    Vector2Int adjacent = gridCell + dir;
                    if (validStegPositions.Contains(adjacent))
                    {
                        // Schiff ist neben einem gültigen Steg!
                        return true;
                    }
                }
            }
        }
        
        Debug.Log("Placement Failed: Ship not adjacent to any valid pier");
        NotificationManager.Instance?.Notify("ship_no_pier", 
            "Schiff muss neben einem Steg platziert werden!", 3f);
        return false;
    }

    /// <summary>
    /// Für Piers: Prüft ob die anliegende Insel ein eigenes Lagerhaus hat.
    /// Folgt der Steg-Kette per BFS bis Land mit Lagerhaus gefunden wird.
    /// </summary>
    private bool CheckPierIslandOwnership(Vector2 center)
    {
        Vector2Int gridPos = new Vector2Int(Mathf.RoundToInt(center.x), Mathf.RoundToInt(center.y));
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(gridPos);
        visited.Add(gridPos);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (var dir in directions)
            {
                Vector2Int neighbor = current + dir;
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);

                if (IslandManager.IsLand(neighbor))
                {
                    if (IsWarehouseOnIsland(neighbor))
                        return true;
                }
                else if (BuildingManager.IsStegAt(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return false;
    }

    private bool IsWarehouseOnIsland(Vector2Int islandPos)
    {
        if (WarehouseManager.Instance != null)
        {
            foreach (var wh in WarehouseManager.Instance.LocalWarehouses)
            {
                if (wh == null) continue;
                Vector2Int whGrid = new Vector2Int(
                    Mathf.RoundToInt(wh.transform.position.x),
                    Mathf.RoundToInt(wh.transform.position.y)
                );
                if (IsSameIsland(islandPos, whGrid))
                    return true;
            }
        }
        for (int i = 0; i < BuildingInstance.AllBuildings.Count; i++)
        {
            BuildingInstance bi = BuildingInstance.AllBuildings[i];
            if (bi != null && bi.isLocal && bi.data != null && bi.data.isWarehouseType)
            {
                Vector2Int biGrid = new Vector2Int(
                    Mathf.RoundToInt(bi.transform.position.x),
                    Mathf.RoundToInt(bi.transform.position.y)
                );
                if (IsSameIsland(islandPos, biGrid))
                    return true;
            }
        }
        return false;
    }

    private bool HasWorkerOnIsland(Vector2 center)
    {
        Vector2Int centerGrid = new Vector2Int(Mathf.RoundToInt(center.x), Mathf.RoundToInt(center.y));
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        if (VillagerManager.Instance == null) return false;
        var villagers = VillagerManager.Instance.ActiveVillagers;
        if (villagers == null) return false;

        foreach (var v in villagers)
        {
            if (v == null || !v.isActiveAndEnabled) continue;
            if (v.role != Villager.Role.Worker) continue;

            Vector2Int vGrid = new Vector2Int(Mathf.RoundToInt(v.transform.position.x), Mathf.RoundToInt(v.transform.position.y));
            if (IsSameIsland(vGrid, centerGrid))
                return true;

            if (!IslandManager.IsLand(vGrid))
            {
                foreach (var dir in directions)
                {
                    Vector2Int adjacent = vGrid + dir;
                    if (IslandManager.IsLand(adjacent) && IsSameIsland(adjacent, centerGrid))
                        return true;
                }
            }
        }
        return false;
    }

    private bool IsSameIsland(Vector2Int pos1, Vector2Int pos2)
    {
        // Beide müssen Land sein
        if (!IslandManager.IsLand(pos1) || !IslandManager.IsLand(pos2))
        {
            return false;
        }

        // Verwende Flood-Fill um zu prüfen ob beide Positionen zur gleichen Insel gehören
        // Starte von pos1 und prüfe ob pos2 erreichbar ist
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(pos1);
        visited.Add(pos1);
        
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        int maxIterations = 10000; // Schutz gegen Endlosschleife
        int iterations = 0;
        
        while (queue.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            Vector2Int current = queue.Dequeue();
            
            // Haben wir pos2 erreicht?
            if (current == pos2)
            {
                return true;
            }
            
            // Alle Nachbarn prüfen
            foreach (var dir in directions)
            {
                Vector2Int next = current + dir;
                if (!visited.Contains(next) && IslandManager.IsLand(next))
                {
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
        }
        
        // pos2 nicht von pos1 aus erreichbar = verschiedene Inseln
        return false;
    }

    private bool CheckPlacementValidity(Vector2 center, int rotationDegrees, int occupiedWidth, int occupiedHeight)
    {
        if (!FogProjector.IsExplored(center))
        {
            Debug.Log("Placement Failed: Area not explored");
            return false;
        }

        // Island ownership check
        if (!CheckIslandOwnership(center, occupiedWidth, occupiedHeight))
        {
            Debug.Log("Placement Failed: Cannot build on this island without a warehouse");
            return false;
        }

        if (currentBuilding.requiredResourceType != ResourceType.None)
        {
            if (IslandManager.GetResourceType(center) != currentBuilding.requiredResourceType)
            {
                Debug.Log($"Placement Failed: Needs {currentBuilding.requiredResourceType}");
                return false;
            }
        }

        float startX = -(occupiedWidth - 1) / 2f;
        float startY = -(occupiedHeight - 1) / 2f;

        for (int x = 0; x < occupiedWidth; x++)
        {
            for (int y = 0; y < occupiedHeight; y++)
            {
                Vector2 pos = center + new Vector2(startX + x, startY + y);
                
                if (BuildingManager.IsOccupied(pos)) 
                {
                    Debug.Log("Placement Failed: Position occupied by another building");
                    return false;
                }

                bool isLand = IslandManager.IsLand(pos);

                if (currentBuilding.placementRule == PlacementRule.LandOnly && !isLand)
                {
                    Debug.Log("Placement Failed: Needs Land");
                    return false;
                }
                
                if (currentBuilding.placementRule == PlacementRule.WaterOnly && isLand)
                {
                    Debug.Log("Placement Failed: Needs Water");
                    return false;
                }
            }
        }

        if (currentBuilding.placementRule == PlacementRule.Pier)
        {
            if (currentBuilding.width != 1 || currentBuilding.height != 1)
            {
                Debug.Log("Placement Failed: Pier pieces must be 1x1");
                return false;
            }

            bool touchesLand = false;
            bool touchesPier = false;
            for (int x = 0; x < currentBuilding.width; x++)
            {
                for (int y = 0; y < currentBuilding.height; y++)
                {
                    Vector2 pos = center + new Vector2(startX + x, startY + y);
                    // Pier cells must be in water (not land, not occupied)
                    if (IslandManager.IsLand(pos) || BuildingManager.IsOccupied(pos))
                    {
                        Debug.Log("Placement Failed: Pier needs to be placed in water");
                        return false;
                    }

                    Vector2Int cell = new Vector2Int(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y));
                    touchesLand |= BuildingManager.HasLandAdjacency(cell);
                    touchesPier |= BuildingManager.HasPierAdjacency(cell);
                }
            }

            if (!touchesLand && !touchesPier)
            {
                Debug.Log("Placement Failed: Standalone pier in water is not allowed");
                return false;
            }
        }

        if (currentBuilding.placementRule == PlacementRule.Ship)
        {
            if (!ValidateShipPlacement(center, rotationDegrees, occupiedWidth, occupiedHeight))
            {
                return false;
            }
        }

        return true;
    }

private bool ValidateShipPlacement(Vector2 center, int rotationDegrees, int occupiedWidth, int occupiedHeight)
    {
        if (currentBuilding.width < 1 || currentBuilding.height < 1)
        {
            return false;
        }

        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        
        // Sammle alle gültigen Steg-Positionen (zu Inseln mit Lagerhaus gehörend)
        List<Vector2Int> localWarehousePositions = new List<Vector2Int>();
        if (WarehouseManager.Instance != null)
        {
            foreach (var wh in WarehouseManager.Instance.LocalWarehouses)
            {
                if (wh == null) continue;
                localWarehousePositions.Add(new Vector2Int(
                    Mathf.RoundToInt(wh.transform.position.x),
                    Mathf.RoundToInt(wh.transform.position.y)
                ));
            }
        }

        List<Vector2Int> allStegPositions = BuildingManager.GetStegPositions();
        HashSet<Vector2Int> stegSet = new HashSet<Vector2Int>(allStegPositions);
        HashSet<Vector2Int> validStegPositions = new HashSet<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        
        // Starte BFS von allen Stegen, die direkt an Land mit Lagerhaus angrenzen
        foreach (Vector2Int stegPos in allStegPositions)
        {
            foreach (var dir in directions)
            {
                Vector2Int landPos = stegPos + dir;
                if (IslandManager.IsLand(landPos))
                {
                    foreach (Vector2Int whPos in localWarehousePositions)
                    {
                        if (IsSameIsland(landPos, whPos))
                        {
                            visited.Add(stegPos);
                            queue.Enqueue(stegPos);
                            validStegPositions.Add(stegPos);
                            break;
                        }
                    }
                    if (validStegPositions.Contains(stegPos)) break;
                }
            }
        }
        
        // BFS: Alle Stege, die über eine Stegkette mit einem gültigen Steg verbunden sind
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (var dir in directions)
            {
                Vector2Int neighbor = current + dir;
                if (visited.Contains(neighbor)) continue;
                if (stegSet.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                    validStegPositions.Add(neighbor);
                }
            }
        }

        // Prüfe ob mindestens eine Zelle des Schiffs neben einem gültigen Steg ist
        float startX = -(occupiedWidth - 1) / 2f;
        float startY = -(occupiedHeight - 1) / 2f;
        
        bool hasValidPierAdjacent = false;
        
        for (int x = 0; x < occupiedWidth; x++)
        {
            for (int y = 0; y < occupiedHeight; y++)
            {
                Vector2 cellPos = center + new Vector2(startX + x, startY + y);
                Vector2Int gridCell = new Vector2Int(Mathf.RoundToInt(cellPos.x), Mathf.RoundToInt(cellPos.y));
                
                foreach (var dir in directions)
                {
                    Vector2Int adjacent = gridCell + dir;
                    if (validStegPositions.Contains(adjacent))
                    {
                        hasValidPierAdjacent = true;
                        break;
                    }
                }
                
                if (hasValidPierAdjacent) break;
            }
            
            if (hasValidPierAdjacent) break;
        }
        
        if (!hasValidPierAdjacent)
        {
            Debug.Log("Placement Failed: Ship not adjacent to valid pier (with warehouse island)");
            return false;
        }

        // Prüfe ob alle Zellen des Schiffs auf Wasser sind
        for (int x = 0; x < occupiedWidth; x++)
        {
            for (int y = 0; y < occupiedHeight; y++)
            {
                Vector2 cellPos = center + new Vector2(startX + x, startY + y);
                Vector2Int gridCell = new Vector2Int(Mathf.RoundToInt(cellPos.x), Mathf.RoundToInt(cellPos.y));
                
                // Schiff muss auf Wasser sein (nicht auf Land)
                if (IslandManager.IsLand(gridCell))
                {
                    Debug.Log("Placement Failed: Ship cannot be placed on land");
                    return false;
                }
            }
        }

        return true;
    }

    private List<Vector2Int> GetPierSideCells(Vector2 center, int occupiedWidth, int occupiedHeight, Vector2Int outward)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        float startX = -(occupiedWidth - 1) / 2f;
        float startY = -(occupiedHeight - 1) / 2f;

        int edgeX = outward.x < 0 ? occupiedWidth - 1 : 0;
        int edgeY = outward.y < 0 ? occupiedHeight - 1 : 0;

        if (outward.x != 0)
        {
            for (int y = 0; y < occupiedHeight; y++)
            {
                Vector2 pos = center + new Vector2(startX + edgeX, startY + y);
                result.Add(new Vector2Int(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y)));
            }
        }
        else
        {
            for (int x = 0; x < occupiedWidth; x++)
            {
                Vector2 pos = center + new Vector2(startX + x, startY + edgeY);
                result.Add(new Vector2Int(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y)));
            }
        }

        return result;
    }

private List<ShipPlacementCandidate> GetShipCandidates(Vector3 worldPos)
    {
        int shipLength = Mathf.Max(currentBuilding.width, currentBuilding.height);
        int shipWidth = Mathf.Min(currentBuilding.width, currentBuilding.height);
        List<ShipPlacementCandidate> candidates = new List<ShipPlacementCandidate>();
        List<Vector2Int> pierCells = BuildingManager.GetStegPositions();
        if (pierCells.Count == 0)
        {
            return candidates;
        }

        Vector2Int mouseGrid = new Vector2Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y));
        int searchRadius = shipLength + shipWidth + 5; // Erhöht für bessere Suche
        HashSet<string> seenKeys = new HashSet<string>();
        Vector2Int[] alongDirections = { Vector2Int.right, Vector2Int.up, Vector2Int.down, Vector2Int.left };
        Vector2Int[] outwardOptions = new Vector2Int[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        // Sammle gültige Steg-Positionen (zu Inseln mit Lagerhaus)
        List<Vector2Int> localWarehousePositions = new List<Vector2Int>();
        if (WarehouseManager.Instance != null)
        {
            foreach (var wh in WarehouseManager.Instance.LocalWarehouses)
            {
                if (wh == null) continue;
                localWarehousePositions.Add(new Vector2Int(
                    Mathf.RoundToInt(wh.transform.position.x),
                    Mathf.RoundToInt(wh.transform.position.y)
                ));
            }
        }
        
        HashSet<Vector2Int> validStegPositions = new HashSet<Vector2Int>();
        Vector2Int[] checkDirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        HashSet<Vector2Int> stegSet = new HashSet<Vector2Int>(pierCells);
        
        foreach (Vector2Int stegPos in pierCells)
        {
            foreach (var dir in checkDirs)
            {
                Vector2Int landPos = stegPos + dir;
                if (IslandManager.IsLand(landPos))
                {
                    foreach (Vector2Int whPos in localWarehousePositions)
                    {
                        if (IsSameIsland(landPos, whPos))
                        {
                            validStegPositions.Add(stegPos);
                            break;
                        }
                    }
                }
            }
        }

        // BFS: Auch Stege weiter draussen in der Kette sind anlegbar, solange sie
        // mit der Landseite (Lagerhaus-Insel) verbunden sind.
        Queue<Vector2Int> chainQueue = new Queue<Vector2Int>();
        foreach (Vector2Int valid in validStegPositions) chainQueue.Enqueue(valid);
        while (chainQueue.Count > 0)
        {
            Vector2Int current = chainQueue.Dequeue();
            foreach (var dir in checkDirs)
            {
                Vector2Int neighbor = current + dir;
                if (validStegPositions.Contains(neighbor)) continue;
                if (!stegSet.Contains(neighbor)) continue;
                validStegPositions.Add(neighbor);
                chainQueue.Enqueue(neighbor);
            }
        }

        foreach (Vector2Int pierCell in pierCells)
        {
            // Nur gültige Stege verwenden
            if (!validStegPositions.Contains(pierCell))
            {
                continue;
            }
            
            if (Mathf.Abs(pierCell.x - mouseGrid.x) > searchRadius || Mathf.Abs(pierCell.y - mouseGrid.y) > searchRadius)
            {
                continue;
            }

            foreach (Vector2Int along in alongDirections)
            {
                // Gerade, durchgehende Steg-Strecke von shipLength Zellen genügt
                // (statt wie bisher: exakt die letzte Segment-Grenze des Stegs).
                if (!IsStraightStegRun(pierCell, along, shipLength))
                {
                    continue;
                }

                // Beide Richtungen ausprobieren (links und rechts vom Pier)
                foreach (Vector2Int outward in outwardOptions)
                {
                    if (outward == along || outward == -along)
                    {
                        continue; // Nur seitliche Richtungen
                    }
                    
                    TryAddShipCandidate(candidates, seenKeys, pierCell, along, outward, shipLength, shipWidth);
                }
            }
        }

        return candidates;
    }

    private Vector2 SnapCenter(Vector3 worldPos, int width, int height)
    {
        float xOffset = (width % 2 == 0) ? 0.5f : 0f;
        float yOffset = (height % 2 == 0) ? 0.5f : 0f;
        float snappedX = Mathf.Round(worldPos.x - xOffset) + xOffset;
        float snappedY = Mathf.Round(worldPos.y - yOffset) + yOffset;
        return new Vector2(snappedX, snappedY);
    }

    private void GetBasePlacementDimensions(BuildingData data, out int width, out int height)
    {
        if (data.placementRule == PlacementRule.Ship)
        {
            width = Mathf.Min(data.width, data.height);
            height = Mathf.Max(data.width, data.height);
            return;
        }

        width = data.width;
        height = data.height;
    }

    private Vector2Int GetShipOutwardDirection(int rotationDegrees)
    {
        switch (Mathf.RoundToInt(Mathf.Repeat(rotationDegrees, 360f)))
        {
            case 0: return Vector2Int.down;
            case 90: return Vector2Int.right;
            case 180: return Vector2Int.up;
            case 270: return Vector2Int.left;
            default: return Vector2Int.down;
        }
    }

    private Vector2Int GetShipLengthDirection(int rotationDegrees)
    {
        switch (Mathf.RoundToInt(Mathf.Repeat(rotationDegrees, 360f)))
        {
            case 0:
            case 180:
                return Vector2Int.up;
            case 90:
            case 270:
                return Vector2Int.right;
            default:
                return Vector2Int.up;
        }
    }

    private void SetGhostColor(Color c)
    {
        foreach (var rend in ghostRenderers)
        {
            rend.material.color = c;
        }
    }

    private void PlaceBuilding(PlacementPreview preview)
    {
        ResourceManager.Instance.SpendResources(currentBuilding.woodCost, currentBuilding.stoneCost, currentBuilding.ironCost, currentBuilding.goldCost);
        BuildingInstance instance = BuildingManager.Instance.SpawnBuilding(currentBuilding, preview.center, true, preview.rotationDegrees, preview.occupiedWidth, preview.occupiedHeight);

        // Schiff-ID an andere Clients weitergeben (damit HP/Sync am richtigen Objekt ankommt)
        int shipId = 0;
        if (instance != null && currentBuilding.placementRule == PlacementRule.Ship)
        {
            Ship shipComp = instance.GetComponent<Ship>();
            if (shipComp != null) shipId = shipComp.ShipNetId;
        }

        if (Photon.Pun.PhotonNetwork.InRoom)
        {
            object[] content = new object[] { currentBuilding.buildingName, preview.center, preview.rotationDegrees, preview.occupiedWidth, preview.occupiedHeight, shipId };
            ExitGames.Client.Photon.SendOptions sendOptions = new ExitGames.Client.Photon.SendOptions { Reliability = true };
            Photon.Pun.PhotonNetwork.RaiseEvent(2, content, new Photon.Realtime.RaiseEventOptions { Receivers = Photon.Realtime.ReceiverGroup.Others }, sendOptions);
        }

        // Remove resource nodes covered by the building footprint
        float startX = -(preview.occupiedWidth - 1) / 2f;
        float startY = -(preview.occupiedHeight - 1) / 2f;
        for (int x = 0; x < preview.occupiedWidth; x++)
        {
            for (int y = 0; y < preview.occupiedHeight; y++)
            {
                Vector2 cellPos = preview.center + new Vector2(startX + x, startY + y);
                IslandManager.RemoveResourceNodeAt(cellPos);
            }
        }

        CancelPlacement();
    }

    // ── Pier drag placement ──────────────────────────────────────────────

    private void HandlePierPlacement(Vector3 worldPos)
    {
        Vector2Int gridPos = new Vector2Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y));

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            isDraggingPier = true;
            pierDragStart = gridPos;
            CreatePierPreview();
            UpdatePierPreview(pierDragStart, gridPos);
        }

        if (isDraggingPier)
        {
            if (Mouse.current.leftButton.isPressed)
                UpdatePierPreview(pierDragStart, gridPos);

            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                BuildPierLine(pierDragStart, gridPos);
                isDraggingPier = false;
                DestroyPierPreview();
                CancelPlacement();
            }
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            isDraggingPier = false;
            DestroyPierPreview();
            CancelPlacement();
        }
    }

    private void CreatePierPreview()
    {
        pierPreviewParent = new GameObject("PierPreview");
    }

    private void DestroyPierPreview()
    {
        if (pierPreviewParent != null)
            Destroy(pierPreviewParent);
        pierPreviewParent = null;
    }

    private void UpdatePierPreview(Vector2Int start, Vector2Int end)
    {
        if (pierPreviewParent == null) return;

        for (int i = pierPreviewParent.transform.childCount - 1; i >= 0; i--)
            Destroy(pierPreviewParent.transform.GetChild(i).gameObject);

        List<Vector2Int> cells = GetLineCells(start, end);
        foreach (Vector2Int cell in cells)
        {
            bool water = !IslandManager.IsLand(cell);
            bool occupied = BuildingManager.IsOccupied(new Vector2(cell.x, cell.y));

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(pierPreviewParent.transform);
            quad.transform.position = new Vector3(cell.x, cell.y, -0.2f);
            Destroy(quad.GetComponent<MeshCollider>());

            Renderer rend = quad.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Sprites/Default"));
            rend.material.color = (water && !occupied) ? canPlaceColor : cannotPlaceColor;
        }
    }

    private List<Vector2Int> GetLineCells(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        int x = start.x, y = start.y;
        int dx = Mathf.Abs(end.x - start.x);
        int dy = -Mathf.Abs(end.y - start.y);
        int sx = start.x < end.x ? 1 : -1;
        int sy = start.y < end.y ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            cells.Add(new Vector2Int(x, y));
            if (x == end.x && y == end.y) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        return cells;
    }

    private void BuildPierLine(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> cells = GetLineCells(start, end);
        int woodPerPier = Mathf.Max(1, currentBuilding.woodCost);
        int totalCost = cells.Count * woodPerPier;

        if (!ResourceManager.Instance.HasResource("holz", totalCost))
        {
            NotificationManager.Instance?.Notify("pier_no_wood",
                $"Nicht genug Holz! Benötigt: {totalCost}", 3f);
            return;
        }

        // Baue alle gültigen Steg-Zellen nacheinander (PlaceStegAt prüft adjacency gegen bereits gesetzte)
        int built = 0;
        foreach (Vector2Int cell in cells)
        {
            if (BuildingManager.Instance.PlaceStegAt(new Vector2(cell.x, cell.y), true, false, currentBuilding))
                built++;
        }

        if (built > 0)
        {
            int spent = built * woodPerPier;
            ResourceManager.Instance.SpendResource("holz", spent);

            // Netzwerk-Event für andere Spieler: sende Start/Ende, remote baut via skipValidation
            if (Photon.Pun.PhotonNetwork.InRoom)
            {
                object[] content = new object[] { (float)start.x, (float)start.y, (float)end.x, (float)end.y };
                ExitGames.Client.Photon.SendOptions sendOptions = new ExitGames.Client.Photon.SendOptions { Reliability = true };
                Photon.Pun.PhotonNetwork.RaiseEvent(18, content, new Photon.Realtime.RaiseEventOptions { Receivers = Photon.Realtime.ReceiverGroup.Others }, sendOptions);
            }
        }
    }

    private void CancelPlacement()
    {
        isPlacing = false;
        isDraggingPier = false;
        DestroyPierPreview();
        if (ghostParent != null)
        {
            ghostParent.SetActive(false);
        }
    }

    private bool IsStraightStegRun(Vector2Int start, Vector2Int along, int shipLength)
    {
        // Genuegt eine durchgehende Steg-Strecke der Schiffslaenge (Algorithmus ist
        // bewusst permissiver als frueher: davor/danach darf weiterer Steg folgen,
        // sonst konnte man an laengeren Stegen nie anlegen).
        for (int i = 0; i < shipLength && i < 16; i++)
        {
            if (!BuildingManager.IsStegAt(start + along * i))
            {
                return false;
            }
        }

        return true;
    }

    private void TryAddShipCandidate(List<ShipPlacementCandidate> candidates, HashSet<string> seenKeys, Vector2Int segmentStart, Vector2Int along, Vector2Int outward, int shipLength, int shipWidth)
    {
        List<Vector2Int> footprintCells = new List<Vector2Int>();
        for (int lengthIndex = 0; lengthIndex < shipLength; lengthIndex++)
        {
            Vector2Int pierCell = segmentStart + along * lengthIndex;
            for (int widthIndex = 0; widthIndex < shipWidth; widthIndex++)
            {
                footprintCells.Add(pierCell + outward * (widthIndex + 1));
            }
        }

        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        foreach (Vector2Int cell in footprintCells)
        {
            minX = Mathf.Min(minX, cell.x);
            maxX = Mathf.Max(maxX, cell.x);
            minY = Mathf.Min(minY, cell.y);
            maxY = Mathf.Max(maxY, cell.y);
        }

        int occupiedWidth = maxX - minX + 1;
        int occupiedHeight = maxY - minY + 1;
        Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        int rotationDegrees = GetShipRotationFromDirections(along, outward);

        string key = $"{center.x:F1}_{center.y:F1}_{rotationDegrees}_{occupiedWidth}_{occupiedHeight}";
        if (!seenKeys.Add(key))
        {
            return;
        }

        candidates.Add(new ShipPlacementCandidate
        {
            center = center,
            rotationDegrees = rotationDegrees,
            occupiedWidth = occupiedWidth,
            occupiedHeight = occupiedHeight
        });
    }

    private List<Vector2Int> SortCellsAlongDirection(List<Vector2Int> cells, Vector2Int along)
    {
        cells.Sort((a, b) =>
        {
            int aValue = along.x != 0 ? a.x : a.y;
            int bValue = along.x != 0 ? b.x : b.y;
            if (aValue != bValue)
            {
                return aValue.CompareTo(bValue);
            }

            int aSecondary = along.x != 0 ? a.y : a.x;
            int bSecondary = along.x != 0 ? b.y : b.x;
            return aSecondary.CompareTo(bSecondary);
        });

        return cells;
    }

    private int GetShipRotationFromDirections(Vector2Int along, Vector2Int outward)
    {
        // Die Textur-Spitze zeigt standardmäßig nach unten (-Y).
        // Die Spitze soll parallel zum Schiff (along-Richtung) zeigen,
        // nicht zum Steg hin (outward).
        // Bei Rotation 0: tip = -transform.up = (0,-1) → unten
        // Bei Rotation 90: tip = -transform.up = (1,0) → rechts
        // Bei Rotation 180: tip = -transform.up = (0,1) → oben
        // Bei Rotation 270: tip = -transform.up = (-1,0) → links
        if (along == Vector2Int.up) return 180;
        if (along == Vector2Int.down) return 0;
        if (along == Vector2Int.right) return 90;
        if (along == Vector2Int.left) return 270;
        return 0;
    }
}
