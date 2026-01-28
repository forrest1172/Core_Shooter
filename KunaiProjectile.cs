using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class KunaiProjectile : NetworkBehaviour
{

    [Header("Stick")]
    [SerializeField] Transform tip;          // assign in prefab to the knife tip
    [SerializeField] float embedDepth = 0.06f;
    [SerializeField] float minStickSpeed = 3.0f;
    [SerializeField] LayerMask stickMask = ~0;


    [Header("Flight")]
    [SerializeField] float lifeSeconds = 12f;
    [SerializeField] float stickDepth = 0.02f;

    Rigidbody rb;
    Collider col;

    public new ulong OwnerClientId { get; private set; }
    public bool HasImpacted { get; private set; }
    public Vector3 ImpactPoint { get; private set; }
    public Quaternion ImpactRotation { get; private set; }

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();

        if (IsServer)
        {
            Invoke(nameof(DespawnSelf), lifeSeconds);
        }
    }

    public void ServerInit(ulong ownerClientId, Vector3 velocity)
    {
        if (!IsServer) return;

        OwnerClientId = ownerClientId;
        HasImpacted = false;

        rb.isKinematic = false;
        rb.linearVelocity = velocity;
        rb.angularVelocity = Vector3.zero;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;
        if (HasImpacted) return;

        // ignore non-stick layers if you want
        if (((1 << collision.gameObject.layer) & stickMask) == 0) return;

        float speed = rb.linearVelocity.magnitude;
        if (speed < minStickSpeed) return; // too slow, just bounce/stop (optional)

        HasImpacted = true;

        var cp = collision.GetContact(0);
        Vector3 normal = cp.normal;

        // We want the blade forward (tip direction) to point INTO the surface => -normal
        Quaternion stickRot = Quaternion.LookRotation(-normal, Vector3.up);

        // If you have a tip transform, offset so the TIP is embedded into the surface
        if (tip != null)
        {
        // After rotation, where will the tip be?
        // Compute local offset from projectile root to tip.
        Vector3 tipLocal = tip.localPosition;

        // World-space offset of tip from root given our stick rotation
        Vector3 tipWorldOffset = stickRot * tipLocal;

        // Place root so tip sits slightly inside the surface
        Vector3 rootPos = cp.point + (-normal * embedDepth) - tipWorldOffset;

        ImpactPoint = rootPos;
        ImpactRotation = stickRot;

        rb.isKinematic = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.SetPositionAndRotation(rootPos, stickRot);
        }
        else
        {
            // Fallback: place root at contact point, embed a bit
            ImpactPoint = cp.point + (-normal * embedDepth);
            ImpactRotation = stickRot;

            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            transform.SetPositionAndRotation(ImpactPoint, ImpactRotation);
        }
}


    void DespawnSelf()
    {
        if (!IsServer) return;
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    public void ServerForceDespawn()
    {
        if (!IsServer) return;
        CancelInvoke(nameof(DespawnSelf));
        DespawnSelf();
    }
}
