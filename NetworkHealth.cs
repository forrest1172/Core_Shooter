using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkHealth : NetworkBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float respawnDelay = 5f;

    [Header("Assign explicitly (recommended)")]
    [SerializeField] private Behaviour[] disableOnDeath; 
    [SerializeField] private CharacterController characterController;

    [SerializeField] private Canvas deathScreen;

    public NetworkVariable<int> CurrentHealth = new(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsDeadNet = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<double> RespawnAtServerTime = 
    new(0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<uint> RespawnId = new(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
    );



    PlayerStats stats;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        CurrentHealth.Value = maxHealth;
        IsDeadNet.Value = false;
    }


    public void TakeDamage(int amount, ulong attackerClientId)
    {
        if (!IsServer) return;
        if (IsDeadNet.Value) return;

        amount = Mathf.Max(0, amount);
        if (amount == 0) return;

        int newHp = Mathf.Max(0, CurrentHealth.Value - amount);
        CurrentHealth.Value = newHp;

        if (newHp == 0)
            HandleDeathServer(attackerClientId);
            
    }

    public void KillInstant(ulong attackerClientId)
    {
        if (!IsServer) return;
        if (IsDeadNet.Value) return;

        CurrentHealth.Value = 0;
        HandleDeathServer(attackerClientId);
    }

    void HandleDeathServer(ulong killerClientId)
    {
        if (!IsServer) return;

        IsDeadNet.Value = true;

        RespawnAtServerTime.Value = NetworkManager.ServerTime.Time + respawnDelay;
        
        // Victim death stat
        if (stats != null) stats.AddDeath();

        // Killer kill stat (ignore if no killer or suicide)
        if (killerClientId != ulong.MaxValue && killerClientId != OwnerClientId)
        {
            if (NetworkManager.ConnectedClients.TryGetValue(killerClientId, out var kc))
            {
                var killerStats = kc.PlayerObject != null ? kc.PlayerObject.GetComponent<PlayerStats>() : null;
                if (killerStats != null) killerStats.AddKill();
            }
        }

        // Optional: drop weapon on death
        var interactor = GetComponent<WeaponInteractor>();
        if (interactor != null) interactor.ForceDropServer();

        // Disable control/physics on everyone
        SetDeadStateClientRpc(true);

        StartCoroutine(RespawnRoutine());
        
    }

    IEnumerator RespawnRoutine()
    {
        
        
        yield return new WaitForSeconds(respawnDelay);

        if (!IsServer) yield break;
        if (NetworkObject == null) yield break;

        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (SpawnManager.Instance != null && SpawnManager.Instance.TryGetSpawnPose(out var p, out var r))
        {
            spawnPos = p;
            spawnRot = r;
        }

        RespawnId.Value++;
        uint rid = RespawnId.Value;


        TeleportServer(spawnPos, spawnRot);
        Debug.Log($"[SERVER] Respawn teleport {OwnerClientId} -> {transform.position}  t={NetworkManager.ServerTime.Time:0.000}");

        CurrentHealth.Value = maxHealth;
    
        IsDeadNet.Value = false;

        RespawnAtServerTime.Value = 0;

        TeleportObserversClientRpc(rid, spawnPos, spawnRot);

        RespawnOwnerClientRpc(rid, spawnPos, spawnRot, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        });

        

        SetDeadStateClientRpc(false);

    }

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    void TeleportObserversClientRpc(uint rid, Vector3 pos, Quaternion rot)
    {
        if (IsOwner) return; // owner handled separately

        // For observers, just snap visuals
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.SetPositionAndRotation(pos, rot);

        if (cc != null) cc.enabled = true;
        Debug.Log($"[OBSERVER] Saw respawn snap rid={rid} -> {transform.position}  localTime={Time.time:0.000}");

    }


    void TeleportServer(Vector3 pos, Quaternion rot)
    {
        if (!IsServer) return;

        var cc = GetComponent<CharacterController>();
        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();

        if (cc != null) cc.enabled = false;

        if (nt != null)
        {
            // IMPORTANT: ensure NT is enabled on the server so it can replicate
        if (!nt.enabled) nt.enabled = true;

        // Force an actual network teleport (resets interpolation for clients)
        nt.Teleport(pos, rot, transform.localScale);
        }
        else
        {
        // No NT present: fallback
        transform.SetPositionAndRotation(pos, rot);
        }

        if (cc != null) cc.enabled = true;
    }


    [ClientRpc]
    void SetDeadStateClientRpc(bool dead)
    {
        // IMPORTANT: assign disableOnDeath in inspector to only the scripts you want off.
        // Example: PlayerPredictedMotor..TODO:disable mesh renderer..maybe networked ragdoll?
        if (disableOnDeath != null)
        {
            foreach (var b in disableOnDeath)
            {
                if (b == null) continue;
                b.enabled = !dead;
            }
        }

        if (characterController != null)
            characterController.enabled = !dead;
        
         // CRITICAL: owner must NOT have server-authoritative NetworkTransform enabled
        var nt = GetComponent<NetworkTransform>();
        if (nt != null)
        {
            // Owner predicted on clients, but host is also server so NT can stay on there
            if (IsOwner && !IsServer) nt.enabled = false;
            else nt.enabled = true; // server & observers must have NT enabled
        }


        if (dead && IsOwner)
        {
            var motor = GetComponent<PlayerPredictedMotor>();
            if (motor != null) motor.SetInputEnabled(false);
        }

        var interactor = GetComponent<WeaponInteractor>();
        if (interactor != null)
            interactor.enabled = !dead;
        
        if (!dead && IsOwner)
        {
            var motor = GetComponent<PlayerPredictedMotor>();
            if (motor != null) motor.SetInputEnabled(true);
        }

    }

    
    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    void RespawnOwnerClientRpc(uint rid, Vector3 pos, Quaternion rot, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        // Hard teleport locally
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.SetPositionAndRotation(pos, rot);
        if (cc != null) cc.enabled = true;

        // Reset prediction + re-enable input in one atomic step
        var motor = GetComponent<PlayerPredictedMotor>();
        if (motor != null)
        {
            motor.SetRespawnId(rid);
            motor.ResetPredictionState();
            motor.SetInputEnabled(true);
        }
    }
}
