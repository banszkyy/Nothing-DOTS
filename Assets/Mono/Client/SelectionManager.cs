using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

class VirtualGhostEntity : IEquatable<VirtualGhostEntity>
{
    public readonly Entity Entity;
    public readonly GhostInstance GhostInstance;
    public readonly UnitCommandDefinition[] UnitCommands;

    public VirtualGhostEntity(Entity entity, GhostInstance ghostInstance, UnitCommandDefinition[] unitCommands)
    {
        Entity = entity;
        GhostInstance = ghostInstance;
        UnitCommands = unitCommands;
    }

    public override bool Equals(object? obj) => obj is VirtualGhostEntity other && Equals(other);
    public bool Equals(VirtualGhostEntity other) => GhostInstance.ghostId == other.GhostInstance.ghostId && GhostInstance.spawnTick == other.GhostInstance.spawnTick;
    public override int GetHashCode() => HashCode.Combine(GhostInstance.ghostId, GhostInstance.spawnTick);

    public static bool operator ==(VirtualGhostEntity left, VirtualGhostEntity right) => left.Equals(right);
    public static bool operator !=(VirtualGhostEntity left, VirtualGhostEntity right) => !left.Equals(right);
}

public class SelectionManager : Singleton<SelectionManager>
{
    const uint Layer = Layers.Selectable | Layers.BuildingPlaceholder;
    const float WorldRaycastDistanceLimit = 300f;

    [SerializeField] float BoxSelectDistanceThreshold;
    [SerializeField, NotNull] RectTransform? SelectBox = default;
    [SerializeField, NotNull] UIDocument? UnitCommandsUI = default;
    UnitCommandsSchema? commandsUi;
    [SerializeField, NotNull] VisualTreeAsset? UnitCommandItemUI = default;

    bool _isSelectBoxVisible;
    Vector3 _selectionStart;
    Vector3 _rightClick;
    HashSet<VirtualGhostEntity> _selected = new();
    HashSet<Entity> _candidates = new();
    Entity _firstHit = Entity.Null;
    Vector3 _unitCommandUIWorldPositionData;
    Vector3 _unitCommandUIPosition;

    public bool IsUnitCommandsActive => UnitCommandsUI.isActiveAndEnabled;

    void SetSelectBoxVisible(bool visible)
    {
        if (_isSelectBoxVisible != visible) SelectBox.gameObject.SetActive(_isSelectBoxVisible = visible);
    }

    void Start()
    {
        _selected = new();
        _candidates = new();
    }

