using Unity.Netcode;
using UnityEngine;

public class KillBox : NetworkBehaviour
{
    [Tooltip("If true, kill counts as suicide (no killer).")]
    [SerializeField] private bool noKiller = true;

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var health = other.GetComponentInParent<NetworkHealth>();
        if (health == null) return;

        ulong killer = noKiller ? ulong.MaxValue : health.OwnerClientId;
        health.KillInstant(killer);
    }
}
