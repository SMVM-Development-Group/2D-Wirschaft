using UnityEngine;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

public class Warehouse : MonoBehaviour
{
    [Header("Warehouse Configuration")]
    public bool isLocal = true;
    public int maxHealth = 150;
    public int currentHealth = 150;
    
    [Header("Local Island Association")]
    public Vector2 islandCenter;  // The center of the island this warehouse belongs to
    public string localIslandId;  // Identifier for the island
    public bool isMainWarehouse = false;  // First warehouse on player's starting island
    
    [Header("Storage")]
    public int storageCapacity = 50;  // Max items total
    private Dictionary<string, int> storedResources = new Dictionary<string, int>();
    
    [Header("Visual")]
    private SpriteRenderer spriteRenderer;
    public Color healthyColor = Color.white;
    public Color damagedColor = new Color(1f, 0.5f, 0.5f);
    public Color capturedColor = new Color(0.5f, 0.5f, 1f);
    
    [Header("Capture")]
    public int capturingPlayerId = -1;  // -1 = not being captured
    
    // Events
    public System.Action<Warehouse> OnHealthChanged;
    public System.Action<Warehouse> OnCaptured;
    public System.Action OnResourcesChanged;
    
    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        InitializeStorage();
    }
    
    private void Start()
    {
        // Determine which island this warehouse is on
        DetermineIslandAssociation();
        
        // Add to warehouse list for island ownership tracking
        WarehouseManager.Instance?.RegisterWarehouse(this);
    }
    
    private void OnDestroy()
    {
        // Remove from warehouse list
        WarehouseManager.Instance?.UnregisterWarehouse(this);
    }
    
    private void Update()
    {
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        if (spriteRenderer == null) return;

        Color target;
        if (!isLocal)
        {
            target = capturedColor;
        }
        else
        {
            float healthPercent = (float)currentHealth / maxHealth;
            target = healthPercent < 0.5f
                ? Color.Lerp(damagedColor, healthyColor, healthPercent * 2)
                : healthyColor;
        }

        // Nur schreiben wenn die Farbe sich wirklich ändert (spart Render-Dirty-Flags)
        if (spriteRenderer.color != target)
        {
            spriteRenderer.color = target;
        }
    }

    private void DetermineIslandAssociation()
    {
        // Find the closest island
        Vector2 warehousePos = transform.position;

        // Use the island that contains this warehouse position
        if (IslandManager.IsLand(warehousePos))
        {
            islandCenter = new Vector2(Mathf.Round(warehousePos.x), Mathf.Round(warehousePos.y));
            localIslandId = $"island_{islandCenter.x}_{islandCenter.y}";
        }
    }

    private void InitializeStorage()
    {
        // Start with some basic resources based on island
        // Resources will be populated by ResourceManager or manually
    }

    // ── Resource Management ───────────────────────────────────────────────────
    
    public int GetStoredAmount(string resourceId)
    {
        return storedResources.TryGetValue(resourceId, out int amount) ? amount : 0;
    }
    
    public bool HasResource(string resourceId, int amount)
    {
        return GetStoredAmount(resourceId) >= amount;
    }
    
    public int GetTotalStoredCount()
    {
        int total = 0;
        foreach (var kvp in storedResources)
        {
            total += kvp.Value;
        }
        return total;
    }
    
    public int GetFreeStorageSpace()
    {
        return storageCapacity - GetTotalStoredCount();
    }
    
    public bool CanStoreMore()
    {
        return GetTotalStoredCount() < storageCapacity;
    }
    
    public bool ReceiveResource(string resourceId, int amount)
    {
        if (!CanStoreMore())
        {
            Debug.Log($"[Warehouse] Storage full! Cannot receive {resourceId}");
            return false;
        }
        
        int current = GetStoredAmount(resourceId);
        int toAdd = Mathf.Min(amount, GetFreeStorageSpace());
        
        storedResources[resourceId] = current + toAdd;
        OnResourcesChanged?.Invoke();
        
        return toAdd == amount;
    }
    
    public bool SpendResource(string resourceId, int amount)
    {
        if (!HasResource(resourceId, amount))
        {
            return false;
        }
        
        storedResources[resourceId] -= amount;
        if (storedResources[resourceId] <= 0)
        {
            storedResources.Remove(resourceId);
        }
        
        OnResourcesChanged?.Invoke();
        return true;
    }
    
    public Dictionary<string, int> GetAllResources()
    {
        return new Dictionary<string, int>(storedResources);
    }
    
    public List<string> GetResourceIds()
    {
        return new List<string>(storedResources.Keys);
    }
    
    public void SetResource(string resourceId, int amount)
    {
        if (amount <= 0)
        {
            storedResources.Remove(resourceId);
        }
        else
        {
            storedResources[resourceId] = amount;
        }
        OnResourcesChanged?.Invoke();
    }
    
    // ── Health System ─────────────────────────────────────────────────────────
    
    public int GetHealth()
    {
        return currentHealth;
    }
    
    public int GetMaxHealth()
    {
        return maxHealth;
    }
    
    public float GetHealthPercent()
    {
        return (float)currentHealth / maxHealth;
    }
    
    public void TakeDamage(int damage, int attackingPlayerId = -1)
    {
        if (damage <= 0) return;
        
        currentHealth -= damage;
        capturingPlayerId = attackingPlayerId;
        
        Debug.Log($"[Warehouse] Took {damage} damage! Health: {currentHealth}/{maxHealth}");
        
        OnHealthChanged?.Invoke(this);
        
        if (currentHealth <= 0)
        {
            Capture(attackingPlayerId);
        }
    }
    
    public void Heal(int amount)
    {
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        OnHealthChanged?.Invoke(this);
    }
    
    public void Repair(int amount)
    {
        Heal(amount);
    }
    
    // ── Capture System ────────────────────────────────────────────────────────
    
    public void Capture(int newOwnerPlayerId)
    {
        Debug.Log($"[Warehouse] Captured by player {newOwnerPlayerId}!");
        
        // Transfer ownership
        isLocal = false;
        
        // Clear or transfer resources based on game design
        // For now, we keep resources but change ownership
        
        OnCaptured?.Invoke(this);
        
        // Sync capture to other players (Event 10)
        if (PhotonNetwork.InRoom)
        {
            object[] data = new object[]
            {
                gameObject.name,
                transform.position.x,
                transform.position.y,
                newOwnerPlayerId
            };
            PhotonNetwork.RaiseEvent(10, data, new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
        }
    }
    
    public void SetOwner(bool local)
    {
        bool wasLocal = isLocal;
        isLocal = local;
        
        if (wasLocal && !isLocal)
        {
            // Lost ownership - trigger capture effect
            spriteRenderer.color = capturedColor;
        }
        else if (!wasLocal && isLocal)
        {
            // Regained ownership
            spriteRenderer.color = healthyColor;
        }
    }
    
    // ── Ship Interaction ──────────────────────────────────────────────────────
    
    public bool LoadShip(Ship ship, string resourceId, int amount)
    {
        if (ship == null) return false;
        if (!isLocal) return false;
        
        int available = GetStoredAmount(resourceId);
        int toLoad = Mathf.Min(amount, available, ship.GetFreeCargoSpace());
        
        if (toLoad <= 0) return false;
        
        // Transfer from warehouse to ship
        SpendResource(resourceId, toLoad);
        ship.TryLoadMaterial(resourceId, toLoad);
        
        return true;
    }
    
    public void UnloadShip(Ship ship)
    {
        if (ship == null) return;
        if (!isLocal) return;
        
        ship.UnloadToWarehouse(this);
    }
    
    // ── Utility ───────────────────────────────────────────────────────────────
    
    public bool IsOnLocalIsland()
    {
        // Check if this warehouse is on the local player's island
        return isLocal;
    }
    
    public Vector2 GetPosition()
    {
        return transform.position;
    }
    
    public string GetDisplayName()
    {
        return isLocal ? "Mein Lager" : "Feindliches Lager";
    }
}