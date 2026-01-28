using Unity.Netcode;
using UnityEngine;

public class WeaponInteractor : NetworkBehaviour
{
    [Header("Pickup")]
    [SerializeField] private float pickupRange = 2.25f;
    [SerializeField] private LayerMask weaponMask = ~0;

    [Header("Drop")]
    [SerializeField] private float dropForward = 1.0f;
    [SerializeField] private float dropUp = 0.25f;
    [SerializeField] private float throwStrength = 2.5f;

    [SerializeField] private NetworkWeapon equipped;

    private NetworkVariable<NetworkObjectReference> equippedRef = new(
    default,
    NetworkVariableReadPermission.Owner,
    NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            equippedRef.OnValueChanged += OnEquippedRefChanged;
            // resolve immediately in case we joined late
            TryResolveEquipped();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
            equippedRef.OnValueChanged -= OnEquippedRefChanged;
    }

    void OnEquippedRefChanged(NetworkObjectReference oldRef, NetworkObjectReference newRef)
    {
        TryResolveEquipped();
    }

    void TryResolveEquipped()
    {
        if (!IsOwner) return;

        if (equippedRef.Value.TryGet(out var obj))
        {
            equipped = obj.GetComponent<NetworkWeapon>();
        }
        else
        {
            equipped = null;
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        if (equipped != null)
        {
        // If the weapon got despawned or no longer considers itself equipped by me, clear it
            if (!equipped.NetworkObject.IsSpawned ||
            !equipped.IsEquipped ||
            equipped.EquippedByClientId.Value != OwnerClientId)
            {
            equipped = null;
            }
        }

        if (Input.GetKeyDown(KeyCode.E))
            TryPickup();

        if (Input.GetKeyDown(KeyCode.G))
            TryDrop();
    }

    void TryPickup()
    {
        var health = GetComponent<NetworkHealth>();
        if (health != null && health.IsDeadNet.Value) return;
        if (!equippedRef.Value.Equals(default)) return; //already equipped (server says so)
        if (equipped != null) return;//idk i guess this does something? even though the server is always right. right? unity?

        var cam = Camera.main;
        if (!cam) return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out var hit, pickupRange, weaponMask, QueryTriggerInteraction.Ignore))
        {
            var weapon = hit.collider.GetComponentInParent<NetworkWeapon>();
            if (weapon == null) return;

            RequestEquipServerRpc(new NetworkObjectReference(weapon.NetworkObject));
        }
    }

    void TryDrop()
    {
        if (!IsOwner) return;

        var health = GetComponent<NetworkHealth>();
        if (health != null && health.IsDeadNet.Value) return;

        if (equipped == null) return;

        RequestDropServerRpc();
    }

    [ServerRpc]
    void RequestEquipServerRpc(NetworkObjectReference weaponRef, ServerRpcParams rpcParams = default)
    {
        if (!weaponRef.TryGet(out var weaponObj)) return;

        var weapon = weaponObj.GetComponent<NetworkWeapon>();
        if (weapon == null) return;
        if (weapon.IsEquipped) return;

        ulong sender = rpcParams.Receive.SenderClientId;
        var playerObj = NetworkManager.ConnectedClients[sender].PlayerObject;
        if (playerObj == null) return;

        // Ensure player doesn't already have a weapon
        var interactor = playerObj.GetComponent<WeaponInteractor>();
        if (interactor == null) return;
        if (interactor.equipped != null) return;

        // Equip
        weapon.EquipServer(sender, playerObj);

        // Server truth
        interactor.equippedRef.Value = new NetworkObjectReference(weaponObj);

        interactor.equipped = weapon;//server sided cache?

    }

    [ServerRpc]
    void RequestDropServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        var playerObj = NetworkManager.ConnectedClients[sender].PlayerObject;
        if (playerObj == null) return;

        var interactor = playerObj.GetComponent<WeaponInteractor>();
        if (interactor == null) return;

        var weap = interactor.equipped;
        if (weap == null) return;

        // Compute drop on SERVER (prevents spoofing and avoids mismatch)
        Vector3 dropPos = playerObj.transform.position
                      + playerObj.transform.forward * dropForward
                      + Vector3.up * dropUp;

        Vector3 throwVel = (playerObj.transform.forward + Vector3.up * 0.15f).normalized * throwStrength;

        weap.DropServer(dropPos, throwVel);
        // Clear server truth
        interactor.equippedRef.Value = default;
        interactor.equipped = null;
        
    }


    public void ForceDropServer()
    {
        if (!IsServer) return;
        if (equipped == null) return;
        
        //dont need this anymore. but maybe the new way will break too
        //ulong owner = NetworkObject.OwnerClientId;

        Vector3 dropPos = transform.position + transform.forward * 1.0f + Vector3.up * 0.25f;
        Vector3 throwVel = (transform.forward + Vector3.up * 0.15f).normalized * 2.5f;

        equipped.DropServer(dropPos, throwVel);

        equippedRef.Value = default;
        equipped = null;
        
    }

}
