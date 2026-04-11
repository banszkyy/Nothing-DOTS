using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.UIElements;

public class ConnectionManager : Singleton<ConnectionManager>
{
    public static World? ClientWorld => NetcodeBootstrap.ClientWorld;
    public static World? ServerWorld => NetcodeBootstrap.ServerWorld;
    public static World? LocalWorld => NetcodeBootstrap.LocalWorld;
    public static World? StagingWorld => NetcodeBootstrap.StagingWorld;

    public static World ClientOrDefaultWorld => NetcodeBootstrap.ClientWorld ?? NetcodeBootstrap.LocalWorld ?? World.DefaultGameObjectInjectionWorld;
    public static World ServerOrDefaultWorld => NetcodeBootstrap.ServerWorld ?? NetcodeBootstrap.LocalWorld ?? World.DefaultGameObjectInjectionWorld;
    public static World StagingOrDefaultWorld => NetcodeBootstrap.StagingWorld ?? NetcodeBootstrap.LocalWorld ?? World.DefaultGameObjectInjectionWorld;

    [SerializeField, NotNull] GameObject? ServerObjects = default;
    [SerializeField, NotNull] GameObject? ClientObjects = default;
    [SerializeField, NotNull] GameObject? StagingObjects = default;

    [Header("UI")]
    [SerializeField, NotNull] UIDocument? MainMenuUI = default;
    [SerializeField, NotNull] UIDocument? NetworkUI = default;

#if UNITY_EDITOR && EDITOR_DEBUG
    [Header("Debug")]
    [SerializeField] string DebugNickname = string.Empty;
    [SerializeField] string DebugSavefile = string.Empty;
    [SerializeField] ushort DebugPort = default;
    [SerializeField] bool AutoHost = false;
    [SerializeField] bool Singleplayer = false;
    [SerializeField] bool NoClient = false;
#endif

    void Start()
    {
#if UNITY_EDITOR && EDITOR_DEBUG
        if (AutoHost)
        {
            if (Singleplayer)
            {
                StartCoroutine(StartSingleplayerAsync(DebugNickname, string.IsNullOrWhiteSpace(DebugSavefile) || !File.Exists(DebugSavefile) ? null : DebugSavefile));
            }
            else if (NoClient)
            {
                StartCoroutine(StartServerAsync(DebugPort == 0 ? NetworkEndpoint.AnyIpv4 : NetworkEndpoint.Parse("127.0.0.1", DebugPort), string.IsNullOrWhiteSpace(DebugSavefile) || !File.Exists(DebugSavefile) ? null : DebugSavefile));
            }
            else
            {
                StartCoroutine(StartHostAsync(DebugPort == 0 ? NetworkEndpoint.AnyIpv4 : NetworkEndpoint.Parse("127.0.0.1", DebugPort), DebugNickname, string.IsNullOrWhiteSpace(DebugSavefile) || !File.Exists(DebugSavefile) ? null : DebugSavefile));
            }
            return;
        }
#endif

        StartCoroutine(FirstStart());
    }

    IEnumerator FirstStart()
    {
        yield return new WaitForEndOfFrame();
        UIManager.Instance.OpenUI(MainMenuUI)
            .Setup(MainMenuManager.Instance);
    }

    public void OnNetworkEventClient(NetCodeConnectionEvent e)
    {
        RefreshUI(e);
        StartCoroutine(LateUIRefresh(e));
    }

    IEnumerator LateUIRefresh(NetCodeConnectionEvent e)
    {
        yield return new WaitForEndOfFrame();
        RefreshUI(e);
    }

    void RefreshUI(NetCodeConnectionEvent e)
    {
        if (e.State == ConnectionState.State.Disconnected)
        {
            MainMenuManager.Instance.ConnectionError = e.DisconnectReason.ToString();

            UIManager.Instance.OpenUI(MainMenuUI)
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
            UIManager.Instance.OpenUI(NetworkUI);

            NetworkUI.rootVisualElement.Q<Label>("label-status").text = e.State switch
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

    public IEnumerator StartSingleplayerAsync(FixedString32Bytes nickname, string? savefile)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.AnyPrefix} Start singleplayer");

        UIManager.Instance.CloseAllUI();

        Debug.Log($" -> NetcodeBootstrap.DestroyLocalWorld");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> CreateLocal");
        yield return StartCoroutine(NetcodeBootstrap.CreateLocal(savefile));

        Debug.Log($" -> DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= NetcodeBootstrap.LocalWorld!;

        Debug.Log($" -> Enabling server objects");
        ServerObjects.SetActive(true);
        Debug.Log($" -> Enabling client objects");
        ClientObjects.SetActive(true);
        Debug.Log($" -> Disabling staging objects");
        StagingObjects.SetActive(false);
        yield return new WaitForEndOfFrame();

        Debug.Log($" -> Set nickname to \"{nickname}\"");
        PlayerSystemClient.GetInstance(LocalWorld!.Unmanaged).SetNickname(nickname);

        Debug.Log($" -> Disabling UI");
        UIManager.Instance.CloseUI(MainMenuUI);

#if UNITY_EDITOR && EDITOR_DEBUG
        if (SetupManager.Instance.isActiveAndEnabled && savefile is null)
        {
            Debug.Log($" -> SetupManager.Instance.Setup()");
            SetupManager.Instance.Setup();
        }
#endif
    }

    public IEnumerator StartHostAsync(NetworkEndpoint endpoint, FixedString32Bytes nickname, string? savefile)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.AnyPrefix} Start host on `{endpoint}`");

        UIManager.Instance.OpenUI(NetworkUI);

