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

    [SerializeField, SaintsField.ReadOnly] UIDocument? ui = default;

    Entity selectedFactoryEntity = Entity.Null;
    Factory selectedFactory = default;

    float refreshAt = default;
    float refreshedBySyncAt = default;
    float syncAt = default;

    void Update()
    {
        if (ui == null || !ui.gameObject.activeSelf) return;

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
        ui.rootVisualElement.Q<ProgressBar>("progress-current").value = selectedFactory.CurrentProgress / selectedFactory.TotalProgress;
        ui.rootVisualElement.Q<ProgressBar>("progress-current").title = selectedFactory.Current.Name.ToString();
    }

    public void Setup(UIDocument ui, Entity factoryEntity)
    {
        gameObject.SetActive(true);
        this.ui = ui;

        selectedFactoryEntity = factoryEntity;
        RefreshUI(factoryEntity);

        syncAt = 0f;
    }

    public void RefreshUI(Entity factoryEntity)
    {
        if (ui == null || !ui.gameObject.activeSelf) return;

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        VisualElement avaliableList = ui.rootVisualElement.Q<VisualElement>("list-avaliable");
        ScrollView queueList = ui.rootVisualElement.Q<ScrollView>("list-queue");

        avaliableList.Clear();
        queueList.Clear();

        DynamicBuffer<BufferedProducingUnit> queue = entityManager.GetBuffer<BufferedProducingUnit>(factoryEntity);

        queueList.SyncList(queue, UI_QueueItem, (item, element, recycled) =>
        {
            element.Q<Label>("label-unit-name").text = item.Name.ToString();
        });

        avaliableList.SyncList(UnitsSystemClient.GetInstance(entityManager.WorldUnmanaged).Units, UI_AvaliableItem, (item, element, recycled) =>
        {
            element.userData = item.Name.ToString();
            element.Q<Label>("label-name").text = item.Name.ToString();
            element.Q<Label>("label-resources").text = item.RequiredResources.ToString();
            if (!recycled) element.Q<Button>().clicked += () => QueueUnit((string)element.userData);
        });

        ui.rootVisualElement.Q<ProgressBar>("progress-current").value = selectedFactory.CurrentProgress / selectedFactory.TotalProgress;
        ui.rootVisualElement.Q<ProgressBar>("progress-current").title = selectedFactory.Current.Name.ToString();
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
        });

        if (selectedFactory.TotalProgress == default)
        {
            selectedFactory.Current = new BufferedProducingUnit()
            {
                Name = unit.Name,
                Prefab = unit.Prefab,
                ProductionTime = unit.ProductionTime
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
                ProductionTime = unit.ProductionTime
            });
        }
        refreshAt = Time.time + .1f;
    }

    public void Cleanup(UIDocument ui)
    {
        selectedFactoryEntity = Entity.Null;
        selectedFactory = default;
        refreshAt = float.PositiveInfinity;
        gameObject.SetActive(false);
    }
}
