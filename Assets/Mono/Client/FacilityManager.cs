using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

public class FacilityManager : Singleton<FacilityManager>, IUISetup<Entity>, IUICleanup
{
    [SerializeField, NotNull] AllPrefabs? Prefabs = default;

    [Header("UI")]

    [SerializeField, NotNull] VisualTreeAsset? UI_ResearchItem = default;

    FacilitySchema? ui;

    Entity selectedEntity = Entity.Null;
    Facility selected = default;

    float refreshAt = default;
    float refreshedBySyncAt = default;
    float syncAt = default;

    void Update()
    {
        if (!ui.IsVisible()) return;

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

        selected.CurrentProgress += Time.deltaTime * Facility.ResearchSpeed;
        ui.CurrentProgressbar.value = selected.CurrentProgress / selected.Current.ResearchTime;
    }

    public void Setup(UIElementReference ui, Entity entity)
    {
        this.ui = new(ui.Element);

        syncAt = Math.Min(syncAt, Time.time + 0.5f);

        selectedEntity = entity;
        RefreshUI(entity);
    }

    public void RefreshUI(Entity entity)
    {
        if (!ui.IsVisible()) return;

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        ScrollView avaliableList = ui.AvailableScrollview;
        ScrollView queueList = ui.QueueScrollview;

        avaliableList.Clear();
        queueList.Clear();

        DynamicBuffer<BufferedResearch> queue = entityManager.GetBuffer<BufferedResearch>(entity);

        queueList.SyncList<BufferedResearch, ResearchItemSchema>(queue, UI_ResearchItem, (item, element, recycled) =>
        {
            ResearchMetadata v = Prefabs.Researches.First(v => v.Name == item.Name.ToString());
            element.Root.userData = v.Name;
            element.NameLabel.text = v.Name;
            element.LabelResources.text = v.ResearchTime.ToString();
            element.Thumbnail.image = null;
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

        avaliableList.SyncList<FixedString64Bytes, ResearchItemSchema>(avaliableNotInQueue, UI_ResearchItem, (item, element, recycled) =>
        {
            ResearchMetadata v = Prefabs.Researches.First(v => v.Name == item.ToString());
            element.Root.userData = v.Name;
            element.NameLabel.text = v.Name;
            element.LabelResources.text = v.ResearchTime.ToString();
            element.Thumbnail.image = null;
            if (!recycled) element.ButtonSelect.clicked += () => QueueResearch((string)element.Root.userData);
        });

        avaliableNotInQueue.Dispose();

        if (selected.Current.Name != default)
        {
            ui.CurrentProgressbar.value = selected.CurrentProgress / selected.Current.ResearchTime;
            ui.CurrentContainer.style.visibility = Visibility.Visible;

            VisualElement? _element = ui.CurrentContainer.Children().FirstOrDefault();
            if (_element is null)
            {
                _element = UI_ResearchItem.Instantiate();
                ui.CurrentContainer.Add(_element);
            }
            ResearchItemSchema element = new(_element);
            ResearchMetadata v = Prefabs.Researches.First(v => v.Name == selected.Current.Name.ToString());

            element.Root.userData = v.Name;
            element.Thumbnail.image = null;
            element.LabelResources.text = v.ResearchTime.ToString();
            element.NameLabel.text = v.Name;
            element.Root.tooltip = v.Name;
            Tooltips.Instance.Reregister(element.Root);
        }
        else
        {
            ui.CurrentProgressbar.value = 0f;
            ui.CurrentContainer.style.visibility = Visibility.Hidden;
        }
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

    public void Cleanup(UIElementReference ui)
    {
        selectedEntity = Entity.Null;
        selected = default;
        refreshAt = float.PositiveInfinity;
    }
}
