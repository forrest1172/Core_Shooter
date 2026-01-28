using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkSoundEmitter : NetworkBehaviour
{
    public static NetworkSoundEmitter Instance;

    void Awake() => Instance = this;

    // Server-only: send sound to only clients in radius
    public void EmitSound(TacticalSfxId id, Vector3 pos, float radius, float loudness01 = 1f)
    {
        if (!IsServer) return;

        List<ulong> targets = ListPoolUlong.Get();
        foreach (var kv in NetworkManager.ConnectedClients)
        {
            ulong clientId = kv.Key;
            var playerObj = kv.Value.PlayerObject;
            if (playerObj == null) continue;

            float dist = Vector3.Distance(playerObj.transform.position, pos);
            if (dist <= radius)
                targets.Add(clientId);
        }
        Debug.Log($"[SFX][SERVER] Emit {id} at {pos} radius={radius} targets={targets.Count} IsSpawned={NetworkObject != null && NetworkObject.IsSpawned}");

        if (targets.Count > 0)
        {
            PlaySoundClientRpc((byte)id, pos, Mathf.Clamp01(loudness01),
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = targets.ToArray() }
                });
        }

        ListPoolUlong.Release(targets);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    void PlaySoundClientRpc(byte id, Vector3 pos, float loudness01, ClientRpcParams rpcParams = default)
    {
        
        if (TacticalAudioManager.Instance == null) return;
        TacticalAudioManager.Instance.Play3D((TacticalSfxId)id, pos, loudness01);
        Debug.Log($"[SFX][CLIENT] PlaySoundClientRpc id={(TacticalSfxId)id} at {pos} loud={loudness01} hasMgr={(TacticalAudioManager.Instance!=null)}");

    }
}

static class ListPoolUlong
{
    static readonly Stack<List<ulong>> pool = new();
    public static List<ulong> Get() => pool.Count > 0 ? pool.Pop() : new List<ulong>(32);
    public static void Release(List<ulong> list) { list.Clear(); pool.Push(list); }
}