    void Update()
    {
        if (UnitCommandsUI.isActiveAndEnabled)
        {
            if (UIManager.Instance.GrapESC())
            {
                HideUnitCommandsUI();
            }
            else
            {
                DebugEx.DrawPoint(_unitCommandUIPosition, 2f, Color.white);

                Vector3 screenPoint = MainCamera.Camera.WorldToScreenPoint(_unitCommandUIPosition);
                if (screenPoint.z >= 0f)
                {
                    screenPoint.z = 0f;
                    screenPoint.y = MainCamera.Camera.pixelHeight - screenPoint.y;
                    commandsUi.Root.style.translate = screenPoint;
                }
            }
        }

        if (BuildingManager.Instance.IsBuilding)
        {
            SetSelectBoxVisible(false);
            _firstHit = Entity.Null;
            SelectBox.gameObject.SetActive(false);
            _selectionStart = default;
            return;
        }

        if (UI.IsUIFocused)
        {
            SetSelectBoxVisible(false);
            _firstHit = Entity.Null;
            SelectBox.gameObject.SetActive(false);
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (UnitCommandsUI.gameObject.activeSelf)
            {
                HideUnitCommandsUI();
            }
            else
            {
                BeginBoxSelect();
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            FinishBoxSelect();
        }

        if (Input.GetMouseButtonDown(1))
        {
            _rightClick = Input.mousePosition;
            BeginUnitAction();
        }

        if (Input.GetMouseButtonUp(1))
        {
            if ((Input.mousePosition - _rightClick).sqrMagnitude > 10)
            {
                _firstHit = default;
            }
            else
            {
                FinishUnitAction();
            }
            _rightClick = default;
        }

        UpdateBoxSelect();

        //{
        //    UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Input.mousePosition);
        //    if (TerrainCollisionSystem.Raycast(ray.origin, ray.direction, 1000f, out float3 hitPoint))
        //    {
        //        DebugEx.DrawPoint(hitPoint, 1f, Color.white, 10f);
        //    }
        //}

        // {
        //     Ray ray = MainCamera.Camera.ScreenPointToRay(Input.mousePosition);
        //     if (Ground.Raycast(ray, out float distance))
        //     {
        //         var start = ray.origin;
        //         var end = ray.GetPoint(distance);
        //         // QuadrantSystem.DrawQuadrant(ray.GetPoint(distance));
        //         NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly map = QuadrantSystem.GetMap(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        //         if (QuadrantRayCast.Raycast(map, start, end, Layers.All, out Hit hit))
        //         {
        //             DebugEx.DrawPoint(hit.Position, 2f, Color.cyan);
        //         }
        //     }
        // }

        foreach (Entity item in _candidates)
        {
            SetUnitStatus(item, new SelectableUnit() { Status = SelectionStatus.Candidate });
        }

        foreach (VirtualGhostEntity item in _selected)
        {
            SetUnitStatus(item.Entity, new SelectableUnit() { Status = SelectionStatus.Selected });
        }
    }

    void BeginBoxSelect()
    {
        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Input.mousePosition);
        _selectionStart = WorldRaycast(ray, out float distance) ? ray.GetPoint(distance) : ray.GetPoint(WorldRaycastDistanceLimit);
    }

    void UpdateBoxSelect()
    {
        ClearSelectionCandidates();

        if (_selectionStart == default)
        {
            SetSelectBoxVisible(false);

            if (!Raycast(MainCamera.Camera.ScreenPointToRay(Input.mousePosition), Layer, out Hit hit)) return;
            Entity selectableHit = hit.Entity.Entity;
            SelectUnitCandidate(selectableHit, SelectionStatus.Candidate);

            return;
        }

        Vector3 startPoint = MainCamera.Camera.WorldToScreenPoint(_selectionStart);
        Vector3 endPoint = Input.mousePosition;

        if (startPoint.z <= 0f)
        {
            //Debug.LogWarning($"{DebugEx.ClientPrefix} Invalid selection box");
            SetSelectBoxVisible(false);
            return;
        }

        if (Vector2.Distance(startPoint, endPoint) < BoxSelectDistanceThreshold)
        {
            if (!Raycast(MainCamera.Camera.ScreenPointToRay(Input.mousePosition), Layer, out Hit hit)) return;
            Entity selectableHit = hit.Entity.Entity;
            SelectUnitCandidate(selectableHit, SelectionStatus.Candidate);
            SetSelectBoxVisible(false);
            return;
        }

        HideUnitCommandsUI();

        float minX = math.min(startPoint.x, endPoint.x);
        float minY = math.min(startPoint.y, endPoint.y);
        float maxX = math.max(startPoint.x, endPoint.x);
        float maxY = math.max(startPoint.y, endPoint.y);

        Rect rect = new(
            minX,
            minY,
            maxX - minX,
            maxY - minY
        );

        foreach (Entity unit in UnitsInRect(rect))
        { SelectUnitCandidate(unit, SelectionStatus.Candidate); }

        SelectBox.anchoredPosition = rect.position;
        SelectBox.sizeDelta = rect.size;
        SetSelectBoxVisible(true);
    }

    static VirtualGhostEntity CreateVirtualGhostEntity(EntityManager entityManager, Entity entity)
    {
        UnitCommandDefinition[] commands = TryGetUnitCommands(entityManager, entity, out ReadOnlySpan<UnitCommandDefinition> buffer)
            ? buffer.ToArray()
            : Array.Empty<UnitCommandDefinition>();
        return new VirtualGhostEntity(
            entity,
            entityManager.GetComponentData<GhostInstance>(entity),
            commands
        );
    }

