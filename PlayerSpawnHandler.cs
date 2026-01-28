using UnityEngine;
using Unity.Netcode;

public class PlayerSpawnHandler : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Delay one frame so NGO is fully ready
        StartCoroutine(ServerTeleportRoutine());
    }

    private System.Collections.IEnumerator ServerTeleportRoutine()
    {
        yield return null;

        Vector3 spawnPos = SpawnManager.Instance.GetNextSpawn().position;
        Debug.Log($"[SERVER] Teleporting player {OwnerClientId} to {spawnPos}");

        transform.position = spawnPos;
    }
}