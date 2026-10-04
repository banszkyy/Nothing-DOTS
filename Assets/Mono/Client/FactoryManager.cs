using System.Diagnostics.CodeAnalysis;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

public class FactoryManager : Singleton<FactoryManager>, IUISetup<Entity>, IUICleanup
{
    [Header("UI Assets")]

    [SerializeField, NotNull] VisualTreeAsset? UI_AvaliableItem = default;
    [SerializeField, NotNull] VisualTreeAsset? UI_QueueItem = default;

    [Header("UI")]

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
        ui.ProgressCurrent.value = selectedFactory.CurrentProgress / selectedFactory.TotalProgress;
        ui.ProgressCurrent.title = selectedFactory.Current.Name.ToString();
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

        VisualElement avaliableList = ui.ListAvaliable;
        ScrollView queueList = ui.ListQueue;

        avaliableList.Clear();
        queueList.Clear();

        DynamicBuffer<BufferedProducingUnit> queue = entityManager.GetBuffer<BufferedProducingUnit>(factoryEntity);

        queueList.SyncList<BufferedProducingUnit, FactoryQueueItemSchema>(queue, UI_QueueItem, (item, element, recycled) =>
        {
            element.LabelUnitName.text = item.Name.ToString();
        });

        avaliableList.SyncList<BufferedUnit, FactoryAvaliableItemSchema>(UnitsSystemClient.GetInstance(entityManager.WorldUnmanaged).Units, UI_AvaliableItem, (item, element, recycled) =>
        {
            element.Root.userData = item.Name.ToString();
            element.LabelName.text = item.Name.ToString();
            element.LabelResources.text = item.RequiredResources.ToString();
            if (!recycled) element.ButtonSelect.clicked += () => QueueUnit((string)element.Root.userData);
        });

        ui.ProgressCurrent.value = selectedFactory.CurrentProgress / selectedFactory.TotalProgress;
        ui.ProgressCurrent.title = selectedFactory.Current.Name.ToString();
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
