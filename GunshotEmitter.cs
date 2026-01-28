using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GunshotEmitter : NetworkBehaviour
{
    [Header("Hearing (meters)")]
    [SerializeField] float unsuppressedRadius = 60f;
    [SerializeField] float suppressedRadius = 25f;

    [Header("Loudness")]
    [Range(0f, 2f)] [SerializeField] float unsuppressedLoudness = 1.0f;
    [Range(0f, 2f)] [SerializeField] float suppressedLoudness = 0.7f;

    readonly List<ulong> targets = new List<ulong>(64);

    /// <summary>
    /// Call on SERVER when a shot is confirmed (hitscan fired).
    /// </summary>
    public void EmitGunshotServer(Vector3 atPos, bool suppressed, ulong shooterClientId)
    {
        if (!IsServer || NetworkManager.Singleton == null) return;

        float radius = suppressed ? suppressedRadius : unsuppressedRadius;
        float loudness = suppressed ? suppressedLoudness : unsuppressedLoudness;

        targets.Clear();

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            // Optional: don't send to shooter; shooter can play locally instantly to avoid latency/double sound
            if (clientId == shooterClientId) continue;

            var playerNO = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerNO == null) continue;

            if (Vector3.Distance(playerNO.transform.position, atPos) <= radius)
                targets.Add(clientId);
        }

        if (targets.Count > 0)
        {
            PlayGunshotClientRpc(atPos, loudness,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = targets.ToArray() }
                });
        }
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    void PlayGunshotClientRpc(Vector3 pos, float loudness01, ClientRpcParams rpcParams = default)
    {
        if (TacticalAudioManager.Instance == null) return;
        TacticalAudioManager.Instance.Play3D(TacticalSfxId.Gunshot, pos, loudness01);
    }
}

