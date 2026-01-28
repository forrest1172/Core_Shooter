using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance;
    [SerializeField] int maxPlayers = 4;

    public string currentJoinCode {get; private set;}

    async void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        DontDestroyOnLoad(gameObject);

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    // HOST
    public async void StartHostWithRelay()
    {
            Allocation allocation = await RelayService.Instance
            .CreateAllocationAsync(maxPlayers - 1);

            string joinCode = await RelayService.Instance
            .GetJoinCodeAsync(allocation.AllocationId);
            currentJoinCode = joinCode;
            Debug.Log("Relay Join Code: " + joinCode);

            UnityTransport transport =
            NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData);

        NetworkManager.Singleton.StartHost();

        LoadGameScene();
    }

    // CLIENT
    public async void StartClientWithRelay(string joinCode)
    {
        JoinAllocation allocation =
            await RelayService.Instance.JoinAllocationAsync(joinCode);

        UnityTransport transport =
            NetworkManager.Singleton.GetComponent<UnityTransport>();

        transport.SetRelayServerData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData,
            allocation.HostConnectionData);

        NetworkManager.Singleton.StartClient();
    }

    void LoadGameScene()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(
                "Game",
                LoadSceneMode.Single
            );
        }
    }
}