using Unity.Netcode;
using UnityEngine;

public class HitscanWeapon : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Weapon")]
    [SerializeField] private float range = 150f;
    [SerializeField] private int damage = 20;
    [SerializeField] private float fireRate = 10f;

    [Header("FX")]
    [Tooltip("Prefab with a ParticleSystem or light flash. Should be a normal (non-networked) prefab.")]
    [SerializeField] private GameObject muzzleFlashPrefab;

    [Tooltip("Prefab for bullet impact. Should be a normal (non-networked) prefab.")]
    [SerializeField] private GameObject impactPrefab;

    [Tooltip("How long before destroying spawned muzzle flash object.")]
    [SerializeField] private float muzzleFlashLifetime = 0.5f;

    float nextFireTime;
    double serverNextAllowedFireTime;

    NetworkWeapon netWeapon;

    void Awake()
    {
        if (muzzle == null) muzzle = transform;
        netWeapon = GetComponent<NetworkWeapon>();
    }

    void Update()
    {
        // Only the weapon owner can shoot
        if (!IsOwner) return;

        // Must be equipped by owner (prevents shooting while on ground)
        if (netWeapon != null && (!netWeapon.IsEquipped || netWeapon.EquippedByClientId.Value != OwnerClientId))
            return;

        if (Input.GetButton("Fire1") && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));
            Fire();
        }
    }

    void Fire()
    {
        var cam = Camera.main;
        if (!cam) return;

        // Camera aim, muzzle origin
        Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 dir = aimRay.direction.normalized;

        //use muzzle.postion to get the orgin of the muzzle
        //exact orgin is used rn
        Vector3 origin = aimRay.origin;

        // Immediate local feel (optional): play muzzle flash locally too
        PlayMuzzleFlashLocal();

        FireServerRpc(origin, dir);
        if (IsOwner && TacticalAudioManager.Instance != null)
        {
            Vector3 soundPos = muzzle.transform != null ? muzzle.position : transform.position;
            TacticalAudioManager.Instance.Play3D(TacticalSfxId.Gunshot, soundPos, 1f);
        }

    }

    void PlayMuzzleFlashLocal()
    {
        if (muzzleFlashPrefab == null || muzzle == null) return;
        var go = Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation);
        Destroy(go, muzzleFlashLifetime);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    void FireServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != OwnerClientId) return;

        // Must still be equipped by sender
        if (netWeapon != null && (!netWeapon.IsEquipped || netWeapon.EquippedByClientId.Value != sender))
            return;

        double now = NetworkManager.ServerTime.Time;
        double minInterval = 1.0 / Mathf.Max(0.01f, fireRate);
        if (now < serverNextAllowedFireTime) return;
        serverNextAllowedFireTime = now + minInterval;

        direction = direction.normalized;

        // Anti-cheese: clamp origin near server muzzle
        if (muzzle != null && Vector3.Distance(origin, muzzle.position) > 1.5f)
            origin = muzzle.position;

        // Tell everyone to play muzzle flash on THIS weapon’s muzzle
        // (Observers will see the flash at the correct weapon in the shooter’s hands)
        PlayMuzzleFlashClientRpc();

        // SERVER: after confirming shot
        var gunshot = GetComponent<GunshotEmitter>();
        if (gunshot != null)
        {
            Vector3 soundPos = muzzle.transform != null ? muzzle.position : transform.position;
            gunshot.EmitGunshotServer(soundPos, suppressed: false, shooterClientId: OwnerClientId);
        }


        if (Physics.Raycast(origin, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null)
                dmg.TakeDamage(damage, sender);

            SpawnImpactClientRpc(hit.point, hit.normal);
        }
    }

    [ClientRpc]
    void PlayMuzzleFlashClientRpc()
    {
        // Avoid double flash for shooter since they already played local (optional)
        if (IsOwner) return;

        if (muzzleFlashPrefab == null || muzzle == null) return;
        var go = Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation);
        Destroy(go, muzzleFlashLifetime);
    }

    [ClientRpc]
    void SpawnImpactClientRpc(Vector3 point, Vector3 normal)
    {
        if (impactPrefab == null) return;
        Destroy(Instantiate(impactPrefab, point, Quaternion.LookRotation(normal)), 2f);
    }
}
