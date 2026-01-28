using Unity.Netcode;

public class PlayerStats : NetworkBehaviour
{
    public NetworkVariable<int> Kills = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> Deaths = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public void AddKill()
    {
        if (!IsServer) return;
        Kills.Value++;
    }

    public void AddDeath()
    {
        if (!IsServer) return;
        Deaths.Value++;
    }
}
