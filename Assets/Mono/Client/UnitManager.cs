using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[Serializable]
class UnitPlaceholderItem
{
    [NotNull] public string? Name = null;
    [NotNull] public GameObject? Prefab = null;
}

public class UnitManager : Singleton<UnitManager>, IUISetup, IUICleanup
{
    [SerializeField, NotNull] AllPrefabs? Prefabs = default;

    [Header("Holograms")]

    [SerializeField, SaintsField.ReadOnly, NotNull] GameObject? UnitHologram = default;
    [SerializeField, NotNull] Material? HologramMaterial = default;
    [SerializeField] Color ValidHologramColor = Color.white;
    [SerializeField] Color InvalidHologramColor = Color.red;
    [SerializeField, Range(-10f, 10f)] float HologramEmission = 1.1f;

    [Header("UI")]

    [SerializeField, NotNull] VisualTreeAsset? UnitButton = default;


    [SerializeField, SaintsField.ReadOnly] bool IsValidPosition;
    BufferedUnit SelectedUnit;
    public bool IsPlacing => SelectedUnit.Prefab != default;

    float refreshAt;
    float refreshedBySyncAt;
    float syncAt;
    UnitsSchema? ui;

    void RefreshUI()
    {
        if (!ui.IsVisible()) return;

        VisualElement container = ui.UnitsScrollview.contentContainer;
        container.Clear();

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        using EntityQuery unitDatabaseQuery = entityManager.CreateEntityQuery(new ComponentType[] { typeof(UnitDatabase) });
        if (!unitDatabaseQuery.TryGetSingletonEntity<UnitDatabase>(out Entity unitDatabase))
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Failed to get `{nameof(UnitDatabase)}` entity singleton");
            return;
        }

