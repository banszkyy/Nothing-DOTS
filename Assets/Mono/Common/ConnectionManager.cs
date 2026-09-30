using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

public class ConnectionManager : Singleton<ConnectionManager>
{
    public static World? ClientWorld => NetcodeBootstrap.ClientWorld;
    public static World? ServerWorld => NetcodeBootstrap.ServerWorld;
    public static World? LocalWorld => NetcodeBootstrap.LocalWorld;
    public static World? StagingWorld => NetcodeBootstrap.StagingWorld;

    public static World ClientOrDefaultWorld => NetcodeBootstrap.ClientWorld ?? NetcodeBootstrap.LocalWorld ?? World.DefaultGameObjectInjectionWorld;
    public static World ServerOrDefaultWorld => NetcodeBootstrap.ServerWorld ?? NetcodeBootstrap.LocalWorld ?? World.DefaultGameObjectInjectionWorld;
    public static World StagingOrDefaultWorld => NetcodeBootstrap.StagingWorld ?? NetcodeBootstrap.LocalWorld ?? World.DefaultGameObjectInjectionWorld;
    public static World? HostWorld => NetcodeBootstrap.ServerWorld ?? NetcodeBootstrap.LocalWorld;

    [SerializeField, NotNull] GameObject? ServerObjects = default;
    [SerializeField, NotNull] GameObject? ClientObjects = default;
    [SerializeField, NotNull] GameObject? StagingObjects = default;

#if UNITY_EDITOR && EDITOR_DEBUG
    [Header("Debug")]
    [SerializeField] string DebugNickname = string.Empty;
    [SerializeField] string DebugSavefile = string.Empty;
    [SerializeField] ushort DebugPort;
    [SerializeField] bool AutoHost;
    [SerializeField] bool Singleplayer;
    [SerializeField] bool NoClient;
#endif

    void Start()
    {
#if UNITY_EDITOR && EDITOR_DEBUG
        if (AutoHost)
        {
            if (Singleplayer)
            {
                StartCoroutine(StartSingleplayer(DebugNickname, string.IsNullOrWhiteSpace(DebugSavefile) || !File.Exists(DebugSavefile) ? null : DebugSavefile));
            }
            else if (NoClient)
            {
                StartCoroutine(StartServer(DebugPort == 0 ? NetworkEndpoint.AnyIpv4 : NetworkEndpoint.Parse("127.0.0.1", DebugPort), string.IsNullOrWhiteSpace(DebugSavefile) || !File.Exists(DebugSavefile) ? null : DebugSavefile));
            }
            else
            {
                StartCoroutine(StartHost(DebugPort == 0 ? NetworkEndpoint.AnyIpv4 : NetworkEndpoint.Parse("127.0.0.1", DebugPort), DebugNickname, string.IsNullOrWhiteSpace(DebugSavefile) || !File.Exists(DebugSavefile) ? null : DebugSavefile));
            }
            return;
        }
#endif

        StartCoroutine(FirstStart());
    }

    IEnumerator FirstStart()
    {
        yield return null;
        UIManager.Instance.OpenUI(UIManager.Instance.MainMenu)
            .Setup(MainMenuManager.Instance);
    }

    public void OnNetworkEventClient(NetCodeConnectionEvent e)
    {
        RefreshUI(e);
        StartCoroutine(LateUIRefresh(e));
    }

    IEnumerator LateUIRefresh(NetCodeConnectionEvent e)
    {
        yield return null;
        RefreshUI(e);
    }

    void RefreshUI(NetCodeConnectionEvent e)
    {
        if (e.State == ConnectionState.State.Disconnected)
        {
            MainMenuManager.Instance.ConnectionError = e.DisconnectReason.ToString();

            UIManager.Instance.OpenUI(UIManager.Instance.MainMenu)
                .Setup(MainMenuManager.Instance);

            Debug.Log($" -> Disabling client objects");
            ClientObjects.SetActive(false);
        }
        else if (e.State == ConnectionState.State.Connected)
        {
            UIManager.Instance.CloseAllUI();

            Debug.Log($" -> Enabling client objects");
            ClientObjects.SetActive(true);
        }
        else
        {
            var ui = new NetworkStatusSchema(UIManager.Instance.OpenUI(UIManager.Instance.NetworkStatus).UI.Element);

            ui.LabelStatus.text = e.State switch
            {
                ConnectionState.State.Unknown => $"?",
                ConnectionState.State.Disconnected => throw new UnreachableException(),
                ConnectionState.State.Connecting => $"Connecting ...",
                ConnectionState.State.Handshake => $"Handshaking ...",
                ConnectionState.State.Approval => $"Approval ...",
                ConnectionState.State.Connected => throw new UnreachableException(),
                _ => throw new UnreachableException(),
            };
        }
    }

