using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerPredictedMotor : NetworkBehaviour
{
    [Header("Speeds")]
    [SerializeField] float walkSpeed = 6.5f;
    [SerializeField] float runSpeed = 9.5f;
    [SerializeField] float airControlMultiplier = 0.9f;

    [Header("Jump/Gravity")]
    [SerializeField] float jumpForce = 6.5f;
    [SerializeField] float gravity = -18f;
    [SerializeField] float groundStickForce = -8f;

    [Header("Jump Tuning")]
    [SerializeField] float coyoteTime = 0.10f;
    [SerializeField] float jumpBufferTime = 0.12f;

    [Header("Look")]
    [SerializeField] float mouseSensitivity = 3f;
    [SerializeField] float maxLookAngle = 85f;
    [SerializeField] Transform cameraPivot; // pitch

    [Header("Prediction")]
    [SerializeField] int tickRate = 60;                 // simulation ticks/sec
    [SerializeField] int maxHistoryTicks = 120;         // history length in ticks
    [SerializeField] float posErrorSnap = 0.35f;        // meters
    [SerializeField] float rotErrorSnapDeg = 8f;        // degrees

    CharacterController cc;

    // Shared sim state (client predicted & server authoritative)
    Vector3 velocity;

    // Tick-based jump helpers
    uint lastGroundedTick;
    uint lastJumpPressedTick;
    uint coyoteTicks;
    uint jumpBufferTicks;

    // Owner-only camera pitch
    float pitch;

    // Tick state
    uint localTick;
    float tickDt;
    float tickAccumulator;

    // Mouse yaw accumulation (frame -> ticks)
    float pendingYawDeg;

    // Reconcile guard
    bool isReconciling;

    // Optional: ignore out-of-order server inputs
    uint lastServerSimTick;

    Vector2 cachedMove;
    bool cachedSprint;
    bool pendingJump;   // latched "jump pressed" until consumed by a tick

    PlayerPauseMenu pauseLogic;
    struct InputCmd
    {
        public uint tick;
        public Vector2 move;
        public bool jumpDown;
        public bool sprint;
        public float yawDeltaDeg; // degrees to rotate this tick
    }

    struct PredictedState
    {
        public uint tick;
        public Vector3 pos;
        public Vector3 vel;
        public float yawDeg;
    }

    readonly Dictionary<uint, InputCmd> inputHistory = new();
    readonly Dictionary<uint, PredictedState> stateHistory = new();

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        pauseLogic = GetComponent<PlayerPauseMenu>();
        if (cameraPivot == null) cameraPivot = transform.Find("CameraPivot");
    }

    uint currentRespawnId;        // set by NetworkHealth on respawn (owner)
    uint lastServerRespawnIdSeen; // server-side tracking (optional safety)

    public void SetRespawnId(uint id)
    {
        if (!IsOwner && !IsServer) return; // safe
        currentRespawnId = id;

        // Reset server tick guard per life (prevents old tick comparisons blocking new life)
        if (IsServer) lastServerSimTick = 0;
    }

    public override void OnNetworkSpawn()
    {
        // IMPORTANT: initialize tickDt for EVERYONE (server + clients + non-owners)
        tickDt = 1f / Mathf.Max(1, tickRate);

        // Precompute tick-based jump windows
        coyoteTicks = (uint)Mathf.CeilToInt(coyoteTime / tickDt);
        jumpBufferTicks = (uint)Mathf.CeilToInt(jumpBufferTime / tickDt);

        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (nt != null && IsOwner)
            nt.enabled = false;

        if (!IsOwner)
        {
            // Disable local camera on non-owner instances
            var cam = GetComponentInChildren<Camera>(true);
            if (cam) cam.gameObject.SetActive(false);
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

    }

    void Update()
    {
        if (!IsOwner) return;
        if(!inputEnabled) return;
        if (isReconciling) return;
        if(pauseLogic.isPaused) return;

        // --- Read mouse this frame ---
        // Convert frame mouse into a yaw delta in degrees and accumulate
        float mouseXFrame = Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
        float mouseYFrame = Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;

        pendingYawDeg += mouseXFrame;

        // Pitch can be per-frame (it doesn't affect server sim if you don't send it)
        pitch -= mouseYFrame;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);
        if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        cachedMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        cachedSprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Latch jump press so a short tap can't be missed by the tick loop
        if (Input.GetButtonDown("Jump"))
            pendingJump = true;

        // --- Fixed-rate tick accumulation ---
        tickAccumulator += Time.deltaTime;

        int ticksToRun = 0;
        while (tickAccumulator >= tickDt)
        {
            tickAccumulator -= tickDt;
            ticksToRun++;
        }

        if (ticksToRun <= 0) return;

        // Distribute yaw fairly across ticks
        float yawPerTick = pendingYawDeg / ticksToRun;
        pendingYawDeg = 0f;

        for (int i = 0; i < ticksToRun; i++)
            RunPredictedTick(yawPerTick);
    }

    public void ResetAfterTeleport(bool resetPitch)
    {
        ResetPredictionState();

        if (resetPitch)
            {
                pitch = 0f;
                if (cameraPivot) cameraPivot.localRotation = Quaternion.identity;
            }
    }


    void RunPredictedTick(float yawDeltaDeg)
    {
        Vector2 move = cachedMove;
        bool sprint = cachedSprint;
        bool jumpDown = pendingJump;
        pendingJump = false; // consume it on this tick

        localTick++;

        var cmd = new InputCmd
        {
            tick = localTick,
            move = move,
            jumpDown = jumpDown,
            sprint = sprint,
            yawDeltaDeg = yawDeltaDeg
        };

        inputHistory[localTick] = cmd;
        TrimDict(inputHistory);

        // HOST: simulate only once (authoritative), no RPC, no reconcile needed
        if (IsServer)
        {
            SimulateTick(cmd, tickDt, isServerSim: true);

            GetComponent<FootstepEmitter>()?.ServerOnMovementSimulated(cmd.move, cmd.sprint);

            // still store state so history doesn't explode / for debugging
            stateHistory[localTick] = CaptureState(localTick);
            TrimDict(stateHistory);
            return;
        }

        // CLIENT: predict locally, then send input to server
        SimulateTick(cmd, tickDt, isServerSim: false);
        stateHistory[localTick] = CaptureState(localTick);
        TrimDict(stateHistory);

        SendInputServerRpc(currentRespawnId, cmd.tick, cmd.move, cmd.jumpDown, cmd.sprint, cmd.yawDeltaDeg);
    }


    PredictedState CaptureState(uint tick)
    {
        return new PredictedState
        {
            tick = tick,
            pos = transform.position,
            vel = velocity,
            yawDeg = transform.eulerAngles.y
        };
    }

    void SimulateTick(InputCmd cmd, float dt, bool isServerSim)
    {
        // Apply yaw
        if (Mathf.Abs(cmd.yawDeltaDeg) > 0.0001f)
            transform.Rotate(Vector3.up * cmd.yawDeltaDeg);

        float speed = cmd.sprint ? runSpeed : walkSpeed;

        // Build movement vector
        Vector3 moveWorld = (transform.right * cmd.move.x + transform.forward * cmd.move.y);
        moveWorld = Vector3.ClampMagnitude(moveWorld, 1f);

        float control = cc.isGrounded ? 1f : airControlMultiplier;

        // Horizontal move
        cc.Move(moveWorld * (speed * control) * dt);

        // Grounded tracking (tick-based)
        if (cc.isGrounded)
        {
            lastGroundedTick = cmd.tick;
            if (velocity.y < 0f) velocity.y = groundStickForce;
        }

        // Jump buffer (tick-based)
        if (cmd.jumpDown)
            lastJumpPressedTick = cmd.tick;

        bool buffered = (lastJumpPressedTick != 0) && (cmd.tick - lastJumpPressedTick) <= jumpBufferTicks;
        bool canCoyote = (lastGroundedTick != 0) && (cmd.tick - lastGroundedTick) <= coyoteTicks;

        if (buffered && canCoyote)
        {
            velocity.y = jumpForce;
            lastJumpPressedTick = 0;
            lastGroundedTick = 0;
        }

        // Gravity + vertical move
        velocity.y += gravity * dt;
        cc.Move(velocity * dt);
    }

    void TrimDict<T>(Dictionary<uint, T> dict)
    {
        uint minTick = (localTick > (uint)maxHistoryTicks) ? (localTick - (uint)maxHistoryTicks) : 0;

        var toRemove = ListPool<uint>.Get();
        foreach (var k in dict.Keys)
            if (k < minTick) toRemove.Add(k);

        foreach (var k in toRemove)
            dict.Remove(k);

        ListPool<uint>.Release(toRemove);
    }

    bool inputEnabled = true; // owner only
    public void SetInputEnabled(bool enabled)
    {
        if (!IsOwner) return;
        inputEnabled = enabled;

        if (!enabled)
        {
            // also clear latched input so it can't "fire" later
            pendingJump = false;
            tickAccumulator = 0f;
        }
    }

    // ---------------- SERVER AUTH ----------------

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    void SendInputServerRpc(uint respawnId, uint tick, Vector2 move, bool jumpDown, bool sprint, float yawDeltaDeg)
    {
        var health = GetComponent<NetworkHealth>();
        if(health != null)
        {
            if(health.IsDeadNet.Value) return;

            if(respawnId != health.RespawnId.Value) return;

            if(respawnId != lastServerRespawnIdSeen)
            {
                lastServerRespawnIdSeen = respawnId;
                lastServerSimTick = 0;
            }
        }

        // Basic out-of-order guard (optional)
        if (tick <= lastServerSimTick)
            return;
        lastServerSimTick = tick;

        var cmd = new InputCmd
        {
            tick = tick,
            move = move,
            jumpDown = jumpDown,
            sprint = sprint,
            yawDeltaDeg = yawDeltaDeg
        };

        // Server sim (authoritative)
        SimulateTick(cmd, tickDt, isServerSim: true);
        var steps = GetComponent<FootstepEmitter>();
        if (IsServer)
        {
        
            if (steps != null)
            steps.ServerOnMovementSimulated(cmd.move, cmd.sprint);
        }

        
        if (steps == null)
        {
            Debug.LogWarning($"[FOOTSTEP][SERVER] FootstepEmitter MISSING on {name} (Owner={OwnerClientId})");
        }
        else
        {
            Debug.Log($"[FOOTSTEP][SERVER] calling ServerOnMovementSimulated Owner={OwnerClientId} move={move} sprint={sprint}");
            steps.ServerOnMovementSimulated(move, sprint);
        }


        // Send authoritative state back to OWNER for reconciliation
        SendAuthoritativeStateClientRpc(
            currentRespawnId,
            cmd.tick,
            transform.position,
            velocity,
            transform.eulerAngles.y,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            }
        );
    }

    [ClientRpc]
    void SendAuthoritativeStateClientRpc(uint respawnId, uint tick, Vector3 pos, Vector3 vel, float yawDeg, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        if (isReconciling) return;

        if(respawnId != currentRespawnId) return;


        // If we don't have predicted state for that tick yet, ignore
        if (!stateHistory.TryGetValue(tick, out var predicted))
            return;

        float posErr = Vector3.Distance(predicted.pos, pos);
        float yawErr = Mathf.Abs(Mathf.DeltaAngle(predicted.yawDeg, yawDeg));

        if (posErr < posErrorSnap && yawErr < rotErrorSnapDeg)
            return;

        StartCoroutine(ReconcileRoutine(tick, pos, vel, yawDeg));
    }

    IEnumerator ReconcileRoutine(uint serverTick, Vector3 serverPos, Vector3 serverVel, float serverYawDeg)
    {
        isReconciling = true;

        // Hard-set authoritative state safely
        cc.enabled = false;
        transform.position = serverPos;
        transform.rotation = Quaternion.Euler(0f, serverYawDeg, 0f);
        velocity = serverVel;
        cc.enabled = true;

        // Optional: wait a frame for CC internal stability
        yield return null;

        // Replay inputs from serverTick+1 -> current localTick
        uint replayTick = serverTick + 1;
        while (replayTick <= localTick)
        {
            if (inputHistory.TryGetValue(replayTick, out var cmd))
                SimulateTick(cmd, tickDt, isServerSim: false);
            replayTick++;
        }

        // Update latest snapshot
        stateHistory[localTick] = CaptureState(localTick);

        isReconciling = false;
    }

    public void ResetPredictionState()
    {
        // safe reset for respawn
        velocity = Vector3.zero;
        inputHistory.Clear();
        stateHistory.Clear();
        localTick = 0;
        tickAccumulator = 0f;
        pendingYawDeg = 0f;
        pendingJump = false;
        isReconciling = false;
    }

}



// Tiny pool to avoid GC in TrimDict (optional)
static class ListPool<T>
{
    static readonly Stack<List<T>> pool = new();
    public static List<T> Get() => pool.Count > 0 ? pool.Pop() : new List<T>(64);
    public static void Release(List<T> list)
    {
        list.Clear();
        pool.Push(list);
    }
}
