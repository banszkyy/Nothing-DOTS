using System;
using System.Diagnostics.CodeAnalysis;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

public class FacilityManager : Singleton<FacilityManager>, IUISetup<Entity>, IUICleanup
{
    [Header("UI Assets")]

    [SerializeField, NotNull] VisualTreeAsset? UI_AvaliableResearch = default;
    [SerializeField, NotNull] VisualTreeAsset? UI_QueueItem = default;

    [Header("UI")]

    [SerializeField, SaintsField.ReadOnly] UIDocument? ui = default;

    Entity selectedEntity = Entity.Null;
    Facility selected = default;

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
            refreshedBySyncAt != ResearchSystemClient.LastSynced.Data)
        {
            refreshedBySyncAt = ResearchSystemClient.LastSynced.Data;
            if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.Exists(selectedEntity))
            {
                UIManager.Instance.CloseUI(this);
                return;
            }

            RefreshUI(selectedEntity);
            refreshAt = Time.time + 1f;
        }

        if (Time.time >= syncAt)
        {
            syncAt = Time.time + 5f;
            ResearchSystemClient.Refresh(ConnectionManager.ClientOrDefaultWorld.Unmanaged);
        }

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        selected = entityManager.GetComponentData<Facility>(selectedEntity);

        if (selected.Current.Name.IsEmpty) return;

        selected.CurrentProgress += Time.deltaTime * Factory.ProductionSpeed;
        ui.rootVisualElement.Q<ProgressBar>("progress-current").value = selected.CurrentProgress / selected.Current.ResearchTime;
        ui.rootVisualElement.Q<ProgressBar>("progress-current").title = selected.Current.Name.ToString();
    }

    public void Setup(UIDocument ui, Entity entity)
    {
        gameObject.SetActive(true);
        this.ui = ui;

        syncAt = Math.Min(syncAt, Time.time + 0.5f);

        selectedEntity = entity;
        RefreshUI(entity);
    }

    public void RefreshUI(Entity entity)
    {
        if (ui == null || !ui.gameObject.activeSelf) return;

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        ScrollView avaliableList = ui.rootVisualElement.Q<ScrollView>("list-avaliable");
        ScrollView queueList = ui.rootVisualElement.Q<ScrollView>("list-queue");

        avaliableList.Clear();
        queueList.Clear();

        DynamicBuffer<BufferedResearch> queue = entityManager.GetBuffer<BufferedResearch>(entity);

        queueList.SyncList(queue, UI_QueueItem, (item, element, recycled) =>
        {
            element.Q<Label>("label-name").text = item.Name.ToString();
        });

        NativeList<FixedString64Bytes> avaliable = ResearchSystemClient.GetInstance(entityManager.WorldUnmanaged).AvaliableResearches;
        NativeList<FixedString64Bytes> avaliableNotInQueue = new(Math.Max(0, avaliable.Length - queue.Length), Allocator.Temp);

        for (int i = 0; i < avaliable.Length; i++)
        {
            if (!selected.Current.Name.IsEmpty &&
                selected.Current.Name == avaliable[i])
            {
                goto inQueue;
            }

            for (int j = 0; j < queue.Length; j++)
            {
                if (queue[j].Name != avaliable[i]) continue;
                goto inQueue;
            }

            avaliableNotInQueue.Add(avaliable[i]);

        inQueue:;
        }

        avaliableList.SyncList(avaliableNotInQueue, UI_AvaliableResearch, (item, element, recycled) =>
        {
            element.userData = item.ToString();
            element.Q<Label>("label-name").text = item.ToString();
            if (!recycled) element.Q<Button>("button-queue").clicked += () => QueueResearch((string)element.userData);
        });

        avaliableNotInQueue.Dispose();

        ui.rootVisualElement.Q<ProgressBar>("progress-current").value = selected.CurrentProgress / selected.Current.ResearchTime;
        ui.rootVisualElement.Q<ProgressBar>("progress-current").title = selected.Current.Name.ToString();
    }

    void QueueResearch(in FixedString64Bytes name)
    {
        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        EntityQuery researchQ = entityManager.CreateEntityQuery(typeof(Research));
        NativeArray<Entity> researches = researchQ.ToEntityArray(Allocator.Temp);
        researchQ.Dispose();

        Entity researchEntity = default;
        Research research = default;

        foreach (Entity _researchEntity in researches)
        {
            Research _research = entityManager.GetComponentData<Research>(_researchEntity);
            if (_research.Name != name) continue;
            researchEntity = _researchEntity;
            research = _research;
        }

        if (researchEntity == Entity.Null)
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Research \"{name}\" not found in the database");
            return;
        }

        // DynamicBuffer<BufferedResearchRequirement> requirements = entityManager.GetBuffer<BufferedResearchRequirement>(researchEntity);

        GhostInstance ghostInstance = entityManager.GetComponentData<GhostInstance>(selectedEntity);

        NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new FacilityQueueResearchRequestRpc()
        {
            ResearchName = research.Name,
            Entity = ghostInstance,
        });

        if (selected.Current.Name.IsEmpty)
        {
            selected.Current = new BufferedResearch()
            {
                Name = research.Name,
                Hash = research.Hash,
                ResearchTime = research.ResearchTime,
            };
            selected.CurrentProgress = 0f;
        }
        else
        {
            DynamicBuffer<BufferedResearch> queue = entityManager.GetBuffer<BufferedResearch>(selectedEntity);
            queue.Add(new BufferedResearch()
            {
                Name = research.Name,
                Hash = research.Hash,
                ResearchTime = research.ResearchTime,
            });
        }

        NativeList<FixedString64Bytes> avaliableResearches = ResearchSystemClient.GetInstance(entityManager.WorldUnmanaged).AvaliableResearches;
        for (int i = 0; i < avaliableResearches.Length; i++)
        {
            if (avaliableResearches[i] != name) continue;
            avaliableResearches.RemoveAt(i);
            break;
        }

        refreshAt = 0f;
        syncAt = Math.Min(syncAt, Time.time + 0.5f);
    }

    public void Cleanup(UIDocument ui)
    {
        selectedEntity = Entity.Null;
        selected = default;
        refreshAt = float.PositiveInfinity;
        gameObject.SetActive(false);
    }
}