        container.SyncList<BufferedUnit, UnitItemSchema>(UnitsSystemClient.GetInstance(entityManager.WorldUnmanaged).Units, UnitButton, (item, element, recycled) =>
        {
            UnitPrefab v = Prefabs.Units.First(v => v.Prefab.name == item.Name.ToString());

            element.Root.userData = item.Name;

            if (!recycled)
            {
                element.ButtonSelect.clicked += () =>
                {
                    SelectUnit((Unity.Collections.FixedString32Bytes)element.Root.userData);
                    element.ButtonSelect.Blur();
                };
            }

            element.LabelResources.text = item.RequiredResources.ToString();
            element.Thumbnail.image = v.Thumbnail;
            element.Root.tooltip = item.Name.ToString();
            Tooltips.Instance.Reregister(element.Root);
        });
    }

    void SelectUnit(Unity.Collections.FixedString32Bytes unitName)
    {
        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        using EntityQuery unitDatabaseQuery = entityManager.CreateEntityQuery(new ComponentType[] { typeof(UnitDatabase) });
        if (!unitDatabaseQuery.TryGetSingletonEntity<UnitDatabase>(out Entity unitDatabase))
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Failed to get {nameof(UnitDatabase)} entity singleton");
            return;
        }

        DynamicBuffer<BufferedUnit> units = entityManager.GetBuffer<BufferedUnit>(unitDatabase, true);

        BufferedUnit unit = default;

        for (int i = 0; i < units.Length; i++)
        {
            if (units[i].Name != unitName) continue;
            unit = units[i];
            break;
        }

        if (unit.Prefab == Entity.Null)
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Unit \"{unitName}\" not found in the database");
            return;
        }

        SelectedUnit = unit;
        if (UnitHologram != null)
        { ApplyHologram(UnitHologram); }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.U) && (!UI.IsUIFocused || ui.IsVisible()))
        {
            SelectedUnit = default;
            if (UnitHologram != null) Destroy(UnitHologram);
            UnitHologram = null;

            if (ui.IsVisible())
            {
                UIManager.Instance.CloseUI(this);
                return;
            }
            else if (!UIManager.Instance.AnyUIVisible)
            {
                UIManager.Instance.OpenUI(UIManager.Instance.Units)
                    .Setup(this);
            }
        }

        if (!ui.IsVisible()) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            SelectedUnit = default;
            if (UnitHologram != null) Destroy(UnitHologram);
            UnitHologram = null;
            IsValidPosition = false;
            return;
        }

        if (Mouse.current.rightButton.wasReleasedThisFrame &&
            !UI.IsMouseHandled &&
            (IsPlacing || ui.IsVisible()) &&
            !CameraControl.Instance.IsDragging)
        {
            if (SelectedUnit.Prefab != Entity.Null)
            {
                SelectedUnit = default;
                if (UnitHologram != null) Destroy(UnitHologram);
                UnitHologram = null;
            }
            else
            {
                UIManager.Instance.CloseUI(this);
            }
            return;
        }

        if (!ui.IsVisible()) return;

        if (Time.time >= refreshAt ||
            refreshedBySyncAt != UnitsSystemClient.LastSynced.Data)
        {
            refreshedBySyncAt = UnitsSystemClient.LastSynced.Data;
            RefreshUI();
            refreshAt = Time.time + 1f;
        }

        if (Time.time >= syncAt)
        {
            syncAt = Time.time + 5f;
            UnitsSystemClient.Refresh(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SelectedUnit = default;
            if (UnitHologram != null) Destroy(UnitHologram);
            UnitHologram = null;
            IsValidPosition = false;
            return;
        }

        if (SelectedUnit.Prefab != Entity.Null)
        {
            HandleUnitPlacement();
            return;
        }
        else if (UnitHologram != null)
        {
            Destroy(UnitHologram);
            UnitHologram = null;
        }
    }

    void HandleUnitPlacement()
    {
        if (UnitHologram != null)
        {
            Destroy(UnitHologram);
        }

        UnitHologram = Instantiate(Prefabs.Units.First(v => SelectedUnit.Name.Equals(v.Prefab.name)).HologramPrefab, transform);

        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Mouse.current.position.value);

        if (!SelectionManager.WorldRaycast(ray, out float distance))
        { return; }

        Vector3 position = ray.GetPoint(distance);
        position.y = 0f;

        if (Input.GetKey(KeyCode.LeftControl))
        { position = new Vector3(math.round(position.x), position.y, math.round(position.z)); }

        Vector3 v = UnitHologram.transform.position - position;
        if (TerrainGenerator.Instance.TrySample(new float2(position.x, position.z), out float h, out float3 n))
        {
            position.y = h;
            TerrainCollisionSystemServer.AlignPreserveYawExact(transform.rotation, n, out quaternion rotation);
            transform.rotation = rotation;
        }
        UnitHologram.transform.position = position;

        var map = QuadrantSystem.GetMap(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        Collider placeholderCollider = new AABBCollider(true, new AABB() { Extents = new float3(1f, 1f, 1f) });

        IsValidPosition = !Collision.Intersect(
            map,
            placeholderCollider,
            position,
            out _,
            out _);

        MeshRenderer[] renderers = UnitHologram.GetComponentsInChildren<MeshRenderer>();

        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].material;
            material.color = IsValidPosition ? ValidHologramColor : InvalidHologramColor;
            material.SetEmissionColor(IsValidPosition ? ValidHologramColor : InvalidHologramColor, HologramEmission);
        }

        if (Mouse.current.leftButton.isPressed && !UI.IsMouseHandled)
        {
            if (SelectedUnit.Prefab == default) return;
            if (!IsValidPosition)
            {
                Debug.Log($"{DebugEx.ClientPrefix} Invalid unit position");
                return;
            }

            if (ConnectionManager.ClientOrDefaultWorld.IsServer())
            {
                throw new NotImplementedException();
            }
            else
            {
                NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new PlaceUnitRequestRpc()
                {
                    UnitName = SelectedUnit.Name,
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

    public void Setup(UIElementReference ui)
    {
        this.ui = new(ui.Element);
        syncAt = 0f;
        RefreshUI();
    }

    public void Cleanup(UIElementReference ui)
    {
        SelectedUnit = default;
        if (UnitHologram != null) Destroy(UnitHologram);
        UnitHologram = null;
    }
}
