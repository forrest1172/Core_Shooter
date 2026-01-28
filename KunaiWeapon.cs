using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

public class KunaiWeapon : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] NetworkWeapon networkWeapon;          // your existing component
    [SerializeField] Transform throwOrigin;                // where the kunai spawns from (hand/muzzle)
    [SerializeField] KunaiProjectile projectilePrefab;     // MUST be a NetworkPrefab
    [SerializeField] LayerMask meleeMask = ~0;

    [Header("Throw")]
    [SerializeField] float throwSpeed = 28f;
    [SerializeField] float throwUp = 0.15f;
    [SerializeField] float throwCooldown = 0.35f;
    [SerializeField] float maxTeleportDistance = 80f;      // anti-abuse limit

    [Header("Melee")]
    [SerializeField] float meleeRange = 2.2f;
    [SerializeField] float meleeCooldown = 0.35f;
    [SerializeField] int meleeDamage = 40;

    [Header("Teleport")]
    [SerializeField] KeyCode teleportKey = KeyCode.Q;
    [SerializeField] bool teleportToImpactIfAvailable = true;
    [SerializeField] float teleportCooldown = 0.5f;

    KunaiProjectile lastThrownServer;          // server authoritative reference
    NetworkObjectReference lastThrownRefNet;   // replicated reference to owner (best-effort)
    float nextThrowTime;
    float nextMeleeTime;
    float nextTeleportTime;

    void Awake()
    {
        if (networkWeapon == null) networkWeapon = GetComponent<NetworkWeapon>();
        if (throwOrigin == null) throwOrigin = transform;
    }

    void Update()
    {
        if (!IsOwner) return;
        if (!IsEquippedByMe()) return;

        var health = GetComponentInParent<NetworkHealth>();
        if (health != null && health.IsDeadNet.Value) return;

        if (Input.GetMouseButtonDown(0))
            TryThrowOwner();

        if (Input.GetMouseButtonDown(1))
            TryMeleeOwner();

        if (Input.GetKeyDown(teleportKey))
            TryTeleportOwner();
    }

    bool IsEquippedByMe()
    {
        if (networkWeapon == null) return false;
        if (!networkWeapon.IsEquipped) return false;
        // assumes your NetworkWeapon has EquippedByClientId.Value
        return networkWeapon.EquippedByClientId.Value == OwnerClientId;
    }

    void TryThrowOwner()
    {
        if (Time.time < nextThrowTime) return;
        nextThrowTime = Time.time + throwCooldown;

        Vector3 origin = throwOrigin ? throwOrigin.position : transform.position;
        Vector3 dir = GetAimDirection();
        Vector3 vel = (dir + Vector3.up * throwUp).normalized * throwSpeed;

        ThrowServerRpc(origin, Quaternion.LookRotation(dir, Vector3.up), vel);
    }

    void TryMeleeOwner()
    {
        if (Time.time < nextMeleeTime) return;
        nextMeleeTime = Time.time + meleeCooldown;

        Vector3 origin = throwOrigin ? throwOrigin.position : transform.position;
        Vector3 dir = GetAimDirection();

        MeleeServerRpc(origin, dir);
    }

    void TryTeleportOwner()
    {
        if (Time.time < nextTeleportTime) return;
        nextTeleportTime = Time.time + teleportCooldown;

        TeleportToKunaiServerRpc();
    }

    Vector3 GetAimDirection()
    {
        // Aim from center screen if possible
        var cam = Camera.main;
        if (cam)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return ray.direction.normalized;
        }
        return transform.forward;
    }

    // ---------------- SERVER ----------------
    //need to change this to updated ngo 6.2 
    //very unreliable maybe change to reliable only packets since this should be rare and not spammable
    //[ServerRpc(RequireOwnership = false)]
    [ServerRpc(Delivery = RpcDelivery.Reliable)]
    void ThrowServerRpc(Vector3 origin, Quaternion rot, Vector3 velocity, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        // Validate sender is the equipped owner
        if (networkWeapon == null || !networkWeapon.IsEquipped) return;
        if (networkWeapon.EquippedByClientId.Value != sender) return;

        // Optional: prevent throw while dead
        var health = GetComponentInParent<NetworkHealth>();
        if (health != null && health.IsDeadNet.Value) return;

        // Despawn previous thrown kunai if it still exists (so you only have one active)
        if (lastThrownServer != null && lastThrownServer.NetworkObject != null && lastThrownServer.NetworkObject.IsSpawned)
            lastThrownServer.ServerForceDespawn();

        // Spawn projectile
        var proj = Instantiate(projectilePrefab, origin, rot);
        var no = proj.GetComponent<NetworkObject>();
        no.Spawn(true);

        proj.ServerInit(sender, velocity);

        lastThrownServer = proj;
        lastThrownRefNet = new NetworkObjectReference(no);

        // Tell owner the current kunai reference (so teleport key works even if you join late)
        SetLastKunaiOwnerClientRpc(lastThrownRefNet, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { sender } }
        });
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    void MeleeServerRpc(Vector3 origin, Vector3 dir, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (networkWeapon == null || !networkWeapon.IsEquipped) return;
        if (networkWeapon.EquippedByClientId.Value != sender) return;

        var health = GetComponentInParent<NetworkHealth>();
        if (health != null && health.IsDeadNet.Value) return;

        // Server raycast melee
        if (Physics.Raycast(origin, dir, out var hit, meleeRange, meleeMask, QueryTriggerInteraction.Ignore))
        {
            var dmg = hit.collider.GetComponent<IDamageable>();
            if (dmg != null)
            {
                dmg.TakeDamage(meleeDamage, sender);
            }
        }
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    void TeleportToKunaiServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (networkWeapon == null || !networkWeapon.IsEquipped) return;
        if (networkWeapon.EquippedByClientId.Value != sender) return;

        var health = GetComponentInParent<NetworkHealth>();
        if (health != null && health.IsDeadNet.Value) return;

        // We keep the authoritative reference on server
        if (lastThrownServer == null || lastThrownServer.NetworkObject == null || !lastThrownServer.NetworkObject.IsSpawned)
            return;

        Vector3 targetPos;
        var playerNO = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(sender);
        float yaw = playerNO.transform.eulerAngles.y;
        Quaternion targetRot; 

        if (teleportToImpactIfAvailable && lastThrownServer.HasImpacted)
        {
            targetPos = lastThrownServer.ImpactPoint;
            targetRot = Quaternion.Euler(0f, yaw, 0f);
        }
        else
        {
            targetPos = lastThrownServer.transform.position;
            targetRot = Quaternion.Euler(0f, yaw, 0f);
                        //lastThrownServer.transform.rotation; if ya want some whacky
        }

        // Anti-abuse: limit teleport distance
        var playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(sender);
        if (playerObj == null) return;

        float dist = Vector3.Distance(playerObj.transform.position, targetPos);
        if (dist > maxTeleportDistance) return;

        // Teleport player (server authoritative)
        TeleportPlayerServer(playerObj, targetPos, targetRot);

        // Optionally despawn kunai after teleport
        // lastThrownServer.ServerForceDespawn();
    }

    void TeleportPlayerServer(NetworkObject playerNO, Vector3 pos, Quaternion rot)
    {
        var cc = playerNO.GetComponent<CharacterController>();
        var nt = playerNO.GetComponent<Unity.Netcode.Components.NetworkTransform>();

        if (cc != null) cc.enabled = false;

        if (nt != null && nt.enabled)
            nt.Teleport(pos, rot, playerNO.transform.localScale);
        else
            playerNO.transform.SetPositionAndRotation(pos, rot);

        if (cc != null) cc.enabled = true;

        // Make predicted owner reset so it doesn’t rubber band
        ResetPredictionOwnerClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { playerNO.OwnerClientId } }
        });
    }

    // ---------------- CLIENT (OWNER) ----------------

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    void SetLastKunaiOwnerClientRpc(NetworkObjectReference projRef, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        lastThrownRefNet = projRef;
    }

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    void ResetPredictionOwnerClientRpc(ClientRpcParams rpcParams = default)
    {
        // runs only for target owner due to rpcParams targeting
        var motor = GetComponentInParent<PlayerPredictedMotor>();
        if (motor != null)
            motor.ResetAfterTeleport(resetPitch:true);
    }
}
