using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkWeapon : NetworkBehaviour
{
    [Header("Grip (required)")]
    [SerializeField] private Transform gripPoint;

    [Header("World Physics")]
    [SerializeField] private Collider pickupCollider;
    [SerializeField] private Rigidbody rb;

    [Header("Socket")]
    [SerializeField] private string socketName = "HandSocket";

    NetworkTransform nt;

    // Owner-only extra glue
    Transform localFollowSocket;
    bool followLocally;

    public NetworkVariable<ulong> EquippedByClientId = new(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsEquipped => EquippedByClientId.Value != ulong.MaxValue;

    void Awake()
    {
        if (pickupCollider == null) pickupCollider = GetComponentInChildren<Collider>();
        if (rb == null) rb = GetComponent<Rigidbody>();
        nt = GetComponent<NetworkTransform>();
    }

    void LateUpdate()
    {
        // Only owner hard-follows to be perfectly attached to predicted player motion.
        if (!followLocally || localFollowSocket == null) return;
        AttachUtils.AlignGripToSocket(transform, gripPoint, localFollowSocket);
    }

    // ---------------- SERVER: EQUIP / DROP ----------------

    public void EquipServer(ulong newOwnerClientId, NetworkObject playerObj)
    {
        if (!IsServer) return;
        if (IsEquipped) return;
        if (playerObj == null) return;

        // Give ownership to holder so they can fire from this weapon
        if (NetworkObject.OwnerClientId != newOwnerClientId)
            NetworkObject.ChangeOwnership(newOwnerClientId);

        EquippedByClientId.Value = newOwnerClientId;

        // Disable world physics
        SetWorldPhysicsEnabled(false);

        // IMPORTANT:
        // Do NOT TrySetParent to HandSocket (not a NetworkObject).
        // Optionally parent to player root NetworkObject (allowed), or leave unparented.
        // This helps keep weapon near player on server for sanity.
        NetworkObject.TrySetParent(playerObj.transform, worldPositionStays: true);

        // Disable NT while equipped so it doesn't lag behind a predicted player
        if (nt != null) nt.enabled = false;

        // Tell all clients to visually attach to HandSocket (normal Transform parenting)
        EquipClientRpc(
            new NetworkObjectReference(playerObj),
            new NetworkObjectReference(NetworkObject),
            newOwnerClientId
        );
    }

    public void DropServer(Vector3 dropPos, Vector3 throwVel)
    {
        if (!IsServer) return;
        if (!IsEquipped) return;

        EquippedByClientId.Value = ulong.MaxValue;

        // Server owns dropped weapon
        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        // Remove parent (allowed since parent has NetworkObject now)
        NetworkObject.TryRemoveParent(worldPositionStays: true);
        transform.position = dropPos;

        // Enable world physics
        SetWorldPhysicsEnabled(true);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(throwVel, ForceMode.VelocityChange);
        }

        // Re-enable NT when dropped
        if (nt != null) nt.enabled = true;

        DropClientRpc(new NetworkObjectReference(NetworkObject), dropPos, throwVel);
    }

    // ---------------- CLIENT: VISUAL ATTACH / DETACH ----------------

    [ClientRpc]
    void EquipClientRpc(NetworkObjectReference playerRef, NetworkObjectReference weaponRef, ulong newOwnerClientId)
    {
        if (!playerRef.TryGet(out var playerObj)) return;
        if (!weaponRef.TryGet(out var weaponObj)) return;

        var weapon = weaponObj.GetComponent<NetworkWeapon>();
        if (weapon == null) return;

        var socket = weapon.FindSocket(playerObj.transform);
        if (socket == null) return;

        // Visual parenting (allowed even if socket isn't a NetworkObject)
        weapon.transform.SetParent(socket, worldPositionStays: true);

        // Disable NT locally while equipped to prevent interpolation lag
        var localNt = weaponObj.GetComponent<NetworkTransform>();
        if (localNt != null) localNt.enabled = false;

        // Align pose
        AttachUtils.AlignGripToSocket(weapon.transform, weapon.gripPoint, socket);

        // Disable physics visuals immediately
        weapon.SetWorldPhysicsEnabled(false);

        // Only the owning client hard-follows every LateUpdate for perfect attachment
        if (weaponObj.IsOwner && weaponObj.OwnerClientId == newOwnerClientId)
        {
            weapon.localFollowSocket = socket;
            weapon.followLocally = true;
        }
        else
        {
            weapon.localFollowSocket = null;
            weapon.followLocally = false;
        }
    }

    [ClientRpc]
    void DropClientRpc(NetworkObjectReference weaponRef, Vector3 dropPos, Vector3 throwVel)
    {
        if (!weaponRef.TryGet(out var weaponObj)) return;

        var weapon = weaponObj.GetComponent<NetworkWeapon>();
        if (weapon == null) return;

        weapon.followLocally = false;
        weapon.localFollowSocket = null;

        weapon.transform.SetParent(null, worldPositionStays: true);
        weapon.transform.position = dropPos;

        // Re-enable NT so dropped weapon can be smoothed
        var localNt = weaponObj.GetComponent<NetworkTransform>();
        if (localNt != null) localNt.enabled = true;

        weapon.SetWorldPhysicsEnabled(true);

        // Visual-only impulse
        var r = weaponObj.GetComponent<Rigidbody>();
        if (r != null)
            r.AddForce(throwVel, ForceMode.VelocityChange);
    }

    // ---------------- HELPERS ----------------

    Transform FindSocket(Transform playerRoot)
    {
        if (playerRoot == null) return null;

        var socket = playerRoot.Find(socketName);
        if (socket != null) return socket;

        foreach (var t in playerRoot.GetComponentsInChildren<Transform>(true))
            if (t.name == socketName)
                return t;

        return null;
    }

    void SetWorldPhysicsEnabled(bool enabled)
    {
        if (pickupCollider != null) pickupCollider.enabled = enabled;

        if (rb != null)
        {
            rb.isKinematic = !enabled;
            rb.useGravity = enabled;
        }
    }
}
