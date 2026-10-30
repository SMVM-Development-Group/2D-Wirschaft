using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Zentrale Multiplayer-Kampfsynchronisation.
/// Schadensfragen werden vom BESITZER der Einheit entschieden und über Events
/// an alle anderen Clients verteilt ("Authority" für Soldat/Schiff).
/// </summary>
public static class NetCombat
{
    public const byte SoldierDamageEvent = 22; // { int netId, float amount }
    public const byte SoldierDieEvent = 23;    // { int netId }
    public const byte ShipDamageEvent = 24;    // { int shipId, int damage }
    public const byte ShipDestroyEvent = 25;   // { int shipId }

    // IDs: <actorNumber> * 1_000_000 + laufende Nummer (kollisionsfrei pro Client)
    private const int IdMultiplier = 1000000;

    private static readonly Dictionary<int, int> nextIdPerActor = new Dictionary<int, int>();

    // Zentrale Nachschlageregister (kein FindObjectsOfType nötig)
    public static readonly Dictionary<int, Soldier> SoldiersById = new Dictionary<int, Soldier>();
    public static readonly Dictionary<int, Ship> ShipsById = new Dictionary<int, Ship>();

    private static int LocalActorCached;

    public static int LocalActorId
    {
        get
        {
            if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
            {
                LocalActorCached = PhotonNetwork.LocalPlayer.ActorNumber;
            }
            return LocalActorCached > 0 ? LocalActorCached : 1;
        }
    }

    /// <summary>Erzeugt eine netzwerkweit eindeutige ID für den lokalen Client.</summary>
    public static int NextId(int actorNumber)
    {
        int actor = actorNumber > 0 ? actorNumber : LocalActorId;
        nextIdPerActor.TryGetValue(actor, out int n);
        n++;
        nextIdPerActor[actor] = n;
        return actor * IdMultiplier + n;
    }

    public static int OwnerFromId(int id)
    {
        return id > 0 ? id / IdMultiplier : 0;
    }

    // ── Registrierung ──────────────────────────────────────────────────────
    public static void RegisterSoldier(Soldier s)
    {
        if (s == null || s.netId == 0) return;
        SoldiersById[s.netId] = s;
    }

    public static void UnregisterSoldier(Soldier s)
    {
        if (s == null || s.netId == 0) return;
        if (SoldiersById.TryGetValue(s.netId, out Soldier existing) && ReferenceEquals(existing, s))
        {
            SoldiersById.Remove(s.netId);
        }
    }

    public static void RegisterShip(Ship s)
    {
        if (s == null || s.ShipNetId == 0) return;
        ShipsById[s.ShipNetId] = s;
    }

    public static void UnregisterShip(Ship s)
    {
        if (s == null || s.ShipNetId == 0) return;
        if (ShipsById.TryGetValue(s.ShipNetId, out Ship existing) && ReferenceEquals(existing, s))
        {
            ShipsById.Remove(s.ShipNetId);
        }
    }

    public static bool TryGetSoldier(int netId, out Soldier s) => SoldiersById.TryGetValue(netId, out s) && s != null;
    public static bool TryGetShip(int shipId, out Ship s) => ShipsById.TryGetValue(shipId, out s) && s != null;

    public static void Raise(byte eventCode, object payload, bool reliable)
    {
        if (!PhotonNetwork.InRoom) return;
        PhotonNetwork.RaiseEvent(eventCode, payload,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            new SendOptions { Reliability = reliable });
    }

    // ── Soldaten-Schaden ──────────────────────────────────────────────────
    /// <summary>Wird vom Angreifer (Turm/Soldat/Schiff) aufgerufen.
    /// Wende lokal an, wenn der Angreifer selbst die Authority ist, sonst an den Besitzer funken.</summary>
    public static void ApplySoldierDamage(Soldier target, float amount)
    {
        if (target == null || amount <= 0f) return;

        if (target.IsAuthority)
        {
            // Authority ist hier → direkt und lokal anwenden
            target.ApplyLocalDamage(amount);
        }
        else
        {
            // Dem Besitzer des Ziols funken;
            Raise(SoldierDamageEvent, new object[] { target.netId, amount }, true);
        }
    }

    /// <summary>Auf Empfängerseite: Schaden anwenden, falls wir Authority über die Einheit haben.</summary>
    public static void ReceiveSoldierDamage(object[] data)
    {
        if (data == null || data.Length < 2) return;
        int netId = (int)data[0];
        float amount = (float)data[1];

        if (TryGetSoldier(netId, out Soldier s) && s.IsAuthority)
        {
            s.ApplyLocalDamage(amount);
        }
    }

    /// <summary>Tod broadcasten (vom Authority-Client des Gestorbenen).</summary>
    public static void BroadcastSoldierDie(int netId)
    {
        Raise(SoldierDieEvent, new object[] { netId }, true);
    }

