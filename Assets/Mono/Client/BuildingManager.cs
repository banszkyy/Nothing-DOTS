using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
    [SerializeField, SaintsField.ReadOnly, NotNull] GameObject? BuildingHologram = default;

    [SerializeField, NotNull] Material? HologramMaterial = default;

    [SerializeField, SaintsField.ReadOnly] bool IsValidPosition = false;

    [SerializeField, NotNull] LineRenderer? WirePlaceholder = default;
    [SerializeField, NotNull] RectTransform? WireConnectorBlob = default;
    [SerializeField, SaintsField.ReadOnly] (SpawnedGhost Entity, int Connector) SelectedPort;
    [SerializeField, SaintsField.ReadOnly] float3 SelectedPortPosition;

    [SerializeField] Color ValidHologramColor = Color.white;
    [SerializeField] Color InvalidHologramColor = Color.red;
    [SerializeField, Range(-10f, 10f)] float HologramEmission = 1.1f;

    public bool IsBuilding => SelectedBuilding.Prefab != default || IsWireConnecting;
    public bool IsWireConnecting => !SelectedPort.Equals(default);

    [Header("UI")]

    [SerializeField, NotNull] VisualTreeAsset? BuildingButton = default;
    [SerializeField, NotNull] UIDocument? BuildingUI = default;

    float refreshAt = default;
    float refreshedBySyncAt = default;
    float syncAt = default;

    void RefreshUI()
    {
        VisualElement container = BuildingUI.rootVisualElement.Q<VisualElement>("unity-content-container");
        container.Clear();

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        using EntityQuery buildingDatabaseQuery = entityManager.CreateEntityQuery(new ComponentType[] { typeof(BuildingDatabase) });
        if (!buildingDatabaseQuery.TryGetSingletonEntity<BuildingDatabase>(out Entity buildingDatabase))
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Failed to get `{nameof(BuildingDatabase)}` entity singleton");
            return;
        }

        container.SyncList(BuildingsSystemClient.GetInstance(entityManager.WorldUnmanaged).Buildings, BuildingButton, (item, element, recycled) =>
        {
            element.userData = item.Name;

            Button button = element.Q<Button>();
            if (!recycled)
            {
                button.clicked += () =>
                {
                    SelectBuilding((Unity.Collections.FixedString32Bytes)element.userData);
                    button.Blur();
                };
            }

            element.Q<Label>("label-name").text = item.Name.ToString();
            element.Q<Label>("label-resources").text = item.RequiredResources.ToString();
        });
    }

    void SelectBuilding(Unity.Collections.FixedString32Bytes buildingName)
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
        if (BuildingHologram != null)
        { ApplyHologram(BuildingHologram); }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B) && (!UI.IsUIFocused || BuildingUI.gameObject.activeSelf))
        {
            SelectedBuilding = default;
            SelectedPort = default;
            SelectedPortPosition = default;
            if (BuildingHologram != null) Destroy(BuildingHologram);
            BuildingHologram = null;
            WirePlaceholder.gameObject.SetActive(false);
            WireConnectorBlob.gameObject.SetActive(false);

            if (BuildingUI.gameObject.activeSelf)
            {
                UIManager.Instance.CloseUI(this);
                return;
            }
            else if (!UIManager.Instance.AnyUIVisible)
            {
                UIManager.Instance.OpenUI(BuildingUI)
                    .Setup(this);
            }
        }

        if (!BuildingUI.gameObject.activeSelf) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            SelectedBuilding = default;
            if (BuildingHologram != null) Destroy(BuildingHologram);
            BuildingHologram = null;
            IsValidPosition = false;
            WirePlaceholder.gameObject.SetActive(false);
            WireConnectorBlob.gameObject.SetActive(false);
            return;
        }

        if (Mouse.current.rightButton.wasReleasedThisFrame &&
            !UI.IsMouseHandled &&
            (IsBuilding || BuildingUI.gameObject.activeSelf) &&
            !CameraControl.Instance.IsDragging)
        {
            if (SelectedBuilding.Prefab != Entity.Null || !SelectedPort.Equals(default))
            {
                SelectedBuilding = default;
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

        if (BuildingUI == null || !BuildingUI.gameObject.activeSelf) return;

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
            return;
        }
        else if (BuildingHologram != null)
        {
            Destroy(BuildingHologram);
            BuildingHologram = null;
        }

        HandleWirePlacement();
    }

    void HandleWirePlacement()
    {
        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Input.mousePosition);

        if (!UI.IsMouseHandled
            && SelectionManager.RayCast(ray, Layers.BuildingOrUnit, out Hit hit)
            && SelectionManager.IsMine(hit.Entity.Entity)
            && ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<Connector>(hit.Entity.Entity))
        {
            Connector connector = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Connector>(hit.Entity.Entity);
            LocalTransform connectorTransform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(hit.Entity.Entity);
            Vector3 hitPoint = ray.GetPoint(hit.Distance);

            float3 hitPort = default;
            float hitPortDistance = float.MaxValue;
            for (int i = 0; i < connector.PortPositions.Length; i++)
            {
                float3 q = connectorTransform.TransformPoint(connector.PortPositions[i]);
                float d = math.distance(q, hitPoint);
                if (d < hitPortDistance)
                {
                    hitPortDistance = d;
                    hitPort = q;
                }
            }

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
            if (!SelectionManager.RayCast(ray, Layers.BuildingOrUnit, out hit)) return;

            Entity hitEntity = hit.Entity.Entity;
            if (!SelectionManager.IsMine(hitEntity))
            {
                Debug.Log($"{DebugEx.ClientPrefix} Entity isn't mine");
                return;
            }

            if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<Connector>(hitEntity))
            {
                Debug.Log($"{DebugEx.ClientPrefix} Entity isn't a connector");
                return;
            }

            Connector connector = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Connector>(hitEntity);
            LocalTransform connectorTransform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(hitEntity);
            GhostInstance connectorGhost = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(hitEntity);
            Vector3 hitPoint = ray.GetPoint(hit.Distance);

            int hitPort = -1;
            float hitPortDistance = float.MaxValue;
            for (int i = 0; i < connector.PortPositions.Length; i++)
            {
                float3 q = connectorTransform.TransformPoint(connector.PortPositions[i]);
                float d = math.distance(q, hitPoint);
                if (i == -1 || d < hitPortDistance)
                {
                    hitPortDistance = d;
                    hitPort = i;
                }
            }

            if (hitPort == -1)
            {
                Debug.LogWarning($"{DebugEx.ClientPrefix} It seems like this entity doesn't have any ports");
                return;
            }

            if (SelectedPort.Equals(default))
            {
                SelectedPort = (connectorGhost, hitPort);
                SelectedPortPosition = connectorTransform.TransformPoint(connector.PortPositions[hitPort]);
            }
            else
            {
                SpawnedGhost otherSelectedConnector = connectorGhost;

                if (ConnectionManager.ClientOrDefaultWorld.IsServer())
                {
                    throw new NotImplementedException();
                }
                else
                {
                    NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new PlaceWireRequestRpc()
                    {
                        EntityA = SelectedPort.Entity,
                        PortA = (byte)SelectedPort.Connector,
                        EntityB = otherSelectedConnector,
                        PortB = (byte)hitPort,
                        IsRemove = false,
                    });
                }

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
                bool isValid = false;
                float3 endPosition = default;

                if (SelectionManager.RayCast(ray, Layers.BuildingOrUnit, out hit)
                    && SelectionManager.IsMine(hit.Entity.Entity)
                    && ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<Connector>(hit.Entity.Entity))
                {
                    Connector connector = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Connector>(hit.Entity.Entity);
                    LocalTransform connectorTransform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalTransform>(hit.Entity.Entity);
                    Vector3 hitPoint = ray.GetPoint(hit.Distance);

                    int hitPort = -1;
                    float hitPortDistance = float.MaxValue;
                    for (int i = 0; i < connector.PortPositions.Length; i++)
                    {
                        float3 q = connectorTransform.TransformPoint(connector.PortPositions[i]);
                        float d = math.distance(q, hitPoint);
                        if (i == -1 || d < hitPortDistance)
                        {
                            hitPortDistance = d;
                            hitPort = i;
                        }
                    }

                    if (hitPort != -1)
                    {
                        isValid = true;
                        endPosition = connectorTransform.TransformPoint(connector.PortPositions[hitPort]);
                    }
                }

                if (!isValid)
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

    public void Setup(UIDocument ui)
    {
        RefreshUI();
        syncAt = 0f;
    }

    public void Cleanup(UIDocument ui)
    {
        SelectedBuilding = default;
        if (BuildingHologram != null) Destroy(BuildingHologram);
        BuildingHologram = null;
        WirePlaceholder.gameObject.SetActive(false);
        WireConnectorBlob.gameObject.SetActive(false);
    }
}
