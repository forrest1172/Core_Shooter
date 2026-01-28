using Unity.Netcode;
using UnityEngine;

public class SfxRpcTest : NetworkBehaviour
{
    void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log("[TEST] Owner pressed P -> calling ServerRpc");
            TestServerRpc();
        }
    }

    [ServerRpc]
    void TestServerRpc(ServerRpcParams rpcParams = default)
    {
        Debug.Log("[TEST][SERVER] ServerRpc received -> sending ClientRpc to all");
        TestClientRpc();
    }

    [ClientRpc]
    void TestClientRpc(ClientRpcParams rpcParams = default)
    {
        Debug.Log("[TEST][CLIENT] ClientRpc received!");

        if (TacticalAudioManager.Instance != null)
            TacticalAudioManager.Instance.Play3D(TacticalSfxId.Footstep_Default, transform.position, 1f);
    }
}
