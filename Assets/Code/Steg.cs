using UnityEngine;
using System.Collections.Generic;

public class Steg : MonoBehaviour
{
    public Vector2Int GridPosition { get; private set; }
    private bool registered;
    private Vector2Int registeredGrid;

    void OnEnable()
    {
        // Achtung: Hier darf NICHT registriert werden!
        // Instantiate() aktiviert das Objekt sofort mit Position (0,0),
        // bevor Initialize() die echte Grid-Zelle setzt — dadurch landete im
        // stegGrid-Register immer (0,0) und Pier-Adjazenz (Verlängerungen an
        // bereits gebaute Stege) schlug fehl. Registrierung passiert in Initialize().
    }
    void OnDisable()
    {
        // Nur deregistrieren, wenn wir vorher sauber registriert wurden (Grid-Position bekannt).
        if (registered) BuildingManager.UnregisterSteg(this);
    }

    // Initialize the steg to the nearest integer grid position (1x1 cells).
    // World X -> grid.x, World Y -> grid.y
    public void Initialize(Vector3 worldPos)
    {
        GridPosition = WorldToGrid(worldPos);
        transform.position = new Vector3(GridPosition.x, GridPosition.y, -0.21f);

        // Registrierung erst JETZT, wenn GridPosition stimmt.
        // Falls das Objekt schon mit einer anderen Zelle registriert war (Reposition), Grid-Eintrag umbinden.
        if (!registered)
        {
            registered = true;
            BuildingManager.RegisterSteg(this);
        }
        else if (registeredGrid != GridPosition)
        {
            BuildingManager.StegGridChanged(this, registeredGrid, GridPosition);
        }
        registeredGrid = GridPosition;
    }

    public static Vector2Int WorldToGrid(Vector3 w) => new Vector2Int(Mathf.RoundToInt(w.x), Mathf.RoundToInt(w.y));

    public IEnumerable<Vector2Int> NeighborPositions()
    {
        yield return GridPosition + new Vector2Int(1, 0);
        yield return GridPosition + new Vector2Int(-1, 0);
        yield return GridPosition + new Vector2Int(0, 1);
        yield return GridPosition + new Vector2Int(0, -1);
    }

    // Call to remove this steg. The manager will recalculate supports and collapse unsupported stegs.
    public void Remove()
    {
        BuildingManager.OnStegRemoved(this);
        Destroy(gameObject);
    }
}
