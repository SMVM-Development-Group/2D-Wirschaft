using UnityEngine;
using System.Collections.Generic;
using Photon.Pun;

public class Ship : MonoBehaviour
{
    public static readonly List<Ship> ActiveShips = new List<Ship>();

    [Header("Ship Configuration")]
    public ShipData shipData;
    public int shipLevel = 1;
    public bool isLocal = true;

    [Header("Multiplayer Sync")]
    public Vector2Int spawnOrigin;
    private int shipNetId;      // 0 = nicht initialisiert

    // Belegte Grid-Zellen: damit die Reservierung dem Schiff folgt und beim
    // Ablegen/Untergehen wieder frei wird (sonst bleibt der Liegeplatz fuer
    // immer blockiert und man kann dort kein weiteres Schiff bauen).
    private List<Vector2Int> occupiedCells = new List<Vector2Int>();
    private Vector2 occupancyCenter;
    private int footprintWidth = 1;
    private int footprintHeight = 1;
    private bool hasOccupancy;

    [Header("Cargo System")]
    public List<ShipSlot> slots = new List<ShipSlot>();

    [Header("Movement")]
    public float moveSpeed = 2f;
    public float rotationSpeed = 180f;
    private Vector3 targetPosition;
    private bool isMoving = false;
    private float currentAngle = 0f;
    private List<Vector2> waterPath = new List<Vector2>();
    private int waterPathIndex = 0;

    [Header("Health")]
    public int maxHP = 180;
    public int currentHP = 180;

    [Header("Crew")]
    public Villager assignedCrew;

    private float syncTimer = 0f;
    private const float SyncInterval = 0.15f;

    // Fog: nur beim Wechsel der Grid-Zelle aktualisieren
    private Vector2Int lastFogCell;
    private bool hasFogCell;

    [Header("Visual")]
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    public Color selectedColor = new Color(0.3f, 0.85f, 1f, 1f);

    private bool isSelected = false;

    // Events
    public System.Action<Ship> OnShipClicked;
    public System.Action OnCargoChanged;

    private int lastBroadcastCargoUsed = -1;

    public int ShipNetId => shipNetId;

    public bool IsAuthority => isLocal;

    // Basisfarbe (tintet fremde Schiffe rot – von BuildingManager gesetzt)
    [HideInInspector] public Color baseColor;
    private bool hasBaseColor;

    public void SetBaseColor(Color c)
    {
        baseColor = c;
        hasBaseColor = true;
        originalColor = c;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        currentAngle = transform.rotation.eulerAngles.z;
        lastFogCell = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        hasFogCell = true;

        // Fog-Gedaechtnis: Schiffe bleiben als ausgegrauter Snapshot stehen, wenn
        // sie das Sichtfeld verlassen (bewegt sich nicht mehr mit).
        FogMemory.Add(gameObject);
    }

    private void OnEnable()
    {
        ActiveShips.Add(this);
        if (shipNetId != 0) NetCombat.RegisterShip(this);
    }

    private void OnDisable()
    {
        ActiveShips.Remove(this);
        if (shipNetId != 0) NetCombat.UnregisterShip(this);
    }

    public void AssignNetId(int id)
    {
        if (id > 0)
        {
            shipNetId = id;
            NetCombat.RegisterShip(this);
        }
    }

    public void SyncFromData()
    {
        if (shipData != null)
        {
            moveSpeed = shipData.moveSpeed;
            rotationSpeed = shipData.rotationSpeed;
            shipLevel = shipData.level;
            maxHP = shipData.level switch
            {
                1 => 180,
                2 => 350,
                3 => 600,
                _ => 180
            };
        }
        currentHP = Mathf.Max(1, maxHP);
        InitializeSlots();
    }

    /// <summary>
    /// Merkt sich die beim Spawn bereits registrierten Belegungszellen, damit
    /// sie beim Ablegen bzw. Untergehen wieder freigegeben werden koennen.
    /// </summary>
    public void InitializeOccupancy(Vector2 center, int width, int height)
    {
        footprintWidth = Mathf.Max(1, width);
        footprintHeight = Mathf.Max(1, height);
        occupancyCenter = center;
        occupiedCells = BuildingManager.GetFootprintCells(center, footprintWidth, footprintHeight);
        hasOccupancy = true;
    }

    private Vector2 SnapToFootprintCenter(Vector3 position)
    {
        float xOffset = (footprintWidth % 2 == 0) ? 0.5f : 0f;
        float yOffset = (footprintHeight % 2 == 0) ? 0.5f : 0f;
        return new Vector2(
            Mathf.Round(position.x - xOffset) + xOffset,
            Mathf.Round(position.y - yOffset) + yOffset);
    }

    /// <summary>Verschiebt die Belegung mit dem Schiff (nur wenn sich die Grid-Zelle aendert).</summary>
    private void UpdateOccupancy()
    {
        if (!hasOccupancy) return;

        Vector2 snapped = SnapToFootprintCenter(transform.position);
        if ((snapped - occupancyCenter).sqrMagnitude < 0.0001f) return;

        BuildingManager.FreeOccupancyCells(occupiedCells);
        occupancyCenter = snapped;
        occupiedCells = BuildingManager.GetFootprintCells(snapped, footprintWidth, footprintHeight);
        BuildingManager.RegisterOccupancyCells(occupiedCells);
    }