    void FinishBoxSelect()
    {
        SetSelectBoxVisible(false);
        if (_selectionStart == default) return;

        if (!Input.GetKey(KeyCode.LeftShift)) ClearSelection();

        Vector3 startPoint = MainCamera.Camera.WorldToScreenPoint(_selectionStart);
        Vector3 endPoint = Input.mousePosition;
        _selectionStart = default;

        if (startPoint.z <= 0f)
        {
            //Debug.LogWarning($"{DebugEx.ClientPrefix} Invalid selection box");
            return;
        }

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        if (Vector2.Distance(startPoint, endPoint) < BoxSelectDistanceThreshold)
        {
            if (!Raycast(MainCamera.Camera.ScreenPointToRay(Input.mousePosition), Layer, out Hit hit)) return;
            VirtualGhostEntity virtualGhostEntity = CreateVirtualGhostEntity(entityManager, hit.Entity.Entity);
            if (GetUnitStatus(virtualGhostEntity.Entity).Status == SelectionStatus.Selected &&
                _selected.Count > 0 &&
                Input.GetKey(KeyCode.LeftShift))
            { DeselectUnit(virtualGhostEntity); }
            else
            { SelectUnit(virtualGhostEntity, SelectionStatus.Selected); }
            return;
        }

        float minX = math.min(startPoint.x, endPoint.x);
        float minY = math.min(startPoint.y, endPoint.y);
        float maxX = math.max(startPoint.x, endPoint.x);
        float maxY = math.max(startPoint.y, endPoint.y);

        Rect rect = new(
            minX,
            minY,
            maxX - minX,
            maxY - minY
        );

        foreach (Entity unit in UnitsInRect(rect))
        {
            VirtualGhostEntity virtualGhostEntity = CreateVirtualGhostEntity(entityManager, unit);
            SelectUnit(virtualGhostEntity, SelectionStatus.Selected);
        }
    }

    void BeginUnitAction()
    {
        _firstHit = Entity.Null;
        if (_selected.Count > 0 && !Input.GetKey(KeyCode.LeftShift)) return;

        if (!Raycast(MainCamera.Camera.ScreenPointToRay(Input.mousePosition), Layer, out Hit hit)) return;
        Entity selectableHit = hit.Entity.Entity;
        if (!IsMine(selectableHit))
        {
            Debug.Log($"{DebugEx.ClientPrefix} Unit isn't mine");
            return;
        }

        _firstHit = selectableHit;
    }

    void FinishUnitAction()
    {
        Entity firstHit = _firstHit;
        _firstHit = Entity.Null;
        if (_selected.Count == 0 || Input.GetKey(KeyCode.LeftShift))
        {
            if (Raycast(MainCamera.Camera.ScreenPointToRay(Input.mousePosition), Layer, out Hit hit) && hit.Entity.Entity == firstHit)
            {
                if (OpenEntityUI(hit.Entity.Entity))
                {
                    return;
                }
            }
        }

        if (_selected.Count > 0)
        {
            ShowUnitCommandsUI();
            return;
        }
    }

    static bool OpenEntityUI(Entity entity)
    {
        if (!IsMine(entity))
        {
            Debug.Log($"{DebugEx.ClientPrefix} Unit isn't mine");
            return false;
        }

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        if (entityManager.HasComponent<Processor>(entity) || entityManager.HasComponent<Factory>(entity) || entityManager.HasComponent<Facility>(entity))
        {
            UIManager.Instance.OpenUI(UIManager.Instance.Unit)
                .Setup(UnitMenuManager.Instance, entity);
            return true;
        }

        return false;
    }

    static bool TryGetUnitCommands(EntityManager entityManager, Entity selected, out ReadOnlySpan<UnitCommandDefinition> commands)
    {
        commands = default;

        if (!entityManager.Exists(selected))
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Cannot get unit commands for `{selected}`: Entity does not exists");
            return false;
        }

