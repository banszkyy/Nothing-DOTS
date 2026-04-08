using System.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Scenes;
using UnityEngine;

[UnityEngine.Scripting.Preserve]
class NetcodeBootstrap : ClientServerBootstrap
{
    public static new World? ServerWorld = default;
    public static new World? ClientWorld = default;
    public static World? LocalWorld = default;
    public static World? StagingWorld = default;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        ServerWorld = null;
        ClientWorld = null;
        LocalWorld = null;
        StagingWorld = null;
    }

    public static void DestroyLocalWorld()
    {
        LocalWorld?.Dispose();
        foreach (World world in World.All)
        {
            if (world == LocalWorld) continue;
            if (world.Flags == WorldFlags.Game)
            {
                world.Dispose();
                break;
            }
        }
        LocalWorld = null;
    }

    public static IEnumerator CreateLocal(string? savefile)
    {
        LocalWorld = CreateLocalWorld("LocalWorld");

        SubScene[] subScenes = Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include);

        while (!LocalWorld.IsCreated)
        {
            yield return null;
        }

        if (subScenes != null)
        {
            for (int i = 0; i < subScenes.Length; i++)
            {
                SceneSystem.LoadParameters loadParameters = new() { Flags = SceneLoadFlags.BlockOnStreamIn };
                Entity sceneEntity = SceneSystem.LoadSceneAsync(LocalWorld.Unmanaged, new Unity.Entities.Hash128(subScenes[i].SceneGUID.Value), loadParameters);
                while (!SceneSystem.IsSceneLoaded(LocalWorld.Unmanaged, sceneEntity))
                {
                    LocalWorld.Update();
                    yield return null;
                }
            }
        }

        if (savefile is not null)
        {
            EntityCommandBuffer entityCommandBuffer = new(Unity.Collections.Allocator.Temp);
            SaveManager.Load(LocalWorld, entityCommandBuffer, savefile);
            entityCommandBuffer.Playback(LocalWorld.EntityManager);
            entityCommandBuffer.Dispose();
        }
        else
        {
            using EntityQuery prefabsQ = LocalWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<PrefabDatabase>());
            if (prefabsQ.TryGetSingleton(out PrefabDatabase prefabs))
            {
                Debug.Log($"{DebugEx.LocalPrefix} Local player created");
                Entity newPlayer = LocalWorld.EntityManager.Instantiate(prefabs.Player);
                LocalWorld.EntityManager.SetComponentData<Player>(newPlayer, new()
                {
                    ConnectionId = 0,
                    ConnectionState = PlayerConnectionState.Local,
                    Team = Player.UnassignedTeam,
                });
            }
        }
    }

    public static IEnumerator CreateServer(NetworkEndpoint endpoint, string? savefile, Ref<bool> success)
    {
        Debug.Log($" -> Creating server world");
        ServerWorld = CreateServerWorld("ServerWorld");

        SubScene[] subScenes = Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include);

        while (!ServerWorld.IsCreated)
        {
            yield return null;
        }

        Debug.Log($" -> Server world created");

        if (subScenes != null)
        {
            Debug.Log($" -> Loading server subscenes");
            for (int i = 0; i < subScenes.Length; i++)
            {
                SceneSystem.LoadParameters loadParameters = new() { Flags = SceneLoadFlags.BlockOnStreamIn };
                Entity sceneEntity = SceneSystem.LoadSceneAsync(ServerWorld.Unmanaged, new Unity.Entities.Hash128(subScenes[i].SceneGUID.Value), loadParameters);
                while (!SceneSystem.IsSceneLoaded(ServerWorld.Unmanaged, sceneEntity))
                {
                    ServerWorld.Update();
                    yield return null;
                }
            }
            Debug.Log($" -> Server subscenes loaded");
        }

        bool ok;
        using (EntityQuery driverQ = ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
        {
            Debug.Log($" -> Start listening on {endpoint}");
            ok = success.Value = driverQ.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(endpoint);
            Debug.Log($" -> Listening: {ok}");
        }

        if (!ok)
        {
            ServerWorld.DestroyAllSystemsAndLogException(out _);
            ServerWorld.Dispose();
            ServerWorld = null;
            yield break;
        }

        if (savefile is not null)
        {
            Debug.Log($" -> Loading savefile");
            EntityCommandBuffer entityCommandBuffer = new(Unity.Collections.Allocator.Temp);
            SaveManager.Load(ServerWorld, entityCommandBuffer, savefile);
            entityCommandBuffer.Playback(ServerWorld.EntityManager);
            entityCommandBuffer.Dispose();
        }
        else
        {
            using EntityQuery prefabsQ = ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<PrefabDatabase>());
            if (prefabsQ.TryGetSingleton(out PrefabDatabase prefabs))
            {
                Debug.Log($"{DebugEx.ServerPrefix} Local player created");
                Entity newPlayer = ServerWorld.EntityManager.Instantiate(prefabs.Player);
                ServerWorld.EntityManager.SetComponentData<Player>(newPlayer, new()
                {
                    ConnectionId = 0,
                    ConnectionState = PlayerConnectionState.Server,
                    Team = 0,
                });
            }
            else
            {
                Debug.LogError($"{DebugEx.ServerPrefix} Cannot create local player: Singleton {nameof(PrefabDatabase)} not found");
            }
        }
        Debug.Log($" -> Server created");
    }

    public static IEnumerator CreateClient(NetworkEndpoint endpoint, Ref<Entity> connectionEntity)
    {
        Debug.Log($" -> Creating client world");
        ClientWorld = CreateClientWorld("ClientWorld");

        SubScene[] subScenes = Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include);

        while (!ClientWorld.IsCreated)
        {
            yield return null;
        }

        Debug.Log($" -> Client world created");

        if (subScenes != null)
        {
            Debug.Log($" -> Loading client subscenes");
            for (int i = 0; i < subScenes.Length; i++)
            {
                SceneSystem.LoadParameters loadParameters = new() { Flags = SceneLoadFlags.BlockOnStreamIn };
                Entity sceneEntity = SceneSystem.LoadSceneAsync(ClientWorld.Unmanaged, new Unity.Entities.Hash128(subScenes[i].SceneGUID.Value), loadParameters);
                while (!SceneSystem.IsSceneLoaded(ClientWorld.Unmanaged, sceneEntity))
                {
                    ClientWorld.Update();
                    yield return null;
                }
            }
            Debug.Log($" -> Client subscenes loaded");
        }

        using EntityQuery driverQ = ClientWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
        Debug.Log($" -> Connecting to {endpoint}");
        connectionEntity.Value = driverQ.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(ClientWorld.EntityManager, endpoint);
        Debug.Log($" -> Connected");
    }

    public static IEnumerator CreateStaging(string? savefile)
    {
        StagingWorld = CreateLocalWorld("StagingWorld");

        SubScene[] subScenes = Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include);

        while (!StagingWorld.IsCreated)
        {
            yield return null;
        }

        if (subScenes != null)
        {
            for (int i = 0; i < subScenes.Length; i++)
            {
                SceneSystem.LoadParameters loadParameters = new() { Flags = SceneLoadFlags.BlockOnStreamIn };
                Entity sceneEntity = SceneSystem.LoadSceneAsync(StagingWorld.Unmanaged, new Unity.Entities.Hash128(subScenes[i].SceneGUID.Value), loadParameters);
                while (!SceneSystem.IsSceneLoaded(StagingWorld.Unmanaged, sceneEntity))
                {
                    StagingWorld.Update();
                    yield return null;
                }
            }
        }

        if (savefile is not null)
        {
            EntityCommandBuffer entityCommandBuffer = new(Unity.Collections.Allocator.Temp);
            SaveManager.Load(StagingWorld, entityCommandBuffer, savefile);
            entityCommandBuffer.Playback(StagingWorld.EntityManager);
            entityCommandBuffer.Dispose();
        }
        else
        {
            using EntityQuery prefabsQ = StagingWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<PrefabDatabase>());
            if (prefabsQ.TryGetSingleton(out PrefabDatabase prefabs))
            {
                Debug.Log($"{DebugEx.LocalPrefix} Local player created");
                Entity newPlayer = StagingWorld.EntityManager.Instantiate(prefabs.Player);
                StagingWorld.EntityManager.SetComponentData<Player>(newPlayer, new()
                {
                    ConnectionId = 0,
                    ConnectionState = PlayerConnectionState.Local,
                    Team = Player.UnassignedTeam,
                });
            }
        }
    }

    public override bool Initialize(string defaultWorldName)
    {
        AutoConnectPort = 0;
        LocalWorld = CreateLocalWorld(defaultWorldName);
        return true;
    }
}