        Debug.Log($" -> NetcodeBootstrap.DestroyLocalWorld");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> NetcodeBootstrap.CreateServer({endpoint})");
        Ref<bool> success = new(true);
        yield return StartCoroutine(NetcodeBootstrap.CreateServer(endpoint, savefile, success));
        if (!success.Value)
        {
            UIManager.Instance.OpenUI(MainMenuUI)
                .Setup<MainMenuManager>();
            yield break;
        }

        Debug.Log($" -> DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= ServerWorld!;

        Debug.Log($" -> Enabling server objects");
        ServerObjects.SetActive(true);
        Debug.Log($" -> Disabling staging objects");
        StagingObjects.SetActive(false);
        yield return new WaitForEndOfFrame();

        using (EntityQuery driverQ = ServerWorld!.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
        {
            endpoint = driverQ.GetSingletonRW<NetworkStreamDriver>().ValueRW.GetLocalEndPoint();
        }

        Debug.Log($" -> endpoint = {endpoint}");

        Debug.Log($" -> NetcodeBootstrap.CreateClient({endpoint})");
        Ref<Entity> connectionEntity = new(Entity.Null);
        yield return StartCoroutine(NetcodeBootstrap.CreateClient(endpoint, connectionEntity));

        Debug.Log($" -> Set nickname to {nickname}");
        PlayerSystemClient.GetInstance(ClientWorld!.Unmanaged).SetNickname(nickname);

        Debug.Log($" -> Enabling client objects");
        ClientObjects.SetActive(true);
        yield return new WaitForEndOfFrame();

#if UNITY_EDITOR && EDITOR_DEBUG
        if (SetupManager.Instance.isActiveAndEnabled && savefile is null)
        {
            Debug.Log($" -> SetupManager.Instance.Setup()");
            SetupManager.Instance.Setup();
        }
#endif
    }

    public IEnumerator StartClientAsync(NetworkEndpoint endpoint, FixedString32Bytes nickname)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.AnyPrefix} Start client on `{endpoint}`");

        UIManager.Instance.OpenUI(NetworkUI);

        Debug.Log($" -> NetcodeBootstrap.DestroyLocalWorld");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> NetcodeBootstrap.CreateClient({endpoint})");
        Ref<Entity> connectionEntity = new(Entity.Null);
        yield return StartCoroutine(NetcodeBootstrap.CreateClient(endpoint, connectionEntity));

        Debug.Log($" -> DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= ClientWorld!;

        Debug.Log($" -> Disabling server objects");
        ServerObjects.SetActive(false);
        Debug.Log($" -> Enabling client objects");
        ClientObjects.SetActive(true);
        Debug.Log($" -> Disabling staging objects");
        StagingObjects.SetActive(false);
        yield return new WaitForEndOfFrame();

        Debug.Log($" -> Set nickname to {nickname}");
        PlayerSystemClient.GetInstance(ClientWorld!.Unmanaged).SetNickname(nickname);
    }

    public IEnumerator StartServerAsync(NetworkEndpoint endpoint, string? savefile)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.EditorPrefix} Start server on `{endpoint}`");

        UIManager.Instance.OpenUI(NetworkUI);

        Debug.Log($" -> NetcodeBootstrap.DestroyLocalWorld");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> NetcodeBootstrap.CreateServer({endpoint})");
        Ref<bool> success = new(false);
        yield return StartCoroutine(NetcodeBootstrap.CreateServer(endpoint, savefile, success));
        if (!success.Value)
        {
            UIManager.Instance.OpenUI(MainMenuUI)
                .Setup<MainMenuManager>();
            yield break;
        }

        Debug.Log($" -> DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= ServerWorld!;

        Debug.Log($" -> Enabling server objects");
        ServerObjects.SetActive(true);
        Debug.Log($" -> Disabling client objects");
        ClientObjects.SetActive(false);
        Debug.Log($" -> Disabling staging objects");
        StagingObjects.SetActive(false);
        yield return new WaitForEndOfFrame();

        Debug.Log($" -> Disabling UI");
        UIManager.Instance.CloseUI(MainMenuUI);

#if UNITY_EDITOR && EDITOR_DEBUG
        if (SetupManager.Instance.isActiveAndEnabled && savefile is null)
        {
            Debug.Log($" -> SetupManager.Instance.Setup()");
            SetupManager.Instance.Setup();
        }
#endif
    }

    public IEnumerator StartStagingAsync(FixedString32Bytes nickname, string? savefile)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log($"{DebugEx.AnyPrefix} Start staging");

        UIManager.Instance.CloseAllUI();

        Debug.Log($" -> NetcodeBootstrap.DestroyLocalWorld");
        NetcodeBootstrap.DestroyLocalWorld();

        Debug.Log($" -> CreateLocal");
        yield return StartCoroutine(NetcodeBootstrap.CreateStaging(savefile));

        Debug.Log($" -> DefaultGameObjectInjectionWorld");
        World.DefaultGameObjectInjectionWorld ??= NetcodeBootstrap.StagingWorld!;

        Debug.Log($" -> Enabling server objects");
        ServerObjects.SetActive(true);
        Debug.Log($" -> Enabling client objects");
        ClientObjects.SetActive(true);
        Debug.Log($" -> Enabling staging objects");
        StagingObjects.SetActive(true);
        yield return new WaitForEndOfFrame();

        Debug.Log($" -> Set nickname to \"{nickname}\"");
        PlayerSystemClient.GetInstance(StagingWorld!.Unmanaged).SetNickname(nickname);

        Debug.Log($" -> Disabling UI");
        UIManager.Instance.CloseUI(MainMenuUI);

#if UNITY_EDITOR && EDITOR_DEBUG
        if (SetupManager.Instance.isActiveAndEnabled && savefile is null)
        {
            Debug.Log($" -> SetupManager.Instance.Setup()");
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
