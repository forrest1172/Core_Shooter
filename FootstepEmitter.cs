using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FootstepEmitter : NetworkBehaviour
{
    [Header("Step timing")]
    [SerializeField] float walkStepInterval = 0.55f;
    [SerializeField] float runStepInterval  = 0.38f;

    [Header("Hearing radius (tactical)")]
    [SerializeField] float walkRadius = 14f;
    [SerializeField] float runRadius  = 22f;

    [Header("Loudness")]
    [SerializeField] float walkLoudness = 0.85f;
    [SerializeField] float runLoudness  = 1.0f;

    [Header("Surface detection (optional)")]
    [SerializeField] bool useSurface = true;
    [SerializeField] float groundCheckDist = 1.6f;

    [Header("Input threshold")]
    [SerializeField] float minMoveInput = 0.15f;

    [Header("Host fallback")]
    [Tooltip("If your host player doesn't go through SendInputServerRpc, this makes host footsteps work anyway.")]
    [SerializeField] bool hostFallbackUsesInput = true;

    CharacterController cc;
    float nextStepTime;

    readonly List<ulong> targets = new List<ulong>(32);

    void Awake() => cc = GetComponent<CharacterController>();

    void Update()
    {
        // Host-only fallback: if the host isn't calling ServerOnMovementSimulated from motor/serverRPC,
        // we call it here using local Input. This runs only on host.
        if (!hostFallbackUsesInput) return;
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsHost) return; // host only
        if (!IsServer || !IsOwner) return;            // this player's server+owner instance only

        var health = GetComponent<NetworkHealth>();
        if (health != null && health.IsDeadNet.Value) return;

        // Use input as the "move happened" signal
        Vector2 move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        ServerOnMovementSimulated(move, sprint);
    }

    /// <summary>
    /// Call this on SERVER after you simulate a movement tick (remote clients).
    /// Host can also use the fallback Update above.
    /// </summary>
    public void ServerOnMovementSimulated(Vector2 moveInput, bool sprint)
    {
        if (!IsServer) return;

        // Don't emit if dead
        var health = GetComponent<NetworkHealth>();
        if (health != null && health.IsDeadNet.Value) return;

        if (cc == null || !cc.isGrounded) return;

        if (moveInput.sqrMagnitude < (minMoveInput * minMoveInput))
            return;

        float interval = sprint ? runStepInterval : walkStepInterval;
        float radius   = sprint ? runRadius : walkRadius;
        float loudness = sprint ? runLoudness : walkLoudness;

        if (Time.time < nextStepTime) return;
        nextStepTime = Time.time + interval;

        TacticalSfxId stepId = TacticalSfxId.Footstep_Default;
        if (useSurface)
            stepId = DetectSurfaceStepId();

        // Build target list (who can hear it)
        targets.Clear();
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            var playerNO = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerNO == null) continue;

            if (Vector3.Distance(playerNO.transform.position, transform.position) <= radius)
                targets.Add(clientId);
        }

        if (targets.Count > 0)
        {
            PlayFootstepClientRpc((byte)stepId, transform.position, loudness,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = targets.ToArray() }
                });
        }
    }

    TacticalSfxId DetectSurfaceStepId()
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        if (Physics.Raycast(origin, Vector3.down, out var hit, groundCheckDist, ~0, QueryTriggerInteraction.Ignore))
        {
            string tag = hit.collider.tag;
            if (tag == "Metal") return TacticalSfxId.Footstep_Metal;
            if (tag == "Wood")  return TacticalSfxId.Footstep_Wood;
        }
        return TacticalSfxId.Footstep_Default;
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    void PlayFootstepClientRpc(byte sfxId, Vector3 pos, float loudness01, ClientRpcParams rpcParams = default)
    {
        // If you want proof it hits host, uncomment:
        // Debug.Log($"[SFX][CLIENT] recv step clientId={NetworkManager.Singleton.LocalClientId} sfx={(TacticalSfxId)sfxId}");

        if (TacticalAudioManager.Instance == null) return;
        TacticalAudioManager.Instance.Play3D((TacticalSfxId)sfxId, pos, loudness01);
    }

    // Manual test you can click on the component (gear icon / context menu)
    [ContextMenu("DEBUG Emit One Footstep (Server)")]
    void DebugEmitOneFootstep()
    {
        if (!IsServer) { Debug.LogWarning("DEBUG step requires server/host"); return; }
        ServerOnMovementSimulated(new Vector2(1, 0), false);
        Debug.Log("DEBUG step emitted");
    }
}
