using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class SetupManager : Singleton<SetupManager>
{
    [Serializable]
    abstract class BaseUnitSetup
    {
        [SerializeField, NotNull] public GameObject? Prefab = default;
        [SerializeField, NotNull] public string? Script = default;
        [SerializeField] public int Team;
    }

    [Serializable]
    class UnitSetup : BaseUnitSetup
    {
        [SerializeField] public bool Enabled;
        [SerializeField] public float2 Spawn;
    }

    [Serializable]
    class SpreadUnitSetup : BaseUnitSetup
    {
        [SerializeField] public bool Enabled;
        [SerializeField] public int Count;
        [SerializeField, Min(0)] public float Density = 0f;
        [SerializeField] public Vector2 Start = default;
        [SerializeField] public Vector2 End = default;
        [SerializeField] public bool RandomRotation = default;
        [SerializeField] public bool RandomPosition = default;
    }

    [Header("Exact Spawns")]
    [SerializeField] bool SpawnExactUnits = default;
    [SerializeField, NotNull] UnitSetup[]? Units = default;

    [Header("Generator")]
    [SerializeField] bool SpawnRandomUnits_ = default;
    [SerializeField, NotNull] SpreadUnitSetup[]? RandomUnits = default;

    [SerializeField] bool Deterministic = default;
    [SaintsField.Playa.ShowIf(nameof(Deterministic)), SerializeField] int RandomSeed = default;

    const float UnitRadius = 1.45f;
    const float UnitArea = UnitRadius * UnitRadius * MathF.PI;
    const float PositionY = 0f;

    void OnValidate()
    {
        for (int i = 0; i < RandomUnits.Length; i++)
        {
            if (RandomUnits[i].Start.x > RandomUnits[i].End.x) RandomUnits[i].Start.x = RandomUnits[i].End.x;
            if (RandomUnits[i].Start.y > RandomUnits[i].End.y) RandomUnits[i].Start.y = RandomUnits[i].End.y;
        }
    }

    readonly struct Prefabs
    {
        readonly DynamicBuffer<BufferedUnit> units;
        readonly DynamicBuffer<BufferedBuilding> buildings;
        readonly DynamicBuffer<TerrainFeaturePrefab> terrainPrefabs;

        Prefabs(DynamicBuffer<BufferedUnit> units, DynamicBuffer<BufferedBuilding> buildings, DynamicBuffer<TerrainFeaturePrefab> terrainPrefabs)
        {
            this.units = units;
            this.buildings = buildings;
            this.terrainPrefabs = terrainPrefabs;
        }

        public static bool From(EntityManager entityManager, out Prefabs prefabs)
        {
            prefabs = default;

            DynamicBuffer<BufferedUnit> units;
            DynamicBuffer<BufferedBuilding> buildings;
            DynamicBuffer<TerrainFeaturePrefab> terrainPrefabs;

            using (EntityQuery unitDatabaseQ = entityManager.CreateEntityQuery(typeof(UnitDatabase)))
            {
                if (!unitDatabaseQ.TryGetSingletonEntity<UnitDatabase>(out Entity unitDatabase))
                {
                    Debug.LogError($"{DebugEx.ServerPrefix} Failed to get {nameof(UnitDatabase)} entity singleton");
                    return false;
                }
                units = entityManager.GetBuffer<BufferedUnit>(unitDatabase, true);
            }

            using (EntityQuery buildingDatabaseQ = entityManager.CreateEntityQuery(typeof(BuildingDatabase)))
            {
                if (!buildingDatabaseQ.TryGetSingletonEntity<BuildingDatabase>(out Entity buildingDatabase))
                {
                    Debug.LogError($"{DebugEx.ServerPrefix} Failed to get {nameof(BuildingDatabase)} entity singleton");
                    return false;
                }
                buildings = entityManager.GetBuffer<BufferedBuilding>(buildingDatabase, true);
            }

            using (EntityQuery terrainFeaturePrefabsQ = entityManager.CreateEntityQuery(typeof(TerrainFeaturePrefabs)))
            {
                if (!terrainFeaturePrefabsQ.TryGetSingletonEntity<TerrainFeaturePrefabs>(out Entity terrainPrefabsEntity))
                {
                    Debug.LogError($"{DebugEx.ServerPrefix} Failed to get {nameof(TerrainFeaturePrefabs)} singleton");
                    return false;
                }
                terrainPrefabs = entityManager.GetBuffer<TerrainFeaturePrefab>(terrainPrefabsEntity, true);
            }

            prefabs = new Prefabs(units, buildings, terrainPrefabs);
            return true;
        }

        public Entity GetPrefab(string name)
        {
            Entity prefab = Entity.Null;

            if (prefab == Entity.Null)
            {
                BufferedUnit unit = units.FirstOrDefault(static (v, c) => v.Name == c, name);
                prefab = unit.Prefab;
            }

            if (prefab == Entity.Null)
            {
                BufferedBuilding building = buildings.FirstOrDefault(static (v, c) => v.Name == c, name);
                prefab = building.Prefab;
            }

            if (prefab == Entity.Null)
            {
                for (int i = 0; i < terrainPrefabs.Length; i++)
                {
                    if (name != terrainPrefabs[i].PrefabName) continue;

                    prefab = terrainPrefabs[i].Prefab;
                    break;
                }
            }

            return prefab;
        }
    }

    public void Setup()
    {
        World world = ConnectionManager.ServerOrDefaultWorld;

        if (!Prefabs.From(world.EntityManager, out Prefabs prefabs)) return;

        if (SpawnExactUnits)
        {
            foreach (UnitSetup unitSetup in Units)
            {
                if (!unitSetup.Enabled) continue;

                Entity prefab = prefabs.GetPrefab(unitSetup.Prefab.name);

                if (prefab == Entity.Null)
                {
                    Debug.LogError($"{DebugEx.ServerPrefix} Prefab \"{unitSetup.Prefab.name}\" not found");
                    continue;
                }

                Entity newUnit = world.EntityManager.Instantiate(prefab);
                world.EntityManager.SetComponentData(newUnit, LocalTransform.FromPosition(new float3(unitSetup.Spawn.x, PositionY, unitSetup.Spawn.y)));

                ApplyUnit(world.EntityManager, newUnit, unitSetup);
            }
        }

        if (SpawnRandomUnits_)
        {
            foreach (SpreadUnitSetup randomUnit in RandomUnits)
            {
                if (!randomUnit.Enabled) continue;

                Entity prefab = prefabs.GetPrefab(randomUnit.Prefab.name);

                if (prefab == Entity.Null)
                {
                    Debug.LogError($"{DebugEx.ServerPrefix} Prefab \"{randomUnit.Prefab.name}\" not found");
                }
                else
                {
                    StartCoroutine(SpawnRandomUnits(prefab, world, randomUnit));
                }
            }
        }
    }

    IEnumerator SpawnRandomUnits(Entity prefab, World world, SpreadUnitSetup spreadUnitSetup)
    {
        yield return new WaitForSecondsRealtime(0.1f);

        int c = 0;

        foreach ((Vector2 position, float rotation) in GetPositions(spreadUnitSetup))
        {
            if (c++ > 50)
            {
                yield return null;
                c = 0;
            }

            Entity newUnit = world.EntityManager.Instantiate(prefab);
            world.EntityManager.SetComponentData(newUnit,
                spreadUnitSetup.RandomRotation
                ? LocalTransform.FromPositionRotation(
                    new float3(position.x, PositionY, position.y),
                    quaternion.EulerXYZ(0f, rotation, 0f)
                )
                : LocalTransform.FromPosition(
                    new float3(position.x, PositionY, position.y)
                )
            );

            ApplyUnit(world.EntityManager, newUnit, spreadUnitSetup);
        }
    }

    IEnumerable<(Vector2 Position, float Rotation)> GetPositions(SpreadUnitSetup spreadUnitSetup)
    {
        Vector2 start = spreadUnitSetup.Start;
        Vector2 end = spreadUnitSetup.End;

        if (spreadUnitSetup.Density != 0f)
        {
            float width = MathF.Sqrt(UnitArea * spreadUnitSetup.Density * spreadUnitSetup.Count);
            start = new Vector2(width, width) * -0.5f;
            end = new Vector2(width, width) * 0.5f;
        }

        System.Random random = Deterministic ? new System.Random(RandomSeed) : RandomManaged.Shared;

        List<float2> spawned = new(spreadUnitSetup.Count);

        bool IsOccupied(float2 position)
        {
            if (SpawnExactUnits)
            {
                foreach (UnitSetup unit in Units)
                {
                    if (!unit.Enabled) continue;
                    if (math.distance(unit.Spawn, position) < 2f * UnitRadius) return true;
                }
            }

            foreach (float2 unit in spawned)
            {
                if (math.distance(unit, position) < 2f * UnitRadius) return true;
            }

            return false;
        }

        if (spreadUnitSetup.RandomPosition)
        {
            for (int i = 0; i < spreadUnitSetup.Count; i++)
            {
                for (int j = 0; j < 50; j++)
                {
                    float2 generated = new(
                        random.Float(start.x, end.x),
                        random.Float(start.y, end.y)
                    );

                    if (IsOccupied(generated)) continue;

                    spawned.Add(generated);
                    yield return (generated, spreadUnitSetup.RandomRotation ? random.Float(0f, math.TAU) : 0f);

                    goto ok;
                }

                //Debug.LogWarning($"Only spawned `{i}` but had to {GeneratedCount}");
                yield break;

            ok:;
            }

            //Debug.Log($"{DebugEx.ServerPrefix} Spawned {GeneratedCount}");
        }
        else if (spreadUnitSetup.Count > 0)
        {
            float width = end.x - start.x;
            float height = end.y - start.y;

            float columns = math.sqrt(spreadUnitSetup.Count * width / height);
            float rows = spreadUnitSetup.Count / columns;

            float dx = math.max(0.1f, width / math.max(1f, columns - 1));
            float dy = math.max(0.1f, height / math.max(1f, rows - 1));
            int i = 0;

            for (float x = start.x; x <= end.x; x += dx)
            {
                for (float y = start.y; y <= end.y; y += dy)
                {
                    float2 generated = new(x, y);

                    if (IsOccupied(generated)) continue;

                    spawned.Add(generated);
                    yield return (generated, spreadUnitSetup.RandomRotation ? random.Float(0f, math.TAU) : 0f);

                    if (++i >= spreadUnitSetup.Count) break;
                }
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.gray;

        if (SpawnExactUnits)
        {
            foreach (UnitSetup unit in Units)
            {
                if (!unit.Enabled) continue;

                bool v = false;
                Vector3 p = new(unit.Spawn.x, PositionY, unit.Spawn.y);

                foreach (MeshFilter item in unit.Prefab.GetAllComponents<MeshFilter>())
                {
                    Gizmos.DrawWireMesh(item.sharedMesh, item.transform.position + p, item.transform.rotation, item.transform.lossyScale);
                    v = true;
                }

                if (v) continue;

                Gizmos.DrawSphere(p, 1f);
            }
        }

        if (SpawnRandomUnits_)
        {
            foreach (SpreadUnitSetup spreadUnitSetup in RandomUnits)
            {
                if (!spreadUnitSetup.RandomPosition || Deterministic)
                {
                    int i = 0;
                    List<MeshFilter> meshes = spreadUnitSetup.Prefab == null ? new() : spreadUnitSetup.Prefab.GetAllComponents<MeshFilter>();
                    foreach ((Vector2 position, float rotation) in GetPositions(spreadUnitSetup))
                    {
                        if (i++ > 100) break;
                        bool v = false;
                        Vector3 p = new(position.x, PositionY, position.y);

                        foreach (MeshFilter item in meshes)
                        {
                            Gizmos.DrawWireMesh(item.sharedMesh, item.transform.position + p, item.transform.rotation, item.transform.lossyScale);
                            v = true;
                        }

                        if (v) continue;

                        Gizmos.DrawSphere(p, UnitRadius);
                    }
                }
            }
        }
    }

    static void ApplyUnit(EntityManager entityManager, Entity entity, BaseUnitSetup unitSetup)
    {
        if (entityManager.HasComponent<Processor>(entity))
        {
            entityManager.ModifyComponent(entity, (ref Processor v) =>
            {
                v.SourceFile = new FileId(unitSetup.Script, NetcodeEndPoint.Server);
            });
        }

        if (entityManager.HasComponent<UnitTeam>(entity))
        {
            entityManager.ModifyComponent(entity, (ref UnitTeam v) =>
            {
                v.Team = unitSetup.Team;
            });
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(SetupManager))]
    public class SetupManagerEditor : UnityEditor.Editor
    {
        public void OnSceneGUI()
        {
            SetupManager t = (SetupManager)target;

            foreach (UnitSetup unitSetup in t.Units)
            {
                if (!unitSetup.Enabled) continue;

                Vector3 p = UnityEditor.Handles.PositionHandle(
                    new Vector3(unitSetup.Spawn.x, PositionY, unitSetup.Spawn.y),
                    Quaternion.identity
                );
                unitSetup.Spawn = new float2(p.x, p.z);
            }

            foreach (SpreadUnitSetup spreadUnitSetup in t.RandomUnits)
            {
                Vector3 start = UnityEditor.Handles.PositionHandle(
                    new Vector3(spreadUnitSetup.Start.x, PositionY, spreadUnitSetup.Start.y),
                    quaternion.identity
                );
                Vector3 end = UnityEditor.Handles.PositionHandle(
                    new Vector3(spreadUnitSetup.End.x, PositionY, spreadUnitSetup.End.y),
                    quaternion.identity
                );
                spreadUnitSetup.Start = new Vector2(start.x, start.z);
                spreadUnitSetup.End = new Vector2(end.x, end.z);
            }
        }
    }
#endif
}
