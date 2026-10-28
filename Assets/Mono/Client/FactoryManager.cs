using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

public class FactoryManager : Singleton<FactoryManager>, IUISetup<Entity>, IUICleanup
{
    [SerializeField, NotNull] AllPrefabs? Prefabs = default;

    [Header("UI")]

    [SerializeField, NotNull] VisualTreeAsset? UI_UnitItem = default;


    FactorySchema? ui;

    Entity selectedFactoryEntity = Entity.Null;
    Factory selectedFactory;

    float refreshAt;
    float refreshedBySyncAt;
    float syncAt;

    void Update()
    {
        if (!ui.IsVisible()) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            return;
        }

        if (Time.time >= refreshAt ||
            refreshedBySyncAt != UnitsSystemClient.LastSynced.Data)
        {
            refreshedBySyncAt = UnitsSystemClient.LastSynced.Data;
            if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.Exists(selectedFactoryEntity))
            {
                UIManager.Instance.CloseUI(this);
                return;
            }

            RefreshUI(selectedFactoryEntity);
            refreshAt = Time.time + 1f;
        }

        if (Time.time >= syncAt)
        {
            syncAt = Time.time + 5f;
            UnitsSystemClient.Refresh(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        }

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        selectedFactory = entityManager.GetComponentData<Factory>(selectedFactoryEntity);

        if (selectedFactory.TotalProgress == default) return;

        selectedFactory.CurrentProgress += Time.deltaTime * Factory.ProductionSpeed;
        ui.CurrentProgressbar.value = selectedFactory.CurrentProgress / selectedFactory.TotalProgress;
    }

    public void Setup(UIElementReference ui, Entity factoryEntity)
    {
        this.ui = new(ui.Element);

        selectedFactoryEntity = factoryEntity;
        RefreshUI(factoryEntity);

        syncAt = 0f;
    }

    public void RefreshUI(Entity factoryEntity)
    {
        if (!ui.IsVisible()) return;

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        ui.AvailableScrollview.Clear();
        ui.QueueScrollview.Clear();

        DynamicBuffer<BufferedProducingUnit> queue = entityManager.GetBuffer<BufferedProducingUnit>(factoryEntity);

        ui.QueueScrollview.SyncList<BufferedProducingUnit, UnitItemSchema>(queue, UI_UnitItem, (item, element, recycled) =>
        {
            UnitPrefab v = Prefabs.Units.First(v => v.Prefab.name == item.Name.ToString());
            element.Root.userData = item.Name.ToString();
            element.Thumbnail.image = v.Thumbnail;
            element.LabelResources.text = v.RequiredResources.ToString();
            element.Root.tooltip = item.Name.ToString();
            Tooltips.Instance.Reregister(element.Root);
        });

        ui.AvailableScrollview.SyncList<BufferedUnit, UnitItemSchema>(UnitsSystemClient.GetInstance(entityManager.WorldUnmanaged).Units, UI_UnitItem, (item, element, recycled) =>
        {
            UnitPrefab v = Prefabs.Units.First(v => v.Prefab.name == item.Name.ToString());
            element.Root.userData = item.Name.ToString();
            element.Thumbnail.image = v.Thumbnail;
            element.LabelResources.text = item.RequiredResources.ToString();
            element.Root.tooltip = item.Name.ToString();
            if (!recycled) element.ButtonSelect.clicked += () => QueueUnit((string)element.Root.userData);
            Tooltips.Instance.Reregister(element.Root);
        });

        if (selectedFactory.Current.Name != default)
        {
            ui.CurrentProgressbar.value = selectedFactory.CurrentProgress / selectedFactory.TotalProgress;
            ui.CurrentContainer.style.visibility = Visibility.Visible;

            VisualElement? _element = ui.CurrentContainer.Children().FirstOrDefault();
            if (_element is null)
            {
                _element = UI_UnitItem.Instantiate();
                ui.CurrentContainer.Add(_element);
            }
            UnitItemSchema element = new(_element);

            UnitPrefab v = Prefabs.Units.First(v => v.Prefab.name == selectedFactory.Current.Name.ToString());
            element.Root.userData = selectedFactory.Current.Name.ToString();
            element.Thumbnail.image = v.Thumbnail;
            element.LabelResources.text = v.RequiredResources.ToString();
            element.Root.tooltip = selectedFactory.Current.Name.ToString();
            Tooltips.Instance.Reregister(element.Root);
        }
        else
        {
            ui.CurrentProgressbar.value = 0f;
            ui.CurrentContainer.style.visibility = Visibility.Hidden;
        }
    }

    void QueueUnit(string unitName)
    {
        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        using EntityQuery unitDatabaseQ = entityManager.CreateEntityQuery(typeof(UnitDatabase));
        if (!unitDatabaseQ.TryGetSingletonEntity<UnitDatabase>(out Entity unitDatabase))
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Failed to get `{nameof(UnitDatabase)}` entity singleton");
            return;
        }

        DynamicBuffer<BufferedUnit> units = entityManager.GetBuffer<BufferedUnit>(unitDatabase, true);

        BufferedUnit unit = units.FirstOrDefault(static (v, c) => v.Name == c, unitName);

        if (unit.Prefab == Entity.Null)
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Unit \"{unitName}\" not found in the database");
            return;
        }

        GhostInstance ghostInstance = entityManager.GetComponentData<GhostInstance>(selectedFactoryEntity);

        NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new FactoryQueueUnitRequestRpc()
        {
            Unit = unit.Name,
            Entity = ghostInstance,
            Source = default,
        });

        if (selectedFactory.TotalProgress == default)
        {
            selectedFactory.Current = new BufferedProducingUnit()
            {
                Name = unit.Name,
                Prefab = unit.Prefab,
                ProductionTime = unit.ProductionTime,
            };
            selectedFactory.CurrentProgress = 0f;
            selectedFactory.TotalProgress = unit.ProductionTime;
        }
        else
        {
            DynamicBuffer<BufferedProducingUnit> queue = entityManager.GetBuffer<BufferedProducingUnit>(selectedFactoryEntity);
            queue.Add(new BufferedProducingUnit()
            {
                Name = unit.Name,
                Prefab = unit.Prefab,
                ProductionTime = unit.ProductionTime,
            });
        }
        refreshAt = Time.time + .1f;
    }

    public void Cleanup(UIElementReference ui)
    {
        selectedFactoryEntity = Entity.Null;
        selectedFactory = default;
        refreshAt = float.PositiveInfinity;
    }
}
