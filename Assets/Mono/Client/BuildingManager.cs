using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[Serializable]
class BuildingPlaceholderItem
{
    [NotNull] public string? Name = default;
    [NotNull] public GameObject? Prefab = default;
}

public class BuildingManager : Singleton<BuildingManager>, IUISetup, IUICleanup
{
    BufferedBuilding SelectedBuilding = default;
    [SerializeField, NotNull] AllPrefabs? Prefabs = default;
    [NotNull] GameObject? BuildingHologram = default;

    [SerializeField, NotNull] Material? HologramMaterial = default;

    bool IsValidPosition = false;

    [SerializeField, NotNull] LineRenderer? WirePlaceholder = default;
    [SerializeField, NotNull] RectTransform? WireConnectorBlob = default;
    (SpawnedGhost Ghost, Entity Entity, int Port) SelectedPort;
    float3 SelectedPortPosition;
    [SerializeField, NotNull] RectTransform? DestroyingBlob = default;

    [SerializeField] Color ValidHologramColor = Color.white;
    [SerializeField] Color InvalidHologramColor = Color.red;
    [SerializeField, Range(-10f, 10f)] float HologramEmission = 1.1f;

    public bool IsBuilding => SelectedBuilding.Prefab != default || IsWireConnecting || IsDestroying;
    public bool IsWireConnecting => !SelectedPort.Equals(default);
    public bool IsDestroying { get; private set; }

    [Header("UI")]

    [SerializeField, NotNull] VisualTreeAsset? BuildingButton = default;

    float refreshAt = default;
    float refreshedBySyncAt = default;
    float syncAt = default;
    BuildingsSchema? ui;

    void RefreshUI()
    {
        if (!ui.IsVisible()) return;

        VisualElement container = ui.BuildingsContainer.contentContainer;
        container.Clear();

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        using EntityQuery buildingDatabaseQuery = entityManager.CreateEntityQuery(new ComponentType[] { typeof(BuildingDatabase) });
        if (!buildingDatabaseQuery.TryGetSingletonEntity<BuildingDatabase>(out Entity buildingDatabase))
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Failed to get `{nameof(BuildingDatabase)}` entity singleton");
            return;
        }

        {
            BuildingsItemSchema e = container.AddNew<BuildingsItemSchema>(BuildingButton);
            e.ButtonSelect.clicked += () =>
            {
                SelectedBuilding = default;
                IsDestroying = true;
                e.ButtonSelect.Blur();
            };
            e.LabelName.text = "Destroy";
            e.LabelResources.text = 0.ToString();
        }

