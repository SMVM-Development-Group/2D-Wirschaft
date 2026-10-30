using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;

public class Ship : MonoBehaviour
{
    [Header("Ship Configuration")]
    public ShipData shipData;
    public int shipLevel = 1;
    public bool isLocal = true;
    
    [Header("Cargo System")]
    public List<ShipSlot> slots = new List<ShipSlot>();
    
    [Header("Movement")]
    public float moveSpeed = 2f;
    public float rotationSpeed = 180f;
    private Vector3 targetPosition;
    private bool isMoving = false;
    private float movementStartTime;
    private bool isSelected = false;
    private float targetAngle = 0f;
    private float currentAngle = 0f;
    private List<Vector2> waterPath = new List<Vector2>();
    private int waterPathIndex = 0;

    [Header("Health")]
    public int maxHP = 180;
    public int currentHP = 180;
    
    [Header("Crew")]
    public Villager assignedCrew;  // The villager sailing this ship
    
    [Header("Multiplayer Sync")]
    public Vector2Int spawnOrigin;
    private float syncTimer = 0f;
    private const float SyncInterval = 0.15f;

    [Header("Visual")]
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    public Color selectedColor = new Color(0.3f, 0.85f, 1f, 1f);
    
    // Events
    public System.Action<Ship> OnShipClicked;
    public System.Action OnCargoChanged;
    
    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        currentAngle = transform.rotation.eulerAngles.z;
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

        // Add click detector
        var collider = GetComponent<Collider2D>();
        if (collider == null)
        {
            var bc = gameObject.AddComponent<BoxCollider2D>();
            bc.size = new Vector2(1.5f, 1.5f);
        }

        // Subscribe to cargo changes
        OnCargoChanged += UpdateVisualState;

