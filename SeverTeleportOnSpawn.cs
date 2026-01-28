using Unity.Netcode;
using UnityEngine;
using System.Collections;
using Unity.Netcode.Components;
using Unity.Mathematics;
using Unity.Services.Matchmaker.Models;

public class ServerTeleportOnSpawn : NetworkBehaviour
{
    NetworkTransform nt;
    CharacterController cc;

    public Transform visualRoot;

    void Awake()
    {
        nt = GetComponent<NetworkTransform>();
        cc = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        transform.position = new Vector3(10,5,10);
        StartCoroutine(InitialSpawn());
    }

    IEnumerator InitialSpawn()
    {
        // Wait for SpawnManager
        while (SpawnManager.Instance == null)
            yield return null;

        yield return null; // one frame safety
        yield return null; // one frame safety

        Transform spawn = SpawnManager.Instance.GetNextSpawn();
        if (spawn == null) yield break;

        //HARD DISABLE systems
        if (cc != null) cc.enabled = false;
        if (nt != null) nt.enabled = false;
        

        transform.SetPositionAndRotation(
            new Vector3(spawn.position.x, spawn.position.y + 5, spawn.position.z),
            quaternion.identity
        );
        //revokeAuthAndSpawnServerRpc(spawn.position);
        if (visualRoot)
        {
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localRotation = Quaternion.identity;
        }
        

        yield return null;
        yield return null;

        //RE-ENABLE after safe position
        
        if (nt != null) nt.enabled = true;
        if (cc != null) cc.enabled = true;
       
    }

    /*[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Server)]
    public void revokeAuthAndSpawnServerRpc(Vector3 spawnPos)
    {
         if (!IsServer) return;
        StartCoroutine(ForceTeleport(spawnPos));
        //teleport
        
    }

    private IEnumerator ForceTeleport(Vector3 spawnPos)
    {
        
        nt.enabled = false;

        yield return null;
        yield return null;

        visualRoot.transform.position = spawnPos;
        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = quaternion.identity;
        
        yield return null;
        yield return null;
        nt.enabled = true;

        
    }*/
}