        if (!entityManager.HasComponent<Processor>(selected))
        {
            Debug.Log($"{DebugEx.ClientPrefix} Cannot get unit commands for `{selected}`: Entity does not have a Processor component");
            return false;
        }

        FileId sourceId = entityManager.GetComponentData<Processor>(selected).SourceFile;
        if (sourceId == default)
        {
            return false;
        }

        if (ConnectionManager.ClientOrDefaultWorld.IsClient())
        {
            if (!ConnectionManager.ClientOrDefaultWorld.GetExistingSystemManaged<CompilerSystemClient>().TryGetSource(sourceId, out CompiledSourceClient? source, ConnectionManager.ClientOrDefaultWorld.Unmanaged))
            {
                Debug.Log($"{DebugEx.ClientPrefix} Cannot get unit commands for `{selected}`: Source \"{sourceId}\" does not exists");
                return false;
            }

            commands = source.UnitCommandDefinitions.HasValue ? source.UnitCommandDefinitions.Value.AsReadOnlySpan() : ReadOnlySpan<UnitCommandDefinition>.Empty;
            return true;
        }
        else
        {
            if (!ConnectionManager.ClientOrDefaultWorld.GetExistingSystemManaged<CompilerSystemServer>().CompiledSources.TryGetValue(sourceId, out CompiledSourceServer? source))
            {
                Debug.Log($"{DebugEx.ClientPrefix} Cannot get unit commands for `{selected}`: Source \"{sourceId}\" does not exists");
                return false;
            }

            commands = source.UnitCommandDefinitions.HasValue ? source.UnitCommandDefinitions.Value.AsReadOnlySpan() : ReadOnlySpan<UnitCommandDefinition>.Empty;
            return true;
        }
    }

    void ShowUnitCommandsUI()
    {
        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Input.mousePosition);
        _unitCommandUIWorldPositionData = AnyRaycast(ray, Layer, out float hit) ? ray.GetPoint(hit) : default;
        _unitCommandUIPosition = _unitCommandUIWorldPositionData == default ? ray.GetPoint(300) : _unitCommandUIWorldPositionData;

        UnitCommandsUI.ForceSetActive(true);

        commandsUi = new(UnitCommandsUI.rootVisualElement);

        commandsUi.Progress.style.display = DisplayStyle.None;
        commandsUi.Progress.value = 0f;

        Vector3 screenPoint = MainCamera.Camera.WorldToScreenPoint(_unitCommandUIPosition);
        if (screenPoint.z >= 0f)
        {
            screenPoint.z = 0f;
            screenPoint.y = MainCamera.Camera.pixelHeight - screenPoint.y;
            commandsUi.Root.style.translate = screenPoint;
        }

        VisualElement container = commandsUi.ContainerUnitCommands;
        VisualElement containerQuick = commandsUi.ContainerUnitQuickCommands;
        container.Clear();
        containerQuick.Clear();

        List<(string Label, List<(VirtualGhostEntity Entity, UnitCommandDefinition Definition)> Instances)> allCommands = new();

        foreach (VirtualGhostEntity selected in _selected)
        {
            if (!TryGetUnitCommands(entityManager, selected.Entity, out ReadOnlySpan<UnitCommandDefinition> commands)) continue;
            for (int i = 0; i < commands.Length; i++)
            {
                UnitCommandDefinition command = commands[i];

                if (_unitCommandUIWorldPositionData == default && command.GetParameters().ToArray().Any(v => v is UnitCommandParameter.Position2 or UnitCommandParameter.Position3))
                {
                    // Position not avaliable, skipping
                    continue;
                }

                string label = command.Label.ToString();

                List<(VirtualGhostEntity Entity, UnitCommandDefinition Definition)>? existing = allCommands.FirstOrDefault(v => string.Equals(v.Label, label, StringComparison.Ordinal)).Instances;
                if (existing is null)
                {
                    existing = new();
                    allCommands.Add((label, existing));
                }
                existing.Add((selected, command));
            }
        }

        foreach ((string label, List<(VirtualGhostEntity VirtualGhostEntity, UnitCommandDefinition Definition)> instances) in allCommands)
        {
            VisualElement _container = label.Length <= 2 ? containerQuick : container;

            UnitCommandItemSchema itemUi = _container.AddNew<UnitCommandItemSchema>(UnitCommandItemUI);
            itemUi.UnitCommandName.text = label;
            itemUi.UnitCommandInstances.text = instances.Count == 1 ? null : $"x{instances.Count}";
            itemUi.UnitCommandButton.clicked += () => HandleUnitCommandClick(instances);
        }

        if (container.childCount == 0 && containerQuick.childCount == 0) UnitCommandsUI.ForceSetActive(false);
    }

    void HideUnitCommandsUI()
    {
        UnitCommandsUI.ForceSetActive(false);
    }

    void HandleUnitCommandClick(List<(VirtualGhostEntity Entity, UnitCommandDefinition Definition)> instances)
    {
        DebugEx.DrawPoint(_unitCommandUIWorldPositionData, 2f, Color.magenta, 10f);

        StartCoroutine(SendUnitCommandClick(instances));
    }

    IEnumerator SendUnitCommandClick(List<(VirtualGhostEntity Entity, UnitCommandDefinition Definition)> instances)
    {
        UnitCommandArguments arguments = new()
        {
            WorldPosition = _unitCommandUIWorldPositionData,
        };

        foreach (IGrouping<int, (VirtualGhostEntity Entity, UnitCommandDefinition Definition)> group in instances.GroupBy(v => v.Definition.Id))
        {
            FixedList64Bytes<SpawnedGhost> ghosts = new();
            foreach ((VirtualGhostEntity entity, _) in group)
            {
                ghosts.Add(entity.GhostInstance);
                if (ghosts.Length >= ghosts.Capacity)
                {
                    NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new UnitCommandBulkRequestRpc()
                    {
                        Entities = ghosts,
                        CommandId = group.Key,
                        Arguments = arguments,
                    });
                    ghosts.Clear();
                    yield return null;
                }
            }
            if (ghosts.Length > 1)
            {
                NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new UnitCommandBulkRequestRpc()
                {
                    Entities = ghosts,
                    CommandId = group.Key,
                    Arguments = arguments,
                });
                yield return null;
            }
            else if (ghosts.Length == 1)
            {
                NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new UnitCommandRequestRpc()
                {
                    Entity = ghosts[0],
                    CommandId = group.Key,
                    Arguments = arguments,
                });
            }
        }
    }

    static IReadOnlyCollection<Entity> UnitsInRect(Rect rect)
    {
        using EntityQuery selectablesQ = ConnectionManager.ClientOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(LocalToWorld), typeof(SelectableUnit));
        using NativeArray<Entity> selectableEntities = selectablesQ.ToEntityArray(Allocator.Temp);
        List<Entity> result = new(selectableEntities.Length);
        for (int i = 0; i < selectableEntities.Length; i++)
        {
            Entity selectableEntity = selectableEntities[i];
            LocalToWorld transform = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<LocalToWorld>(selectableEntity);
            Vector3 point = MainCamera.Camera.WorldToScreenPoint(transform.Position);
            if (point.x < rect.xMin ||
                point.y < rect.yMin ||
                point.x > rect.xMax ||
                point.y > rect.yMax)
            { continue; }
            result.Add(selectableEntity);
        }
        return result;
    }

    static SelectableUnit GetUnitStatus(Entity unit)
    {
        if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.Exists(unit) ||
            !ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<SelectableUnit>(unit))
        {
            return new SelectableUnit()
            {
                Status = SelectionStatus.None,
            };
        }

        return ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<SelectableUnit>(unit);
    }

    public static bool IsMine(Entity unit)
    {
        if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.Exists(unit)) return false;
        if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<UnitTeam>(unit)) return true;
        UnitTeam unitTeam = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<UnitTeam>(unit);
        if (!PlayerSystemClient.GetInstance(ConnectionManager.ClientOrDefaultWorld.Unmanaged).TryGetLocalPlayer(out Player localPlayer)) return false;
        return unitTeam.Team == localPlayer.Team;
    }

    static void SetUnitStatus(Entity unit, SelectableUnit status)
    {
        if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.Exists(unit) ||
            !ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<SelectableUnit>(unit))
        { return; }
        ConnectionManager.ClientOrDefaultWorld.EntityManager.SetComponentData<SelectableUnit>(unit, status);
    }

    void SelectUnitCandidate(Entity unit, SelectionStatus status)
    {
        if (!IsMine(unit)) return;
        if (GetUnitStatus(unit).Status == SelectionStatus.None)
        {
            SetUnitStatus(unit, new()
            {
                Status = status,
            });
        }
        _candidates.Add(unit);
    }

    void ClearSelectionCandidates()
    {
        if (_candidates.Count == 0) return;
        foreach (Entity unit in _candidates)
        {
            if (GetUnitStatus(unit).Status != SelectionStatus.Candidate) continue;
            SetUnitStatus(unit, new()
            {
                Status = SelectionStatus.None,
            });
        }
        _candidates.Clear();
    }

    void SelectUnit(VirtualGhostEntity unit, SelectionStatus status)
    {
        if (!IsMine(unit.Entity)) return;
        SetUnitStatus(unit.Entity, new()
        {
            Status = status,
        });
        _selected.Add(unit);

        HUDManager.Instance.ui.LabelSelectedUnits.text = _selected.Count.ToString();
        HUDManager.Instance.ui.LabelSelectedUnits.parent.style.display = _selected.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
    }

    void DeselectUnit(VirtualGhostEntity unit)
    {
        SetUnitStatus(unit.Entity, new()
        {
            Status = SelectionStatus.None,
        });
        _selected.Remove(unit);

        HUDManager.Instance.ui.LabelSelectedUnits.text = _selected.Count.ToString();
        HUDManager.Instance.ui.LabelSelectedUnits.parent.style.display = _selected.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
    }

    void ClearSelection()
    {
        foreach (VirtualGhostEntity unit in _selected)
        {
            SetUnitStatus(unit.Entity, new()
            {
                Status = SelectionStatus.None,
            });
        }
        _selected.Clear();

        HUDManager.Instance.ui.LabelSelectedUnits.text = _selected.Count.ToString();
        HUDManager.Instance.ui.LabelSelectedUnits.parent.style.display = _selected.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
    }

    public static bool WorldRaycast(UnityEngine.Ray ray, out float distance)
    {
        return TerrainGenerator.Instance.Raycast(ray.origin, ray.direction, WorldRaycastDistanceLimit, out distance, out _);
        //return Ground.Raycast(MainCamera.Camera.ScreenPointToRay(Input.mousePosition), out distance);
    }

    public static bool AnyRaycast(UnityEngine.Ray ray, uint layer, out float hit)
    {
        NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly map = QuadrantSystem.GetMap(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        if (QuadrantRayCast.Raycast(map, new Ray(ray, WorldRaycastDistanceLimit, layer), out var unitHit))
        {
            hit = unitHit.Distance;
            if (WorldRaycast(ray, out float _hit)) hit = Math.Min(hit, _hit);
            return true;
        }
        else
        {
            if (WorldRaycast(ray, out hit)) return true;
        }
        return false;
    }

    public static bool Raycast(UnityEngine.Ray ray, uint layer, out Hit hit)
    {
        NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly map = QuadrantSystem.GetMap(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        if (!QuadrantRayCast.Raycast(map, new Ray(ray, WorldRaycastDistanceLimit, layer), out hit)) return false;
        if (WorldRaycast(ray, out float worldHitDistance) && hit.Distance > worldHitDistance + 5f) return false;
        return true;
    }
}