    /// <summary>Auf Empfängerseite: Kopie entfernen (keine Re-Broadcast!).</summary>
    public static void ReceiveSoldierDie(object[] data)
    {
        if (data == null || data.Length < 1) return;
        int netId = (int)data[0];
        if (TryGetSoldier(netId, out Soldier s))
        {
            s.NetDestroy(); // entfernt Registrierung + GameObject
            SoldiersById.Remove(netId);
        }
    }

    // ── Schiffs-Schaden ───────────────────────────────────────────────────
    public static void ApplyShipDamage(Ship target, int amount)
    {
        if (target == null || amount <= 0) return;

        if (target.IsAuthority)
        {
            target.ApplyLocalDamage(amount);
        }
        else
        {
            Raise(ShipDamageEvent, new object[] { target.ShipNetId, amount }, true);
        }
    }

    public static void ReceiveShipDamage(object[] data)
    {
        if (data == null || data.Length < 2) return;
        int shipId = (int)data[0];
        int amount = (int)data[1];

        if (TryGetShip(shipId, out Ship s) && s.IsAuthority)
        {
            s.ApplyLocalDamage(amount);
        }
    }

    public static void ReceiveShipDestroy(object[] data)
    {
        if (data == null || data.Length < 1) return;
        int shipId = (int)data[0];
        if (TryGetShip(shipId, out Ship s))
        {
            s.NetDestroy();
        }
    }

    // ── Soldaten spawner (einzige Quelle für Soldaten-Objekte) ────────────
    /// <summary>
    /// Erzeugt einen Soldaten lokal und – wenn broadcast – synchronisiert ihn über Event 12.
    /// forcedNetId > 0 wird beim Empfang von Event 12 benutzt (Clienteigene Kopie).
    /// </summary>
    public static Soldier SpawnSoldier(SoldierType type, WeaponMaterial material, Vector3 position,
        int ownerActor, bool broadcast, int forcedNetId = 0)
    {
        GameObject solObj = new GameObject($"Soldier_{type}_{ownerActor}");
        solObj.transform.position = position;
        solObj.transform.rotation = Quaternion.identity;

        SpriteRenderer sr = solObj.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 21;
        solObj.AddComponent<BoxCollider2D>().size = new Vector2(1f, 1f);

        Soldier s = solObj.AddComponent<Soldier>();
        s.ownerActorNumber = ownerActor;
        s.soldierType = type;
        s.weaponMaterial = material;
        s.netId = forcedNetId > 0 ? forcedNetId : NextId(ownerActor);
        s.moveSpeed = 1.5f;
        s.PrepareVisualsOnly = !SoldierIsOwnedLocally(ownerActor); // Fremde Soldaten nicht simulieren

        if (SoldierIsOwnedLocally(ownerActor))
        {
            FogRevealer fr = solObj.AddComponent<FogRevealer>();
            fr.radius = 4f;
            fr.isLocalPlayer = true;
        }

        if (broadcast && PhotonNetwork.InRoom)
        {
            Raise(12, Soldier.SerializeSpawnEvent(s), true); // SoldierSpawnEventCode = 12
        }

        return s;
    }

    public static bool SoldierIsOwnedLocally(int ownerActor)
    {
        if (!PhotonNetwork.InRoom)
        {
            return teamIsSinglePlayer(ownerActor);
        }
        return ownerActor == LocalActorId;
    }

    private static bool teamIsSinglePlayer(int ownerActor)
    {
        return ownerActor == 0 || ownerActor == 1;
    }
}

/// <summary>
/// nimmt die neuen Kampfevents entgegen (Codes 22–25). Wird pro Szene einmal erstellt.
/// </summary>
public class NetCombatRunner : MonoBehaviour, IOnEventCallback
{
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
        switch (photonEvent.Code)
        {
            case NetCombat.SoldierDamageEvent:
                if (photonEvent.CustomData is object[] dmgData) NetCombat.ReceiveSoldierDamage(dmgData);
                break;
            case NetCombat.SoldierDieEvent:
                if (photonEvent.CustomData is object[] dieData) NetCombat.ReceiveSoldierDie(dieData);
                break;
            case NetCombat.ShipDamageEvent:
                if (photonEvent.CustomData is object[] shipDmg) NetCombat.ReceiveShipDamage(shipDmg);
                break;
            case NetCombat.ShipDestroyEvent:
                if (photonEvent.CustomData is object[] shipDie) NetCombat.ReceiveShipDestroy(shipDie);
                break;
        }
    }
}

public static class NetCombatBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRunner()
    {
        if (Object.FindAnyObjectByType<NetCombatRunner>() == null)
        {
            GameObject go = new GameObject("NetCombatRunner");
            go.AddComponent<NetCombatRunner>();
        }
    }
}