    private void ReleaseOccupancy()
    {
        if (!hasOccupancy) return;
        BuildingManager.FreeOccupancyCells(occupiedCells);
        occupiedCells.Clear();
        hasOccupancy = false;
    }

    private void InitializeSlots()
    {
        slots.Clear();
        int count = shipData != null ? shipData.GetSlotCount() : GetSlotCountForLevel();
        for (int i = 0; i < count; i++)
            slots.Add(new ShipSlot());
    }

    private int GetSlotCountForLevel()
    {
        return shipLevel switch
        {
            1 => 8,
            2 => 16,
            3 => 30,
            _ => 8
        };
    }

    private void Start()
    {
        SyncFromData();

        var collider = GetComponent<Collider2D>();
        if (collider == null)
        {
            var bc = gameObject.AddComponent<BoxCollider2D>();
            bc.size = new Vector2(1.5f, 1.5f);
        }

        OnCargoChanged += UpdateVisualState;

        if (shipNetId == 0 && isLocal)
        {
            // Nachtraeglich (alte Spawnpfade): lokale Schiffe vergeben selbst eine ID.
            shipNetId = NetCombat.NextId(NetCombat.LocalActorId);
        }
        if (shipNetId != 0)
        {
            NetCombat.RegisterShip(this);
        }

        // Besatzung nur vom BESITZER vergeben (kein Zweit-Crew auf anderen Clients!)
        if (isLocal && !HasCrew() && VillagerManager.Instance != null)
        {
            AssignFirstAvailableVillager();
        }
    }

    private void UpdateVisualState()
    {
        if (spriteRenderer != null)
        {
            int used = 0;
            for (int i = 0; i < slots.Count; i++) if (!slots[i].IsEmpty) used++;
            int total = slots.Count;
            float ratio = total > 0 ? (float)used / total : 0f;
            spriteRenderer.color = Color.Lerp(originalColor, selectedColor, ratio * 0.3f);
        }
    }

    private void Update()
    {
        HandleMovement();
        UpdateOccupancy();

        if (isLocal)
        {
            HandleClickDetection();
            BroadcastPosition();
        }
        else
        {
            InterpolateRemoteState();
        }
    }

    // ── Netz-Sync (Event 14) ──────────────────────────────────────────────
    private Vector3 remoteTargetPosition;
    private float remoteTargetRot;
    private int remoteCargoUsed;
    private bool remoteMoving;
    private bool hasRemoteState;

    private const byte NetFlagMoving = 1;

    public void ApplyNetState(object[] data)
    {
        if (data == null || data.Length < 5) return;
        // { shipId, x, y, rot, flags, cargoUsed }
        float nx = (float)data[1];
        float ny = (float)data[2];
        remoteTargetRot = (float)data[3];
        float flags = (float)data[4];
        remoteMoving = flags >= 0.5f;
        remoteCargoUsed = data.Length > 5 ? (int)data[5] : -1;

        remoteTargetPosition = new Vector3(nx, ny, transform.position.z);
        hasRemoteState = true;

        if (remoteCargoUsed >= 0 && remoteCargoUsed != remoteCargoAppliedCount)
        {
            remoteCargoAppliedCount = remoteCargoUsed;
            VisualizeCargoUsed(remoteCargoUsed);
        }
    }

    private int remoteCargoAppliedCount = -1;

    private void InterpolateRemoteState()
    {
        if (!hasRemoteState) return;

        float distance = Vector2.Distance(transform.position, remoteTargetPosition);
        if (distance > 4f)
        {
            transform.position = remoteTargetPosition;
            currentAngle = remoteTargetRot;
        }
        else if (distance > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, remoteTargetPosition,
                moveSpeed * 1.6f * Time.deltaTime);

            float angleDiff = Mathf.DeltaAngle(currentAngle, remoteTargetRot);
            float rotationStep = rotationSpeed * 1.4f * Time.deltaTime;
            if (Mathf.Abs(angleDiff) < rotationStep)
                currentAngle = remoteTargetRot;
            else
                currentAngle += Mathf.Sign(angleDiff) * rotationStep;
        }
        else if (!remoteMoving && Vector2.Distance(transform.position, remoteTargetPosition) > 0.01f)
        {
            transform.position = remoteTargetPosition;
            currentAngle = remoteTargetRot;
        }

        transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
    }

    private void VisualizeCargoUsed(int used)
    {
        if (spriteRenderer == null) return;
        if (isSelected) return;
        Color baseCol = hasBaseColor ? baseColor : originalColor;
        int total = slots.Count > 0 ? slots.Count : 8;
        float ratio = total > 0 ? Mathf.Clamp01((float)used / total) : 0f;
        spriteRenderer.color = Color.Lerp(baseCol, selectedColor, ratio * 0.3f);
    }

    private void BroadcastPosition()
    {
        if (!PhotonNetwork.InRoom) return;
        if (!isMoving) return;

        syncTimer += Time.deltaTime;
        if (syncTimer < SyncInterval) return;
        syncTimer = 0f;

        SendSyncEvent(NetFlagMoving);
    }

    private void SendSyncEvent(float movingFlag)
    {
        object[] data = new object[]
        {
            shipNetId,
            transform.position.x,
            transform.position.y,
            transform.rotation.eulerAngles.z,
            movingFlag,
            GetUsedSlotCount()
        };
        NetCombat.Raise(14, data, false);
    }

    // ── Bewegung ─────────────────────────────────────────────────────────
    private const float WaypointReachedDistance = 0.35f;
    private float waypointBestDistance = float.MaxValue;
    private int collisionRepaths = 0;
    private const int MaxCollisionRepaths = 3;

    private void HandleMovement()
    {
        if (!isLocal) return; // nur der BESITZER simuliert die Fahrt

        if (!isMoving) return;

        // Fallback: Schiff steht auf Land -> sofort anhalten statt erst nach
        // ein paar Sekunden Fahrt durch die Insel.
        if (IsOnLand())
        {
            StopMovement();
            NotificationManager.Instance?.Notify("ship_stranded",
                "Schiff gestrandet! Bitte auf Wasser setzen.", 5f);
            return;
        }

        Vector3 target = GetCurrentWaypoint();
        Vector3 direction = target - transform.position;
        direction.z = 0f;
        float distance = direction.magnitude;

        // Wegpunkt erreicht? Zusaetzlich "passiert" erkennen: bei begrenzter
        // Drehrate kann ein Schiff sonst ewig um einen Punkt kreisen, den es
        // wegen seines Wendekreises nie exakt erreicht.
        if (distance <= WaypointReachedDistance ||
            (distance > waypointBestDistance && distance < 1.5f))
        {
            AdvanceWaypoint();
            return;
        }
        waypointBestDistance = Mathf.Min(waypointBestDistance, distance);

        currentAngle = RotateTowards(direction);
        transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);

        // Entlang der Richtung zum Wegpunkt fahren (nicht entlang transform.up):
        // sonst zieht die begrenzte Drehrate das Schiff in eine Kreisbahn um den
        // Wegpunkt. Die Drehung ist rein visuell, die Position folgt dem Kurs.
        float desiredAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        float alignment = Mathf.Clamp01(Mathf.Cos(Mathf.DeltaAngle(currentAngle, desiredAngle) * Mathf.Deg2Rad));
        float speedFactor = Mathf.Lerp(0.15f, 1f, alignment);
        float step = Mathf.Min(moveSpeed * speedFactor * Time.deltaTime, distance);
        Vector3 nextPosition = transform.position + direction / distance * step;

        // Landkollision: keinen Schritt auf eine Landzelle machen. Das greift
        // auch, wenn der (geglaettete) Pfad durch Drift neben der Ideallinie
        // liegt oder gar kein Wasserpfad gefunden wurde.
        if (!IsPositionAllowed(nextPosition))
        {
            // Erst versuchen, ab der aktuellen Position neu zu planen und um
            // die Insel herumzufahren. Klappt das nicht, anhalten statt
            // durch Land zu fahren.
            if (TryRepathFromCurrent())
                return;

            StopMovement();
            NotificationManager.Instance?.Notify("ship_stranded", "Schiff gestrandet! Kein Wasserweg.", 5f);
            return;
        }

        transform.position = nextPosition;

        RevealFogAlongPath();
    }

    /// <summary>
    /// Darf das Schiff an diese Position? Nur Wasser - Land ist fuer Schiffe
    /// grundsaetzlich nicht befahrbar.
    /// </summary>
    private bool IsPositionAllowed(Vector3 position)
    {
        Vector2Int cell = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
        return !IslandManager.IsLand(cell);
    }

    /// <summary>
    /// Plant den Wasserweg ab der aktuellen Position neu, wenn die aktuelle
    /// Ideallinie an Land stossen wuerde. So faehrt das Schiff um die Insel
    /// herum, anstatt einfach dagegen zu fahren.
    /// </summary>
    private bool TryRepathFromCurrent()
    {
        if (collisionRepaths >= MaxCollisionRepaths) return false;
        if (IsOnLand()) return false;

        List<Vector2> path = BuildingManager.FindWaterPath(transform.position, targetPosition);
        path = SmoothWaterPath(transform.position, path);
        if (path.Count == 0) return false;

        collisionRepaths++;
        waterPath = path;
        waterPathIndex = 0;
        waypointBestDistance = float.MaxValue;
        return true;
    }

    private Vector3 GetCurrentWaypoint()
    {
        if (waterPathIndex < waterPath.Count)
        {
            Vector2 point = waterPath[waterPathIndex];
            return new Vector3(point.x, point.y, transform.position.z);
        }
        return targetPosition;
    }

    private void AdvanceWaypoint()
    {
        waypointBestDistance = float.MaxValue;
        collisionRepaths = 0;

        if (waterPathIndex < waterPath.Count)
        {
            waterPathIndex++;
            if (waterPathIndex < waterPath.Count) return;
        }

        // Letzte Annäherung an den exakten Klickpunkt (kann vom gerundeten
        // Wegpunkt abweichen).
        Vector3 end = new Vector3(targetPosition.x, targetPosition.y, transform.position.z);
        if (Vector2.Distance(transform.position, end) > WaypointReachedDistance) return;

        StopMovement();
    }

    private float RotateTowards(Vector3 direction)
    {
        float targetRot = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        float angleDiff = Mathf.DeltaAngle(currentAngle, targetRot);
        float rotationStep = rotationSpeed * Time.deltaTime;

        if (Mathf.Abs(angleDiff) < rotationStep)
            return targetRot;
        return currentAngle + Mathf.Sign(angleDiff) * rotationStep;
    }

    private bool IsOnLand()
    {
        Vector2Int grid = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        return IslandManager.IsLand(grid);
    }

    public void MoveTo(Vector3 destination)
    {
        if (!isLocal) return; // nur der Besitzer steuert sein Schiff

        if (!HasCrew())
        {
            AssignFirstAvailableVillager();
        }

        if (!HasCrew())
        {
            Debug.Log("[Ship] Cannot move - no villager assigned as crew!");
            NotificationManager.Instance?.Notify("ship_no_crew", "Ein Schiff braucht einen Dorfbewohner um zu fahren!", 5f);
            return;
        }

        Vector2Int destGrid = new Vector2Int(Mathf.RoundToInt(destination.x), Mathf.RoundToInt(destination.y));

        if (IslandManager.IsLand(destGrid))
        {
            destination = FindNearestWater(destination);
        }

        targetPosition = new Vector3(destination.x, destination.y, transform.position.z);

        // Gestrandet? Dann das Schiff zuerst ans naechste Ufer setzen. Es darf
        // auf keinen Fall sichtbar ueber Land fahren - genau dadurch landete es
        // frueher ein paar Bloecke tief auf der Insel und blieb haengen.
        if (IsOnLand())
        {
            List<Vector2> escape = FindLandEscapePath(transform.position);
            if (escape == null || escape.Count == 0)
            {
                isMoving = false;
                NotificationManager.Instance?.Notify("ship_stranded",
                    "Schiff gestrandet! Kein Wasserweg erreichbar.", 5f);
                return;
            }

            Vector2 shore = escape[escape.Count - 1];
            transform.position = new Vector3(shore.x, shore.y, transform.position.z);
            UpdateOccupancy();
        }

        Vector3 pathStart = transform.position;
        waterPath = BuildingManager.FindWaterPath(pathStart, destination);
        waterPath = SmoothWaterPath(pathStart, waterPath);
        waterPathIndex = 0;
        waypointBestDistance = float.MaxValue;
        collisionRepaths = 0;

        if (waterPath.Count == 0)
        {
            // Kein Wasserweg: Auf keinen Fall geradeaus fahren (das waere der
            // Weg quer ueber die Insel).
            isMoving = false;
            NotificationManager.Instance?.Notify("ship_stranded",
                "Kein Wasserweg zum Ziel!", 5f);
            return;
        }

        isMoving = true;
    }

    /// <summary>
    /// Kuerzester Zellpfad von einer (gestrandeten) Landposition bis zur ersten
    /// Wasserzelle. Der letzte Punkt ist immer Wasser. Null, wenn im Suchradius
    /// kein Wasser erreichbar ist.
    /// </summary>
    private static List<Vector2> FindLandEscapePath(Vector2 from, int maxExpansions = 20000)
    {
        Vector2Int start = new Vector2Int(Mathf.RoundToInt(from.x), Mathf.RoundToInt(from.y));
        if (!IslandManager.IsLand(start)) return new List<Vector2>();

        var queue = new Queue<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        queue.Enqueue(start);
        cameFrom[start] = start;

        Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        int limit = BuildingManager.MapCellLimit;
        int expansions = 0;

        while (queue.Count > 0 && expansions++ < maxExpansions)
        {
            Vector2Int current = queue.Dequeue();
            foreach (var d in dirs)
            {
                Vector2Int next = current + d;
                if (cameFrom.ContainsKey(next)) continue;
                if (next.x < -limit || next.x > limit || next.y < -limit || next.y > limit) continue;

                cameFrom[next] = current;

                if (!IslandManager.IsLand(next))
                {
                    var path = new List<Vector2>();
                    Vector2Int step = next;
                    while (step != start)
                    {
                        path.Add(new Vector2(step.x, step.y));
                        step = cameFrom[step];
                    }
                    path.Reverse();
                    return path;
                }

                queue.Enqueue(next);
            }
        }

        return null;
    }

    /// <summary>
    /// Entfernt Zickzack-Wegpunkte, wenn eine direkte Wasserlinie frei ist.
    /// Das Schiff faehrt dadurch ruhige, weitere Boegen statt jede Grid-Zelle
    /// einzeln anzusteuern (weniger hektisches Drehen).
    /// </summary>
    private static List<Vector2> SmoothWaterPath(Vector3 start, List<Vector2> path)
    {
        if (path == null || path.Count <= 1) return path ?? new List<Vector2>();

        var smoothed = new List<Vector2>(path.Count);
        Vector2 anchor = start;
        int index = 0;

        while (index < path.Count)
        {
            int next = index;
            // Nur begrenzt weit vorausschauen: haelt die Glaettung auch bei
            // sehr langen Wegen guenstig.
            int farthest = Mathf.Min(path.Count - 1, index + 40);
            for (int candidate = farthest; candidate > index; candidate--)
            {
                if (HasWaterLineOfSight(anchor, path[candidate]))
                {
                    next = candidate;
                    break;
                }
            }

            smoothed.Add(path[next]);
            anchor = path[next];
            index = next + 1;
        }

        return smoothed;
    }

    private static bool HasWaterLineOfSight(Vector2 from, Vector2 to)
    {
        float distance = Vector2.Distance(from, to);
        if (distance < 0.001f) return true;

        int samples = Mathf.CeilToInt(distance / 0.2f);
        for (int i = 1; i <= samples; i++)
        {
            Vector2 point = Vector2.Lerp(from, to, (float)i / samples);
            if (TouchesLand(point)) return false;
        }
        return true;
    }

    /// <summary>
    /// Prueft die Zelle an einem Punkt inklusive angrenzender Zellen an
    /// Bruchkanten. Verhindert, dass eine geglaettete Linie diagonal ueber
    /// eine Landecke schneidet.
    /// </summary>
    private static bool TouchesLand(Vector2 point)
    {
        int x0 = Mathf.FloorToInt(point.x), x1 = Mathf.CeilToInt(point.x);
        int y0 = Mathf.FloorToInt(point.y), y1 = Mathf.CeilToInt(point.y);
        return IslandManager.IsLand(new Vector2(x0, y0)) ||
               IslandManager.IsLand(new Vector2(x0, y1)) ||
               IslandManager.IsLand(new Vector2(x1, y0)) ||
               IslandManager.IsLand(new Vector2(x1, y1));
    }

    private Vector3 FindNearestWater(Vector3 landPos)
    {
        // Naechstes Ufer ueber den kuerzesten Landweg suchen. Die alte
        // Ring-Suche brach nach 20 Zellen ab und lieferte bei grossen Inseln
        // wieder eine Landzelle - daraus wurde ein leerer Wasserpfad und das
        // Schiff fuhr geradeaus weiter.
        List<Vector2> escape = FindLandEscapePath(landPos);
        if (escape != null && escape.Count > 0)
        {
            Vector2 shore = escape[escape.Count - 1];
            return new Vector3(shore.x, shore.y, landPos.z);
        }

        return landPos;
    }

    public void AssignFirstAvailableVillager()
    {
        if (VillagerManager.Instance == null) return;

        Villager v = VillagerManager.Instance.GetAvailableVillager();
        if (v != null)
        {
            AssignCrew(v);
        }
    }

    private void StopMovement()
    {
        isMoving = false;
        RevealAreaAroundShip();

        if (isLocal)
        {
            // Stopsignal an alle: Bewegungs-Flag 0 + Endposition
            if (PhotonNetwork.InRoom)
            {
                SendSyncEvent(0f);
            }
        }
    }

    public void StopMovementCommand()
    {
        isMoving = false;
        if (isLocal && PhotonNetwork.InRoom)
        {
            SendSyncEvent(0f);
        }
    }

    private void RevealFogAlongPath()
    {
        Vector2Int cell = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        if (!hasFogCell || cell != lastFogCell)
        {
            lastFogCell = cell;
            hasFogCell = true;
            FogProjector.RegisterExploration(transform.position, 12f);
        }
    }

    private void RevealAreaAroundShip()
    {
        FogProjector.RegisterExploration(transform.position, 12f);
    }

    // ── Klick / Auswahl ──────────────────────────────────────────────────
    private void HandleClickDetection()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || Camera.main == null) return;

        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
        Collider2D hit = Physics2D.OverlapPoint(mouseWorldPos);

        if (hit != null && hit.gameObject == gameObject)
        {
            if (UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.shiftKey.isPressed)
                {
                    ToggleSelection();
                }
                else
                {
                    SelectShip();
                }
            }
        }
    }

    private void SelectShip()
    {
        for (int i = 0; i < ActiveShips.Count; i++)
        {
            Ship s = ActiveShips[i];
            if (s != null)
            {
                s.isSelected = false;
                s.UpdateVisualSelection();
            }
        }

        isSelected = true;
        UpdateVisualSelection();

        ShipCargoUI cargoUI = ShipCargoUI.Instance;
        if (cargoUI == null)
        {
            cargoUI = FindAnyObjectByType<ShipCargoUI>();
        }
        if (cargoUI == null)
        {
            GameObject go = new GameObject("ShipCargoUI");
            cargoUI = go.AddComponent<ShipCargoUI>();
        }
        cargoUI.ShowShipUI(this);
    }

    private void ToggleSelection()
    {
        isSelected = !isSelected;
        UpdateVisualSelection();

        if (isSelected)
        {
            OnShipClicked?.Invoke(this);
        }
    }

    private void UpdateVisualSelection()
    {
        if (spriteRenderer == null) return;

        if (isSelected)
        {
            spriteRenderer.color = selectedColor;
        }
        else
        {
            VisualizeCargoUsed(GetUsedSlotCount());
        }
    }

    // ── Cargo System (Slot-basiert) ──────────────────────────────────────

    public bool HasCrew()
    {
        return assignedCrew != null;
    }

    public bool AssignCrew(Villager villager)
    {
        if (assignedCrew != null) return false;

        assignedCrew = villager;
        villager.AssignToShip(this);
        return true;
    }

    public void ReleaseCrew()
    {
        if (assignedCrew != null)
        {
            assignedCrew.ReleaseFromShip();
            assignedCrew = null;
        }
    }

    // ── Slot Management ───────────────────────────────────────────────────

    public int GetSlotCount() => slots.Count;

    public int GetUsedSlotCount()
    {
        int used = 0;
        for (int i = 0; i < slots.Count; i++) if (!slots[i].IsEmpty) used++;
        return used;
    }

    public int GetFreeSlotCount()
    {
        int free = 0;
        for (int i = 0; i < slots.Count; i++) if (slots[i].IsEmpty) free++;
        return free;
    }

    public bool HasFreeSlot() => GetFreeSlotCount() > 0;

    public int GetCargoCount()
    {
        int count = 0;
        for (int i = 0; i < slots.Count; i++) if (slots[i].content == ShipSlot.SlotContent.Material) count++;
        return count;
    }

    public int GetCargoCapacity() => slots.Count;

    public int GetFreeCargoSpace() => GetFreeSlotCount();

    public bool CanAddCargo() => HasFreeSlot();

    public List<ShipSlot> GetAllSlots()
    {
        var result = new List<ShipSlot>(slots.Count);
        for (int i = 0; i < slots.Count; i++) result.Add(slots[i]);
        return result;
    }

    public Dictionary<string, int> GetAllCargoGrouped()
    {
        var grouped = new Dictionary<string, int>();
        foreach (var slot in slots)
        {
            if (slot.content == ShipSlot.SlotContent.Material && !string.IsNullOrEmpty(slot.resourceId))
            {
                if (!grouped.ContainsKey(slot.resourceId)) grouped[slot.resourceId] = 0;
                grouped[slot.resourceId] += slot.amount;
            }
        }
        return grouped;
    }

    public List<string> GetAllCargo()
    {
        var list = new List<string>();
        foreach (var slot in slots)
        {
            if (slot.content == ShipSlot.SlotContent.Material && !string.IsNullOrEmpty(slot.resourceId))
            {
                for (int i = 0; i < slot.amount; i++)
                    list.Add(slot.resourceId);
            }
        }
        return list;
    }

    // ── Loading ───────────────────────────────────────────────────────────

    public bool TryLoadMaterial(string resourceId, int amount)
    {
        if (!isLocal) return false; // nur der Besitzer belaedt seine Schiffe

        int remaining = amount;

        // Erst vorhandene Slots mit gleichem Material auffuellen (max 16)
        foreach (var slot in slots)
        {
            if (slot.content == ShipSlot.SlotContent.Material && slot.resourceId == resourceId && slot.amount < 16)
            {
                int canAdd = Mathf.Min(remaining, 16 - slot.amount);
                slot.amount += canAdd;
                remaining -= canAdd;
                if (remaining <= 0)
                {
                    OnCargoChanged?.Invoke();
                    BroadcastCargoState();
                    return true;
                }
            }
        }

        foreach (var slot in slots)
        {
            if (slot.IsEmpty)
            {
                int toLoad = Mathf.Min(remaining, 16);
                slot.content = ShipSlot.SlotContent.Material;
                slot.resourceId = resourceId;
                slot.amount = toLoad;
                remaining -= toLoad;
                if (remaining <= 0)
                {
                    OnCargoChanged?.Invoke();
                    BroadcastCargoState();
                    return true;
                }
            }
        }

        OnCargoChanged?.Invoke();
        BroadcastCargoState();
        return remaining < amount;
    }

    private void BroadcastCargoState()
    {
        if (!isLocal || !PhotonNetwork.InRoom) return;
        SendSyncEvent(isMoving ? NetFlagMoving : 0f); // inkl. aktuellem Ladestand
    }

    public bool TryLoadBuilder()
    {
        if (!isLocal) return false;

        foreach (var slot in slots)
        {
            if (slot.IsEmpty)
            {
                Villager freeWorker = FindFreeWorker();

                if (freeWorker == null)
                {
                    Villager freeVillager = FindFreeVillager();
                    if (freeVillager != null)
                    {
                        freeVillager.role = Villager.Role.Worker;
                        SpriteRenderer sr = freeVillager.GetComponent<SpriteRenderer>();
                        if (sr != null)
                        {
                            sr.color = Color.orange;
                            Sprite custom = Resources.Load<Sprite>("Worker");
                            if (custom != null) sr.sprite = custom;
                        }
                        freeWorker = freeVillager;
                    }
                }

                if (freeWorker == null)
                {
                    NotificationManager.Instance?.Notify("no_free_worker",
                        "Kein freier Bauarbeiter verfügbar!", 4f);
                    return false;
                }

                freeWorker.gameObject.SetActive(false);
                slot.content = ShipSlot.SlotContent.Builder;
                slot.amount = 1;
                slot.villager = freeWorker;
                OnCargoChanged?.Invoke();
                BroadcastCargoState();
                return true;
            }
        }
        return false;
    }

    private Villager FindFreeWorker()
    {
        if (VillagerManager.Instance == null) return null;
        Villager best = null;
        float bestDist = float.MaxValue;
        var list = VillagerManager.Instance.ActiveVillagers;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null || !v.gameObject.activeSelf) continue;
            if (v.role != Villager.Role.Worker) continue;
            if (v.assignedShip != null) continue;
            if (v.AssignedBuilding != null) continue;
            float d = Vector3.Distance(transform.position, v.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = v;
            }
        }
        return best;
    }

    private Villager FindFreeVillager()
    {
        if (VillagerManager.Instance == null) return null;
        Villager best = null;
        float bestDist = float.MaxValue;
        var list = VillagerManager.Instance.ActiveVillagers;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null || !v.gameObject.activeSelf) continue;
            if (v.role == Villager.Role.Worker) continue;
            if (v.assignedShip != null) continue;
            if (v.AssignedBuilding != null) continue;
            float d = Vector3.Distance(transform.position, v.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = v;
            }
        }
        return best;
    }

    public bool TryLoadSoldier(SoldierType type)
    {
        if (!isLocal) return false;

        Soldier available = FindNearestSoldier(type);
        if (available == null)
        {
            NotificationManager.Instance?.Notify("no_soldier_nearby",
                $"Kein {type}-Soldat in der Nähe!", 4f);
            return false;
        }

        foreach (var slot in slots)
        {
            if (slot.IsEmpty)
            {
                slot.content = ShipSlot.SlotContent.Soldier;
                slot.soldierType = type;
                slot.amount = 1;
                slot.loadedSoldierRef = available;
                available.gameObject.SetActive(false);
                OnCargoChanged?.Invoke();
                BroadcastCargoState();
                return true;
            }
        }
        return false;
    }

    private Soldier FindNearestSoldier(SoldierType type)
    {
        // Robust: Direkt ueber ActiveSoldiers iterieren (in OnEnable/OnDisable
        // gepflegt) statt ueber NetCombat.SoldiersById. Das Register hat bei
        // Soldaten gefehlt, deren netId erst in Start() zugewiesen wurde
        // (OnEnable registriert bei netId==0 nicht) - danach liess sich der
        // Soldat nicht aufs Schiff laden ("Kein Soldat in der Naehe").
        // Owner-Pruefung via IsOwnedByLocalPlayer (ownerActorNumber) ist
        // resilienter als OwnerFromId/ID-Division bei veralteten IDs.
        Soldier best = null;
        float bestDist = float.MaxValue;

        var soldiers = Soldier.ActiveSoldiers;
        for (int i = 0; i < soldiers.Count; i++)
        {
            Soldier s = soldiers[i];
            if (s == null || !s.gameObject.activeInHierarchy) continue;
            if (s.soldierType != type) continue;
            if (!s.IsOwnedByLocalPlayer) continue;
            if (IsSoldierAlreadyLoaded(s)) continue;

            float d = Vector3.Distance(transform.position, s.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = s;
            }
        }
        return best;
    }

    private bool IsSoldierAlreadyLoaded(Soldier soldier)
    {
        foreach (var slot in slots)
        {
            if (slot.loadedSoldierRef == soldier) return true;
        }
        return false;
    }

    // ── Unloading ─────────────────────────────────────────────────────────

    public void UnloadAllToIsland()
    {
        if (!isLocal) return;

        Vector2 landPos = FindNearestLandCell(transform.position);

        foreach (var slot in slots)
        {
            if (slot.content == ShipSlot.SlotContent.Material)
            {
                var warehouse = FindNearestLocalWarehouse();
                if (warehouse != null)
                    warehouse.ReceiveResource(slot.resourceId, slot.amount);
            }
            else if (slot.content == ShipSlot.SlotContent.Builder)
            {
                Vector2 spawnPos = landPos;
                if (slot.villager != null)
                {
                    slot.villager.transform.position = spawnPos;
                    slot.villager.gameObject.SetActive(true);
                    slot.villager.ReleaseFromShip();
                }
                else if (VillagerManager.Instance != null)
                {
                    VillagerManager.Instance.SpawnVillagerAt(spawnPos, Villager.Role.Worker, true);
                }
            }
            else if (slot.content == ShipSlot.SlotContent.Soldier)
            {
                Vector2 spawnPos = landPos;
                if (slot.loadedSoldierRef != null)
                {
                    slot.loadedSoldierRef.transform.position = spawnPos;
                    slot.loadedSoldierRef.gameObject.SetActive(true);
                    slot.loadedSoldierRef.transform.rotation = Quaternion.identity;
                }
                else
                {
                    // Soldat auf dem gegnerischen Client war nicht geladen (normal, da leer) → neu erzeugen
                    int actor = PhotonNetwork.InRoom ? PhotonNetwork.LocalPlayer.ActorNumber : 1;
                    NetCombat.SpawnSoldier(slot.soldierType, WeaponMaterial.Stone, new Vector3(spawnPos.x, spawnPos.y, 0f), actor, true);
                }
            }
            slot.Clear();
        }
        OnCargoChanged?.Invoke();
        BroadcastCargoState();
    }

    private Vector2 FindNearestLandCell(Vector2 from)
    {
        Vector2Int center = new Vector2Int(Mathf.RoundToInt(from.x), Mathf.RoundToInt(from.y));
        if (IslandManager.IsLand(center))
            return new Vector2(center.x, center.y);

        int searchRadius = 15;
        for (int r = 1; r <= searchRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    Vector2Int test = new Vector2Int(center.x + dx, center.y + dy);
                    if (IslandManager.IsLand(test))
                        return new Vector2(test.x, test.y);
                }
            }
        }
        return from;
    }

    public void RemoveSlotItem(int slotIndex)
    {
        if (!isLocal) return;
        if (slotIndex >= 0 && slotIndex < slots.Count)
        {
            var slot = slots[slotIndex];
            if (slot.loadedSoldierRef != null)
            {
                Vector2 landPos = FindNearestLandCell(transform.position);
                slot.loadedSoldierRef.transform.position = landPos;
                slot.loadedSoldierRef.gameObject.SetActive(true);
            }
            slot.Clear();
            OnCargoChanged?.Invoke();
            BroadcastCargoState();
        }
    }

    // ── Warehouse Interop ─────────────────────────────────────────────────

    public void UnloadToWarehouse(Warehouse warehouse)
    {
        if (!isLocal) return;
        if (warehouse == null) return;
        bool changed = false;
        foreach (var slot in slots)
        {
            if (slot.content == ShipSlot.SlotContent.Material && !string.IsNullOrEmpty(slot.resourceId))
            {
                warehouse.ReceiveResource(slot.resourceId, slot.amount);
                slot.Clear();
                changed = true;
            }
        }
        if (changed)
        {
            OnCargoChanged?.Invoke();
            BroadcastCargoState();
        }
    }

    public void LoadFromWarehouse(Warehouse warehouse, string resourceId, int amount)
    {
        if (!isLocal) return;
        if (warehouse == null) return;
        int toLoad = amount;
        while (toLoad > 0)
        {
            int batch = Mathf.Min(toLoad, 16);
            if (TryLoadMaterial(resourceId, batch))
            {
                warehouse.SpendResource(resourceId, batch);
                toLoad -= batch;
            }
            else break;
        }
    }

    public void ClearCargo()
    {
        if (!isLocal) return;
        foreach (var slot in slots) slot.Clear();
        OnCargoChanged?.Invoke();
        BroadcastCargoState();
    }

    private Warehouse FindNearestLocalWarehouse()
    {
        // WarehouseManager-Register (konsol mit GetNearestLocalWarehouse) statt FindObjectsOfType
        return WarehouseManager.Instance != null
            ? WarehouseManager.Instance.GetNearestLocalWarehouse(transform.position)
            : null;
    }

    public bool IsMoving() => isMoving;
    public Vector3 GetTargetPosition() => targetPosition;
    public bool IsSelected() => isSelected;

    public string GetShipName() => shipData != null ? shipData.shipName : gameObject.name;
    public ShipType GetShipType() => shipData != null ? shipData.shipType : ShipType.Trade;
    public bool IsTradeShip() => GetShipType() == ShipType.Trade;
    public bool IsMilitaryShip() => GetShipType() == ShipType.Military;

    // ── Schaden / Zerstoerung (netzwerkfaehig) ────────────────────────────

    /// <summary>Schaden verursachen. Authority (Besitzer des Schiffs) entscheidet ueber HP.</summary>
    public void TakeDamage(int damage)
    {
        NetCombat.ApplyShipDamage(this, damage);
    }

    /// <summary>Lokale HP-Anwendung (nur Clients mit Authority fuer dieses Schiff).</summary>
    public void ApplyLocalDamage(int damage)
    {
        currentHP -= Mathf.Max(0, damage);
        if (currentHP <= 0)
        {
            currentHP = 0;
            // NICHT NetDestroy() — wir sind der Zerstörer und MUSSEN via OnDestroy broadcasten!
            Destroy(gameObject);
            return;
        }
    }

    /// <summary>Netzvernichtung (Event 25): ohne erneuten Broadcast.</summary>
    public void NetDestroy()
    {
        destroyedByNet = true;
        Destroy(gameObject);
    }

    private bool destroyedByNet;

    private void OnDestroy()
    {
        ReleaseOccupancy();

        if (shipNetId != 0)
        {
            NetCombat.UnregisterShip(this);

            // Lokale Zerstoerung (nicht per Netz): an alle funken.
            if (!destroyedByNet && !isQuitting && currentHP <= 0)
            {
                NetCombat.Raise(NetCombat.ShipDestroyEvent, new object[] { shipNetId }, true);
            }
        }

        // Geladene Soldaten/Bauarbeiter nicht fuer immer deaktiviert lassen:
        // beim Sinken am naechsten Land wieder freigeben.
        if (!isQuitting)
        {
            SalvageLoadedUnits();
        }

        ReleaseCrew();
    }

    /// <summary>
    /// Beim Zerstoeren des Schiffs geladene Einheiten am naechsten Land wieder aktivieren.
    /// Material geht mit dem Schiff unter. Auf fremden Clients sind Slots leer -> No-Op.
    /// </summary>
    private void SalvageLoadedUnits()
    {
        Vector2 landPos = FindNearestLandCell(transform.position);
        int actor = PhotonNetwork.InRoom ? PhotonNetwork.LocalPlayer.ActorNumber : 1;

        foreach (var slot in slots)
        {
            if (slot.content == ShipSlot.SlotContent.Soldier)
            {
                if (slot.loadedSoldierRef != null)
                {
                    slot.loadedSoldierRef.transform.position = landPos;
                    slot.loadedSoldierRef.transform.rotation = Quaternion.identity;
                    slot.loadedSoldierRef.gameObject.SetActive(true);
                }
                else
                {
                    // Referenz verloren (z.B. Soldat ausserhalb des Clients erzeugt): Ersatz spawnen.
                    NetCombat.SpawnSoldier(slot.soldierType, WeaponMaterial.Stone,
                        new Vector3(landPos.x, landPos.y, 0f), actor, true);
                }
            }
            else if (slot.content == ShipSlot.SlotContent.Builder && slot.villager != null)
            {
                slot.villager.transform.position = landPos;
                slot.villager.gameObject.SetActive(true);
                slot.villager.ReleaseFromShip();
            }
            slot.Clear();
        }
    }

    private static bool isQuitting;

    void OnApplicationQuit()
    {
        isQuitting = true;
    }
}
