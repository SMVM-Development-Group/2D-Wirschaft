using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class PlayerCombatHealth : MonoBehaviourPun
{
    public int maxHP = 120;
    public int currentHP = 120;

    // Zentrales Register statt FindObjectsOfType pro Frame
    public static readonly List<PlayerCombatHealth> All = new List<PlayerCombatHealth>();

    private void Awake()
    {
        currentHP = Mathf.Max(1, maxHP);
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    public bool IsHostileTo(bool localBuilding)
    {
        if (photonView == null) return false;
        return localBuilding ? !photonView.IsMine : photonView.IsMine;
    }

    public void TakeDamage(int damage)
    {
        currentHP -= Mathf.Max(0, damage);
        if (currentHP <= 0)
        {
            currentHP = 0;
            Die();
        }
    }

    private void Die()
    {
        gameObject.SetActive(false);
        NotificationManager.Instance?.Notify("player_defeated", "Ein Spieler wurde vom Turm ausgeschaltet.", 6f);
    }
}