    public IEnumerator StartSingleplayer(FixedString32Bytes nickname, string? savefile)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.AnyPrefix} Start singleplayer");

        UIManager.Instance.CloseAllUI();

        Debug.Log($" -> Destroying local world");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> Creating local world");
        yield return StartCoroutine(NetcodeBootstrap.CreateLocal(savefile));

        Debug.Log($" -> Setting DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= NetcodeBootstrap.LocalWorld!;

        Debug.Log($" -> Enabling server objects");
        ServerObjects.SetActive(true);
        Debug.Log($" -> Enabling client objects");
        ClientObjects.SetActive(true);
        Debug.Log($" -> Disabling staging objects");
        StagingObjects.SetActive(false);
        yield return null;

        Debug.Log($" -> Set nickname to \"{nickname}\"");
        PlayerSystemClient.GetInstance(LocalWorld!.Unmanaged).SetNickname(nickname);

        Debug.Log($" -> Disabling UI");
        UIManager.Instance.CloseUI(UIManager.Instance.MainMenu);

#if UNITY_EDITOR && EDITOR_DEBUG
        if (SetupManager.Instance.isActiveAndEnabled && savefile is null)
        {
            Debug.Log($" -> Setting up test environment");
            SetupManager.Instance.Setup();
        }
#endif
    }

    public IEnumerator StartHost(NetworkEndpoint endpoint, FixedString32Bytes nickname, string? savefile)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.AnyPrefix} Start host on `{endpoint}`");

        var networkUi = new NetworkStatusSchema(UIManager.Instance.OpenUI(UIManager.Instance.NetworkStatus).UI.Element);

        networkUi.LabelStatus.text = "Creating server ...";

        Debug.Log($" -> Destroying local world");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> Creating server ({endpoint})");
        Ref<bool> success = new(true);
        yield return StartCoroutine(NetcodeBootstrap.CreateServer(endpoint, savefile, success));
        if (!success.Value)
        {
            networkUi.LabelStatus.text = "Error";
            UIManager.Instance.OpenUI(UIManager.Instance.MainMenu)
                .Setup<MainMenuManager>();
            yield break;
        }

        Debug.Log($" -> Setting DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= ServerWorld!;

        Debug.Log($" -> Enabling server objects");
        ServerObjects.SetActive(true);
        Debug.Log($" -> Disabling staging objects");
        StagingObjects.SetActive(false);
        yield return null;

        networkUi.LabelStatus.text = "Creating client ...";

        using (EntityQuery driverQ = ServerWorld!.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
        {
            endpoint = driverQ.GetSingletonRW<NetworkStreamDriver>().ValueRW.GetLocalEndPoint();
        }

        Debug.Log($" -> Creating client ({endpoint})");
        Ref<Entity> connectionEntity = new(Entity.Null);
        yield return StartCoroutine(NetcodeBootstrap.CreateClient(endpoint, connectionEntity));

        Debug.Log($" -> Set nickname to {nickname}");
        PlayerSystemClient.GetInstance(ClientWorld!.Unmanaged).SetNickname(nickname);

        Debug.Log($" -> Enabling client objects");
        ClientObjects.SetActive(true);
        yield return null;

#if UNITY_EDITOR && EDITOR_DEBUG
        if (SetupManager.Instance.isActiveAndEnabled && savefile is null)
        {
            Debug.Log($" -> Setting up test environment");
            SetupManager.Instance.Setup();
        }
#endif
    }

    public IEnumerator StartClient(NetworkEndpoint endpoint, FixedString32Bytes nickname)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.AnyPrefix} Start client on `{endpoint}`");

        var networkUi = new NetworkStatusSchema(UIManager.Instance.OpenUI(UIManager.Instance.NetworkStatus).UI.Element);

        networkUi.LabelStatus.text = "Creating client ...";

        Debug.Log($" -> Destroying local world");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> NetcodeBootstrap.CreateClient({endpoint})");
        Ref<Entity> connectionEntity = new(Entity.Null);
        yield return StartCoroutine(NetcodeBootstrap.CreateClient(endpoint, connectionEntity));

        Debug.Log($" -> Setting DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= ClientWorld!;

        Debug.Log($" -> Disabling server objects");
        ServerObjects.SetActive(false);
        Debug.Log($" -> Enabling client objects");
        ClientObjects.SetActive(true);
        Debug.Log($" -> Disabling staging objects");
        StagingObjects.SetActive(false);
        yield return null;

        Debug.Log($" -> Set nickname to {nickname}");
        PlayerSystemClient.GetInstance(ClientWorld!.Unmanaged).SetNickname(nickname);
    }

    public IEnumerator StartServer(NetworkEndpoint endpoint, string? savefile)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.EditorPrefix} Start server on `{endpoint}`");

        var networkUi = new NetworkStatusSchema(UIManager.Instance.OpenUI(UIManager.Instance.NetworkStatus).UI.Element);

        networkUi.LabelStatus.text = "Creating server ...";

        Debug.Log($" -> Destroying local world");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> Creating server ({endpoint})");
        Ref<bool> success = new(false);
        yield return StartCoroutine(NetcodeBootstrap.CreateServer(endpoint, savefile, success));
        if (!success.Value)
        {
            UIManager.Instance.OpenUI(UIManager.Instance.MainMenu)
                .Setup<MainMenuManager>();
            yield break;
        }

        Debug.Log($" -> Setting DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= ServerWorld!;

        Debug.Log($" -> Enabling server objects");
        ServerObjects.SetActive(true);
        Debug.Log($" -> Disabling client objects");
        ClientObjects.SetActive(false);
        Debug.Log($" -> Disabling staging objects");
        StagingObjects.SetActive(false);
        yield return null;

        Debug.Log($" -> Disabling UI");
        UIManager.Instance.CloseUI(UIManager.Instance.MainMenu);

        networkUi.LabelStatus.text = string.Empty;
        UIManager.Instance.CloseUI(UIManager.Instance.NetworkStatus);

#if UNITY_EDITOR && EDITOR_DEBUG
        if (SetupManager.Instance.isActiveAndEnabled && savefile is null)
        {
            Debug.Log($" -> Setting up test environment");
            SetupManager.Instance.Setup();
        }
#endif
    }

    public IEnumerator StartStaging(FixedString32Bytes nickname, string? savefile)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.AnyPrefix} Start staging");

        UIManager.Instance.CloseAllUI();

        Debug.Log($" -> Destroying local world");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> Creating local world");
        yield return StartCoroutine(NetcodeBootstrap.CreateStaging(savefile));

        Debug.Log($" -> Setting DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= NetcodeBootstrap.StagingWorld!;

        Debug.Log($" -> Enabling server objects");
        ServerObjects.SetActive(true);
        Debug.Log($" -> Enabling client objects");
        ClientObjects.SetActive(true);
        Debug.Log($" -> Enabling staging objects");
        StagingObjects.SetActive(true);
        yield return null;

        Debug.Log($" -> Set nickname to \"{nickname}\"");
        PlayerSystemClient.GetInstance(StagingWorld!.Unmanaged).SetNickname(nickname);

        Debug.Log($" -> Disabling UI");
        UIManager.Instance.CloseUI(UIManager.Instance.MainMenu);

#if UNITY_EDITOR && EDITOR_DEBUG
        if (SetupManager.Instance.isActiveAndEnabled && savefile is null)
        {
            Debug.Log($" -> Setting up test environment");
            SetupManager.Instance.Setup();
        }
#endif
    }

    public static void KickClient(int connectionId)
    {
        if (ServerWorld == null) return;

        using EntityQuery networkIdQ = ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
        using NativeArray<Entity> entities = networkIdQ.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            NetworkId networkId = ServerWorld.EntityManager.GetComponentData<NetworkId>(entities[i]);
            if (networkId.Value != connectionId) continue;
            ServerWorld.EntityManager.AddComponentData<NetworkStreamRequestDisconnect>(entities[i], new()
            {
                Reason = NetworkStreamDisconnectReason.ClosedByRemote,
            });
        }
    }

    public static void DisconnectEveryone()
    {
        if (ServerWorld == null) return;

        using EntityQuery networkStreamConnectionQ = ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
        using EntityQuery networkIdQ = ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
        using NativeArray<Entity> entities = networkIdQ.ToEntityArray(Allocator.Temp);

        using EntityQuery driverQ = ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
        RefRW<NetworkStreamDriver> driver = driverQ.GetSingletonRW<NetworkStreamDriver>();

        for (int i = 0; i < entities.Length; i++)
        {
            ServerWorld.EntityManager.AddComponentData<NetworkStreamRequestDisconnect>(entities[i], new()
            {
                Reason = NetworkStreamDisconnectReason.ClosedByRemote,
            });
        }
    }
}
