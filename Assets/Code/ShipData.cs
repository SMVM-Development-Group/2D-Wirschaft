using UnityEngine;
using System.Collections.Generic;

public enum ShipType { Trade, Military }

[System.Serializable]
public class ShipSlot
{
    public enum SlotContent { Empty, Material, Builder, Soldier }
    public SlotContent content = SlotContent.Empty;
    public string resourceId = "";
    public int amount = 0;
    public SoldierType soldierType = SoldierType.Spear;
    public int loadedSoldierNetId = 0;   // netId des geladenen Soldaten (fuer Netz-Sync)
    public Villager villager = null;
    public Soldier loadedSoldierRef = null;

    public bool IsEmpty => content == SlotContent.Empty;
    public bool IsPerson => content == SlotContent.Builder || content == SlotContent.Soldier;

    public void Clear()
    {
        content = SlotContent.Empty;
        resourceId = "";
        amount = 0;
        loadedSoldierNetId = 0;
        villager = null;
        loadedSoldierRef = null;
    }
}

[CreateAssetMenu(fileName = "NewShipData", menuName = "Ships/ShipData")]
public class ShipData : ScriptableObject
{
    [Header("Basic Info")]
    public string shipName = "Handelsschiff";
    public ShipType shipType = ShipType.Trade;
    public GameObject prefab;
    
    [Header("Size")]
    public int width = 1;
    public int height = 3;
    
    [Header("Level")]
    [Range(1, 3)]
    public int level = 1;

    [Header("Capacity")]
    public int crewRequired = 1;
    
    [Header("Movement")]
    public float moveSpeed = 2f;
    public float rotationSpeed = 180f;
    
    [Header("Visuals")]
    public Color shipColor = Color.white;
    public Sprite shipSprite;

    public int GetSlotCount()
    {
        return level switch
        {
            1 => 8,
            2 => 16,
            3 => 30,
            _ => 8
        };
    }
}