        // Automatisch einen arbeitslosen Dorfbewohner als Besatzung zuweisen
        if (!HasCrew() && VillagerManager.Instance != null)
        {
            AssignFirstAvailableVillager();
        }
    }

    private void UpdateVisualState()
    {
        if (spriteRenderer != null)
        {
            int used = slots.Count(s => !s.IsEmpty);
            int total = slots.Count;
            float ratio = total > 0 ? (float)used / total : 0f;
            spriteRenderer.color = Color.Lerp(originalColor, selectedColor, ratio * 0.3f);
        }
    }
    
    private void Update()
    {
        HandleMovement();
        HandleClickDetection();
        BroadcastPosition();
    }
    
    private void HandleClickDetection()
    {
        if (!isLocal) return;
        
        // Check if mouse is over this ship
        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
        Collider2D hit = Physics2D.OverlapPoint(mouseWorldPos);
        
        if (hit != null && hit.gameObject == gameObject)
        {
            if (UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                // Check if shift is held for multi-select, otherwise single click
                if (UnityEngine.InputSystem.Keyboard.current.shiftKey.isPressed)
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
    
    private void HandleMovement()
    {
        if (!isMoving) return;

        if (waterPath.Count == 0)
        {
            MoveDirectTowardsTarget();
            return;
        }

        Vector3 target;
        if (waterPathIndex < waterPath.Count)
        {
            target = new Vector3(waterPath[waterPathIndex].x, waterPath[waterPathIndex].y, transform.position.z);
        }
        else
        {
            target = targetPosition;
        }
        
        Vector3 direction = target - transform.position;
        direction.z = 0;
        
        if (direction.magnitude < 0.3f)
        {
            if (waterPathIndex < waterPath.Count)
            {
                waterPathIndex++;
                return;
            }
            else
            {
                StopMovement();
                return;
            }
        }

        if (Time.time - movementStartTime > 2f && IsOnLand())
        {
            StopMovement();
            NotificationManager.Instance?.Notify("ship_stranded",
                "Schiff gestrandet!", 5f);
            return;
        }
        
        targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        
        float angleDiff = Mathf.DeltaAngle(currentAngle, targetAngle);
        float rotationStep = rotationSpeed * Time.deltaTime;
        
        if (Mathf.Abs(angleDiff) < rotationStep)
            currentAngle = targetAngle;
        else
            currentAngle += Mathf.Sign(angleDiff) * rotationStep;
        
        transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
        transform.position += transform.up * moveSpeed * Time.deltaTime;
        
        RevealFogAlongPath();
    }

    private void MoveDirectTowardsTarget()
    {
        Vector3 direction = targetPosition - transform.position;
        direction.z = 0;

        if (direction.magnitude < 0.3f)
        {
            StopMovement();
            return;
        }

        if (Time.time - movementStartTime > 2f && IsOnLand())
        {
            StopMovement();
            NotificationManager.Instance?.Notify("ship_stranded",
                "Schiff gestrandet! Kein Wasserweg.", 5f);
            return;
        }

        targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        float angleDiff = Mathf.DeltaAngle(currentAngle, targetAngle);
        float rotationStep = rotationSpeed * Time.deltaTime;
        if (Mathf.Abs(angleDiff) < rotationStep)
            currentAngle = targetAngle;
        else
            currentAngle += Mathf.Sign(angleDiff) * rotationStep;

        transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
        transform.position += transform.up * moveSpeed * Time.deltaTime;
    }

    private bool IsOnLand()
    {
        Vector2Int grid = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        return IslandManager.IsLand(grid);
    }
    
    public void MoveTo(Vector3 destination)
    {
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
        
        waterPath = BuildingManager.FindWaterPath(transform.position, destination);
        waterPathIndex = 0;
        movementStartTime = Time.time;
        isMoving = true;
        
        Debug.Log($"[Ship] Moving to {destination}, path length: {waterPath.Count}");
    }
    

    private Vector3 FindNearestWater(Vector3 landPos)
    {
        Vector2Int center = new Vector2Int(Mathf.RoundToInt(landPos.x), Mathf.RoundToInt(landPos.y));
        int searchRadius = 20;

        for (int r = 1; r <= searchRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    Vector2Int test = new Vector2Int(center.x + dx, center.y + dy);
                    if (BuildingManager.IsWalkable(test))
                    {
                        Vector3 nearest = new Vector3(test.x, test.y, landPos.z);
                        if (IslandManager.IsLand(test))
                        {
                            // Check adjacent water
                            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                            foreach (var d in dirs)
                            {
                                Vector2Int water = test + d;
                                if (!IslandManager.IsLand(water))
                                    return new Vector3(water.x, water.y, landPos.z);
                            }
                        }
                    }
                }
            }
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
            Debug.Log($"[Ship] Auto-assigned villager {v.name} as crew");
        }
    }
    
    private void StopMovement()
    {
        isMoving = false;
        RevealAreaAroundShip();
        if (isLocal && PhotonNetwork.InRoom)
        {
            SendSyncEvent();
        }
    }
    
    public void StopMovementCommand()
    {
        isMoving = false;
    }

    private void BroadcastPosition()
    {
        if (!isLocal || !PhotonNetwork.InRoom) return;

        if (!isMoving) return;

        syncTimer += Time.deltaTime;
        if (syncTimer < SyncInterval) return;
        syncTimer = 0f;

        SendSyncEvent();
    }

    private void SendSyncEvent()
    {
        Vector3 pos = transform.position;
        float rot = transform.rotation.eulerAngles.z;
        object[] data = new object[]
        {
            (float)spawnOrigin.x,
            (float)spawnOrigin.y,
            pos.x,
            pos.y,
            rot
        };
        PhotonNetwork.RaiseEvent(14, data,
            new Photon.Realtime.RaiseEventOptions { Receivers = Photon.Realtime.ReceiverGroup.Others },
            new ExitGames.Client.Photon.SendOptions { Reliability = false });
    }
    
    private void RevealFogAlongPath()
    {
        FogProjector.RegisterExploration(transform.position, 40f);
    }
    
    private void RevealAreaAroundShip()
    {
        FogProjector.RegisterExploration(transform.position, 50f);
    }
    
    private void SelectShip()
    {
        // Deselect all other ships first
        Ship[] allShips = FindObjectsByType<Ship>();
        foreach (Ship s in allShips)
        {
            s.isSelected = false;
            s.UpdateVisualSelection();
        }
        
        isSelected = true;
        UpdateVisualSelection();
        
        // Open ship UI panel — auto-create if not in scene (wie BuildingInfoPanel)
        ShipCargoUI cargoUI = ShipCargoUI.Instance;
        if (cargoUI == null)
        {
            cargoUI = FindAnyObjectByType<ShipCargoUI>();
        }
        if (cargoUI == null)
        {
            GameObject go = new GameObject("ShipCargoUI");
            cargoUI = go.AddComponent<ShipCargoUI>();
            Debug.Log("[Ship] ShipCargoUI auto-erstellt.");
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
            spriteRenderer.color = originalColor;
        }
    }
    
    // ── Cargo System (New Slot-Based) ─────────────────────────────────────

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

    // ── Slot Management ──────────────────────────────────────────────────

    public int GetSlotCount() => slots.Count;

    public int GetUsedSlotCount() => slots.Count(s => !s.IsEmpty);

    public int GetFreeSlotCount() => slots.Count(s => s.IsEmpty);

    public bool HasFreeSlot() => GetFreeSlotCount() > 0;

    public int GetCargoCount() => slots.Count(s => s.content == ShipSlot.SlotContent.Material);

    public int GetCargoCapacity() => slots.Count;

    public int GetFreeCargoSpace() => GetFreeSlotCount();

    public bool CanAddCargo() => HasFreeSlot();

    public List<ShipSlot> GetAllSlots() => new List<ShipSlot>(slots);

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

    // ── Loading ──────────────────────────────────────────────────────────

    public bool TryLoadMaterial(string resourceId, int amount)
    {
        int remaining = amount;

        // Erst vorhandene Slots mit gleichem Material auffüllen (max 16)
        foreach (var slot in slots)
        {
            if (slot.content == ShipSlot.SlotContent.Material && slot.resourceId == resourceId && slot.amount < 16)
            {
                int canAdd = Mathf.Min(remaining, 16 - slot.amount);
                slot.amount += canAdd;
                remaining -= canAdd;
                if (remaining <= 0) { OnCargoChanged?.Invoke(); return true; }
            }
        }

        // Dann leere Slots belegen
        foreach (var slot in slots)
        {
            if (slot.IsEmpty)
            {
                int toLoad = Mathf.Min(remaining, 16);
                slot.content = ShipSlot.SlotContent.Material;
                slot.resourceId = resourceId;
                slot.amount = toLoad;
                remaining -= toLoad;
                if (remaining <= 0) { OnCargoChanged?.Invoke(); return true; }
            }
        }

        OnCargoChanged?.Invoke();
        return remaining < amount;
    }

    public bool TryLoadBuilder()
    {
        foreach (var slot in slots)
        {
            if (slot.IsEmpty)
            {
                Villager freeVillager = FindFreeVillager();
                if (freeVillager == null)
                {
                    NotificationManager.Instance?.Notify("no_free_villager",
                        "Kein freier Dorfbewohner verfügbar!", 4f);
                    return false;
                }
                freeVillager.gameObject.SetActive(false);
                slot.content = ShipSlot.SlotContent.Builder;
                slot.amount = 1;
                slot.villager = freeVillager;
                OnCargoChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    private Villager FindFreeVillager()
    {
        if (VillagerManager.Instance == null) return null;
        Villager best = null;
        float bestDist = float.MaxValue;
        foreach (var v in VillagerManager.Instance.ActiveVillagers)
        {
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
                return true;
            }
        }
        return false;
    }

    private Soldier FindNearestSoldier(SoldierType type)
    {
        Soldier[] allSoldiers = FindObjectsByType<Soldier>();
        Soldier best = null;
        float bestDist = float.MaxValue;

        foreach (var s in allSoldiers)
        {
            if (s == null || !s.gameObject.activeInHierarchy) continue;
            if (s.soldierType != type) continue;
            if (s.team != Team.Player) continue;
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

    // ── Unloading ────────────────────────────────────────────────────────

    public void UnloadAllToIsland()
    {
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
                if (slot.villager != null)
                {
                    slot.villager.transform.position = transform.position + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), 0f);
                    slot.villager.gameObject.SetActive(true);
                    slot.villager.ReleaseFromShip();
                }
                else if (VillagerManager.Instance != null)
                {
                    VillagerManager.Instance.SpawnVillagerAt(transform.position, Villager.Role.Worker);
                }
            }
            else if (slot.content == ShipSlot.SlotContent.Soldier)
            {
                if (slot.loadedSoldierRef != null)
                {
                    slot.loadedSoldierRef.transform.position = transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f);
                    slot.loadedSoldierRef.gameObject.SetActive(true);
                }
                else
                {
                    GameObject solObj = new GameObject($"Unloaded_Soldier_{slot.soldierType}");
                    solObj.transform.position = transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f);
                    var sr = solObj.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = 21;
                    solObj.AddComponent<BoxCollider2D>().size = new Vector2(1f, 1f);
                    var s = solObj.AddComponent<Soldier>();
                    s.soldierType = slot.soldierType;
                    s.team = Team.Player;
                    s.moveSpeed = 1.5f;
                    FogRevealer fr = solObj.AddComponent<FogRevealer>();
                    fr.radius = 4f;
                    fr.isLocalPlayer = true;
                }
            }
            slot.Clear();
        }
        OnCargoChanged?.Invoke();
    }

    public void RemoveSlotItem(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < slots.Count)
        {
            var slot = slots[slotIndex];
            if (slot.loadedSoldierRef != null)
            {
                slot.loadedSoldierRef.transform.position = transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f);
                slot.loadedSoldierRef.gameObject.SetActive(true);
            }
            slot.Clear();
            OnCargoChanged?.Invoke();
        }
    }

    // ── Warehouse Interop (alt, für Kompatibilität) ──────────────────────

    public void UnloadToWarehouse(Warehouse warehouse)
    {
        if (warehouse == null) return;
        foreach (var slot in slots)
        {
            if (slot.content == ShipSlot.SlotContent.Material && !string.IsNullOrEmpty(slot.resourceId))
            {
                warehouse.ReceiveResource(slot.resourceId, slot.amount);
                slot.Clear();
            }
        }
        OnCargoChanged?.Invoke();
    }

    public void LoadFromWarehouse(Warehouse warehouse, string resourceId, int amount)
    {
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
        foreach (var slot in slots) slot.Clear();
        OnCargoChanged?.Invoke();
    }

    private Warehouse FindNearestLocalWarehouse()
    {
        Warehouse[] whs = FindObjectsByType<Warehouse>();
        Warehouse nearest = null;
        float minDist = float.MaxValue;
        foreach (var wh in whs)
        {
            if (wh.isLocal)
            {
                float dist = Vector3.Distance(transform.position, wh.transform.position);
                if (dist < minDist) { minDist = dist; nearest = wh; }
            }
        }
        return nearest;
    }

    public bool IsMoving() => isMoving;
    public Vector3 GetTargetPosition() => targetPosition;
    public bool IsSelected() => isSelected;

    public string GetShipName() => shipData != null ? shipData.shipName : gameObject.name;
    public ShipType GetShipType() => shipData != null ? shipData.shipType : ShipType.Trade;
    public bool IsTradeShip() => GetShipType() == ShipType.Trade;
    public bool IsMilitaryShip() => GetShipType() == ShipType.Military;

    public void TakeDamage(int damage)
    {
        currentHP -= Mathf.Max(0, damage);
        if (currentHP <= 0)
        {
            currentHP = 0;
            Destroy(gameObject);
        }
    }
    
    private void OnDestroy()
    {
        ReleaseCrew();
    }
}


