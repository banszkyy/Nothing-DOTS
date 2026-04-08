using System;
using System.Diagnostics.CodeAnalysis;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

public class PauseManager : Singleton<PauseManager>, IUISetup, IUICleanup
{
    [Header("UI Assets")]

    [SerializeField, NotNull] VisualTreeAsset? UI_ConnectionItem = default;

    [Header("UI")]

    [SerializeField, SaintsField.ReadOnly] UIDocument? ui = default;

    float refreshAt = default;

    void Update()
    {
        if (((!UIManager.Instance.AnyUIVisible && !SelectionManager.Instance.IsUnitCommandsActive) || !(ui == null || !ui.gameObject.activeSelf)) && UIManager.Instance.GrapESC())
        {
            if (ui == null || !ui.gameObject.activeSelf)
            {
                UIManager.Instance.OpenUI(UIManager.Instance.Pause)
                    .Setup(this);
            }
            else
            {
                UIManager.Instance.CloseUI(this);
            }
            return;
        }

        if (ui == null || !ui.gameObject.activeSelf) return;

        if (Time.time >= refreshAt)
        {
            RefreshUI();
            refreshAt = Time.time + 1f;
        }
    }

    public void Setup(UIDocument ui)
    {
        this.ui = ui;
        refreshAt = 0f;

        ui.rootVisualElement.Q<Button>("button-exit").clicked += OnButtonExit;
        ui.rootVisualElement.Q<Button>("button-save").clicked += OnButtonSave;
    }

    void OnButtonExit()
    {
        ConnectionManager.DisconnectEveryone();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
#else
        Application.Quit();
#endif
    }

    void OnButtonSave()
    {
        if (ConnectionManager.ServerWorld is null)
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Cannot save: server world is null");
        }
        else
        {
            SaveManager.Save(ConnectionManager.ServerWorld, "save.bin");
        }
    }

    public void RefreshUI()
    {
        if (ui == null || !ui.gameObject.activeSelf || ConnectionManager.ClientOrDefaultWorld == null) return;

        ui.rootVisualElement.Q<Button>("button-save").style.display = ConnectionManager.ServerWorld != null ? DisplayStyle.Flex : DisplayStyle.None;

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        using EntityQuery playersQ = entityManager.CreateEntityQuery(typeof(Player));
        using NativeArray<Player> players = playersQ.ToComponentDataArray<Player>(Allocator.Temp);

        if (!PlayerSystemClient.GetInstance(ConnectionManager.ClientOrDefaultWorld.Unmanaged).TryGetLocalPlayer(out Player localPlayer)) localPlayer = default;

        ui.rootVisualElement.Q<ScrollView>("list-connections").SyncList(
            players,
            UI_ConnectionItem,
            (player, element, recycled) =>
            {
                element.userData = player.ConnectionId;
                element.Q<Label>("label-nickname").text = player.Nickname.ToString();
                element.Q<Label>("label-team").text = player.Team.ToString();
                if (ConnectionManager.ClientOrDefaultWorld.Unmanaged.IsLocal())
                {
                    element.Q<Label>("label-ping").style.display = DisplayStyle.None;
                }
                else
                {
                    double ping = TimeSpan.FromTicks(player.Ping).TotalMilliseconds;
                    element.Q<Label>("label-ping").text = $"{Math.Ceiling(ping)} ms";
                    element.Q<Label>("label-ping").style.color = ping switch
                    {
                        <= 0 => new StyleColor(StyleKeyword.Null),
                        <= 30 => new StyleColor(Color.green),
                        <= 100 => new StyleColor(Color.yellow),
                        _ => new StyleColor(Color.red),
                    };
                }
                element.Q<VisualElement>("icon-admin").style.display = player.IsAdmin ? DisplayStyle.Flex : DisplayStyle.None;
                if (!recycled) element.Q<Button>("button-kick").clicked += () =>
                {
                    ConnectionManager.KickClient((int)element.userData);
                    RefreshUI();
                };
                element.Q<Button>("button-kick").style.display = (ConnectionManager.ServerWorld != null && player.ConnectionId != 0 && player.ConnectionId != localPlayer.ConnectionId) ? DisplayStyle.Flex : DisplayStyle.None;
            },
            player => player.ConnectionState is not PlayerConnectionState.Disconnected and not PlayerConnectionState.Server);
    }

    public void Cleanup(UIDocument ui)
    {
        refreshAt = float.PositiveInfinity;
        ui.rootVisualElement.Q<Button>("button-exit").clicked -= OnButtonExit;
        ui.rootVisualElement.Q<Button>("button-save").clicked -= OnButtonSave;
    }
}
