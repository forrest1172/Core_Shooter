using TMPro;
using Unity.Netcode;
using UnityEngine;

public class DeathScreenController : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject deathScreenRoot; // Canvas or panel root
    [SerializeField] private TMP_Text respawnText;

    NetworkHealth health;

    void Awake()
    {
        health = GetComponent<NetworkHealth>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Only the owning client shows a death screen for themselves
            if (deathScreenRoot != null) deathScreenRoot.SetActive(false);
            enabled = false;
            return;
        }

        if (deathScreenRoot != null) deathScreenRoot.SetActive(false);

        if (health != null)
        {
            health.IsDeadNet.OnValueChanged += OnDeadChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (health != null)
            health.IsDeadNet.OnValueChanged -= OnDeadChanged;
    }

    void OnDeadChanged(bool oldValue, bool newValue)
    {
        if (deathScreenRoot != null)
            deathScreenRoot.SetActive(newValue);

        // If you want mouse unlocked on death:
        if (newValue)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        if (!IsOwner) return;
        if (health == null) return;
        if (!health.IsDeadNet.Value) return;

        // ServerTime is available on clients too
        double now = NetworkManager.Singleton.ServerTime.Time;
        double remaining = health.RespawnAtServerTime.Value - now;

        if (respawnText != null)
        {
            respawnText.text = remaining > 0
                ? $"Respawning in: {remaining:0.0}"
                : "Respawning...";
        }
    }
}