        container.SyncList<BufferedBuilding, BuildingsItemSchema>(BuildingsSystemClient.GetInstance(entityManager.WorldUnmanaged).Buildings, BuildingButton, (item, element, recycled) =>
        {
            element.Root.userData = item.Name;

            if (!recycled)
            {
                element.ButtonSelect.clicked += () =>
                {
                    SelectBuilding((Unity.Collections.FixedString32Bytes)element.Root.userData);
                    element.ButtonSelect.Blur();
                };
            }

            element.LabelName.text = item.Name.ToString();
            element.LabelResources.text = item.RequiredResources.ToString();
        },
        startIndex: 1);
    }

    void SelectBuilding(FixedString32Bytes buildingName)
    {
        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        using EntityQuery buildingDatabaseQuery = entityManager.CreateEntityQuery(new ComponentType[] { typeof(BuildingDatabase) });
        if (!buildingDatabaseQuery.TryGetSingletonEntity<BuildingDatabase>(out Entity buildingDatabase))
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Failed to get {nameof(BuildingDatabase)} entity singleton");
            return;
        }

        DynamicBuffer<BufferedBuilding> buildings = entityManager.GetBuffer<BufferedBuilding>(buildingDatabase, true);

        BufferedBuilding building = default;

        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i].Name != buildingName) continue;
            building = buildings[i];
            break;
        }

        if (building.Prefab == Entity.Null)
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Building \"{buildingName}\" not found in the database");
            return;
        }

        SelectedBuilding = building;
        IsDestroying = false;
        if (BuildingHologram != null)
        { ApplyHologram(BuildingHologram); }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B) && (!UI.IsUIFocused || ui.IsVisible()))
        {
            SelectedBuilding = default;
            SelectedPort = default;
            SelectedPortPosition = default;
            IsDestroying = false;
            if (BuildingHologram != null) Destroy(BuildingHologram);
            BuildingHologram = null;
            WirePlaceholder.gameObject.SetActive(false);
            WireConnectorBlob.gameObject.SetActive(false);

            if (ui.IsVisible())
            {
                UIManager.Instance.CloseUI(this);
                return;
            }
            else if (!UIManager.Instance.AnyUIVisible)
            {
                UIManager.Instance.OpenUI(UIManager.Instance.Buildings)
                    .Setup(this);
            }
        }

        if (!ui.IsVisible()) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            SelectedBuilding = default;
            IsDestroying = false;
            if (BuildingHologram != null) Destroy(BuildingHologram);
            BuildingHologram = null;
            IsValidPosition = false;
            WirePlaceholder.gameObject.SetActive(false);
            WireConnectorBlob.gameObject.SetActive(false);
            return;
        }

        if (Mouse.current.rightButton.wasReleasedThisFrame &&
            !UI.IsMouseHandled &&
            (IsBuilding || ui.IsVisible()) &&
            !CameraControl.Instance.IsDragging)
        {
            if (SelectedBuilding.Prefab != Entity.Null || !SelectedPort.Equals(default) || IsDestroying)
            {
                SelectedBuilding = default;
                IsDestroying = false;
                if (BuildingHologram != null) Destroy(BuildingHologram);
                BuildingHologram = null;
                SelectedPort = default;
                SelectedPortPosition = default;
                WirePlaceholder.gameObject.SetActive(false);
                WireConnectorBlob.gameObject.SetActive(false);
            }
            else
            {
                UIManager.Instance.CloseUI(this);
            }
            return;
        }

        if (!ui.IsVisible()) return;

        if (Time.time >= refreshAt ||
            refreshedBySyncAt != BuildingsSystemClient.LastSynced.Data)
        {
            refreshedBySyncAt = BuildingsSystemClient.LastSynced.Data;
            RefreshUI();
            refreshAt = Time.time + 1f;
        }

        if (Time.time >= syncAt)
        {
            syncAt = Time.time + 5f;
            BuildingsSystemClient.Refresh(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SelectedBuilding = default;
            IsDestroying = false;
            if (BuildingHologram != null) Destroy(BuildingHologram);
            BuildingHologram = null;
            IsValidPosition = false;
            WirePlaceholder.gameObject.SetActive(false);
            WireConnectorBlob.gameObject.SetActive(false);
            return;
        }

        if (SelectedBuilding.Prefab != Entity.Null)
        {
            HandleBuildingPlacement();
            WireConnectorBlob.gameObject.SetActive(false);
            IsDestroying = false;
        }
        else if (BuildingHologram != null)
        {
            Destroy(BuildingHologram);
            BuildingHologram = null;
        }
        else if (IsDestroying)
        {
            HandleDestroying();
        }
        else
        {
            HandleWirePlacement();
        }
    }

    void HandleDestroying()
    {
        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Input.mousePosition);

        if (!UI.IsMouseHandled
            && SelectionManager.RayCast(ray, Layers.BuildingOrUnit, out Hit hit)
            && SelectionManager.IsMine(hit.Entity.Entity)
            && ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<Building>(hit.Entity.Entity))
        {
            LocalTransform transform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(hit.Entity.Entity);
            DestroyingBlob.gameObject.SetActive(true);
            DestroyingBlob.anchoredPosition = MainCamera.Camera.WorldToScreenPoint(transform.Position);
            goto k;
        }

        DestroyingBlob.gameObject.SetActive(false);
    k:

        if (Mouse.current.leftButton.wasPressedThisFrame && !UI.IsMouseHandled)
        {
            if (!SelectionManager.RayCast(ray, Layers.BuildingOrUnit, out hit)) return;

            Entity hitEntity = hit.Entity.Entity;
            if (!SelectionManager.IsMine(hitEntity))
            {
                Debug.Log($"{DebugEx.ClientPrefix} Entity isn't mine");
                return;
            }

            if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<Building>(hitEntity))
            {
                Debug.Log($"{DebugEx.ClientPrefix} Entity isn't a building");
                return;
            }

            GhostInstance buildingGhost = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(hitEntity);
            NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new DestroyBuildingRpc()
            {
                Entity = buildingGhost,
            });
        }
    }

    bool SelectPort(out Entity entity, out int port)
    {
        using var q = ConnectionManager.ClientOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(Connector));
        using var entities = q.ToEntityArray(Allocator.Temp);

        port = -1;
        entity = Entity.Null;
        float closest = float.PositiveInfinity;

        for (int i = 0; i < entities.Length; i++)
        {
            var transform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(entities[i]);
            var connector = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Connector>(entities[i]);
            for (int j = 0; j < connector.PortPositions.Length; j++)
            {
                if (SelectedPort.Entity == entities[i] && SelectedPort.Port == j) continue;

                var p = transform.TransformPoint(connector.PortPositions[j]);
                var sp = MainCamera.Camera.WorldToScreenPoint(p);
                if (sp.z <= 0f) continue;
                float d = math.distance(new float2(sp.x, sp.y), new float2(Input.mousePosition.x, Input.mousePosition.y));
                if (d < 30f)
                {
                    if (!SelectionManager.IsMine(entities[i]))
                    {
                        continue;
                    }

                    if (d < closest)
                    {
                        closest = d;
                        entity = entities[i];
                        port = j;
                    }
                }
            }
        }

        return entity != Entity.Null;
    }

    static bool SelectPortOld(out Entity entity, out int port)
    {
        entity = Entity.Null;
        port = -1;

        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Input.mousePosition);

        if (!SelectionManager.RayCast(ray, Layers.BuildingOrUnit, out Hit hit)) return false;

        Entity hitEntity = hit.Entity.Entity;
        if (!SelectionManager.IsMine(hitEntity))
        {
            return false;
        }

        if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<Connector>(hitEntity))
        {
            return false;
        }

        Connector connector = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Connector>(hitEntity);
        LocalTransform connectorTransform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(hitEntity);
        Vector3 hitPoint = ray.GetPoint(hit.Distance);

        float hitPortDistance = float.MaxValue;
        for (int i = 0; i < connector.PortPositions.Length; i++)
        {
            float3 q = connectorTransform.TransformPoint(connector.PortPositions[i]);
            float d = math.distance(q, hitPoint);
            if (i == -1 || d < hitPortDistance)
            {
                hitPortDistance = d;
                port = i;
            }
        }

        return port != -1;
    }

    void HandleWirePlacement()
    {
        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Input.mousePosition);

        if (!UI.IsMouseHandled && SelectPort(out Entity connectorEntity, out int port))
        {
            Connector connector = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Connector>(connectorEntity);
            LocalTransform connectorTransform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(connectorEntity);
            float3 hitPort = connectorTransform.TransformPoint(connector.PortPositions[port]);

            if (!hitPort.Equals(default))
            {
                WireConnectorBlob.gameObject.SetActive(true);
                WireConnectorBlob.anchoredPosition = MainCamera.Camera.WorldToScreenPoint(hitPort);
                goto k;
            }
        }

        WireConnectorBlob.gameObject.SetActive(false);
    k:

        if (Mouse.current.leftButton.wasPressedThisFrame && !UI.IsMouseHandled)
        {
            if (!SelectPort(out connectorEntity, out port)) return;

            Connector connector = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Connector>(connectorEntity);
            LocalTransform connectorTransform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(connectorEntity);
            GhostInstance connectorGhost = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(connectorEntity);

            if (SelectedPort.Equals(default))
            {
                SelectedPort = (connectorGhost, connectorEntity, port);
                SelectedPortPosition = connectorTransform.TransformPoint(connector.PortPositions[port]);
            }
            else
            {
                NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new PlaceWireRequestRpc()
                {
                    EntityA = SelectedPort.Ghost,
                    PortA = (byte)SelectedPort.Port,
                    EntityB = connectorGhost,
                    PortB = (byte)port,
                    IsRemove = false,
                });

                SelectedPort = default;
                SelectedPortPosition = default;
                WirePlaceholder.gameObject.SetActive(false);
            }
        }
        else
        {
            if (SelectedPort.Equals(default))
            {
                WirePlaceholder.gameObject.SetActive(false);
            }
            else
            {
                WirePlaceholder.gameObject.SetActive(true);
                float3 endPosition;
                bool isValid;

                if (isValid = SelectPort(out connectorEntity, out port))
                {
                    Connector connector = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Connector>(connectorEntity);
                    LocalTransform connectorTransform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(connectorEntity);
                    endPosition = connectorTransform.TransformPoint(connector.PortPositions[port]);

                    if (math.distance(SelectedPortPosition, endPosition) > 12f)
                    {
                        isValid = false;
                    }
                }
                else
                {
                    float d = math.distance(MainCamera.Camera.transform.position, SelectedPortPosition);
                    endPosition = SelectionManager.WorldRaycast(ray, out float distance) && distance < d ? ray.GetPoint(distance) : ray.GetPoint(d);
                }

                WirePlaceholder.material.color = isValid ? ValidHologramColor : InvalidHologramColor;
                WirePlaceholder.material.SetEmissionColor(isValid ? ValidHologramColor : InvalidHologramColor, HologramEmission);

                Vector3[] points = WireRendererSystemClient.GenerateWire(SelectedPortPosition, endPosition);
                WirePlaceholder.positionCount = points.Length;
                WirePlaceholder.SetPositions(points);
            }
        }
    }

    void HandleBuildingPlacement()
    {
        if (BuildingHologram != null)
        {
            Destroy(BuildingHologram);
        }

        BuildingHologram = Instantiate(Prefabs.Buildings.First(v => SelectedBuilding.Name.Equals(v.Prefab.name)).HologramPrefab, transform);

        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Mouse.current.position.value);

        if (!SelectionManager.WorldRaycast(ray, out float distance))
        { return; }

        Vector3 position = ray.GetPoint(distance);
        position.y = 0f;

        if (Input.GetKey(KeyCode.LeftControl))
        { position = new Vector3(math.round(position.x), position.y, math.round(position.z)); }

        Vector3 v = BuildingHologram.transform.position - position;
        if (TerrainGenerator.Instance.TrySample(new float2(position.x, position.z), out float h, out float3 n))
        {
            position.y = h;
            TerrainCollisionSystemServer.AlignPreserveYawExact(transform.rotation, n, out quaternion rotation);
            transform.rotation = rotation;
        }
        BuildingHologram.transform.position = position;

        var map = QuadrantSystem.GetMap(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        Collider placeholderCollider = new AABBCollider(true, new AABB() { Extents = new float3(1f, 1f, 1f) });

        IsValidPosition = !Collision.Intersect(
            map,
            placeholderCollider,
            position,
            out _,
            out _);

        if (IsValidPosition)
        {
            if (ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<Extractor>(SelectedBuilding.Prefab))
            {
                using var q = ConnectionManager.ClientOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(LocalTransform), typeof(ResourceNode));
                using var es = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                foreach (var resource in es)
                {
                    if (math.distance(resource.Position, position) < 5f)
                    {
                        goto ok;
                    }
                }

                IsValidPosition = false;
            ok:;
            }
        }

        MeshRenderer[] renderers = BuildingHologram.GetComponentsInChildren<MeshRenderer>();

        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].material;
            material.color = IsValidPosition ? ValidHologramColor : InvalidHologramColor;
            material.SetEmissionColor(IsValidPosition ? ValidHologramColor : InvalidHologramColor, HologramEmission);
        }

        if (Mouse.current.leftButton.isPressed && !UI.IsMouseHandled)
        {
            if (SelectedBuilding.Prefab == default) return;
            if (!IsValidPosition)
            {
                Debug.Log($"{DebugEx.ClientPrefix} Invalid building position");
                return;
            }

            if (ConnectionManager.ClientOrDefaultWorld.IsServer())
            {
                throw new NotImplementedException();
            }
            else
            {
                NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new PlaceBuildingRequestRpc()
                {
                    BuildingName = SelectedBuilding.Name,
                    Position = position,
                    Source = default,
                });
            }

            UIManager.Instance.CloseUI(this);
        }
    }

    static void ApplyHologram(GameObject hologram)
    {
        GameObject hologramModels = GetHologramModelGroup(hologram);
        hologramModels.transform.SetPositionAndRotation(default, Quaternion.identity);

        foreach (MeshRenderer v in hologram.GetComponentsInChildren<MeshRenderer>())
        {
            v.materials = new Material[] { Instantiate(Instance.HologramMaterial) };
        }
    }

    static GameObject GetHologramModelGroup(GameObject hologram)
    {
        Transform hologramModels = hologram.transform.Find("Model");
        if (hologramModels != null)
        { Destroy(hologramModels.gameObject); }

        hologramModels = new GameObject("Model").transform;
        hologramModels.SetParent(hologram.transform);
        hologramModels.localPosition = default;
        return hologramModels.gameObject;
    }

    public void Setup(UIElementReference ui)
    {
        this.ui = new BuildingsSchema(ui.Element);
        RefreshUI();
        syncAt = 0f;
    }

    public void Cleanup(UIElementReference ui)
    {
        SelectedBuilding = default;
        IsDestroying = false;
        if (BuildingHologram != null) Destroy(BuildingHologram);
        BuildingHologram = null;
        WirePlaceholder.gameObject.SetActive(false);
        WireConnectorBlob.gameObject.SetActive(false);
    }
}
