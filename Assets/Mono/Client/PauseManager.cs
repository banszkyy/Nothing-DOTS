using System;
using System.Diagnostics.CodeAnalysis;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

public class PauseManager : Singleton<PauseManager>, IUISetup, IUICleanup
{
    [Header("UI Assets")]

    [SerializeField, NotNull] VisualTreeAsset? UI_ConnectionItem = default;

    [Header("UI")]

    PauseMenuSchema? ui;

    float refreshAt;

    void Update()
    {
        if (((!UIManager.Instance.AnyUIVisible && !SelectionManager.Instance.IsUnitCommandsActive) || ui.IsVisible()) && UIManager.Instance.GrapESC())
        {
            if (!ui.IsVisible())
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

        if (!ui.IsVisible()) return;

        if (Time.time >= refreshAt)
        {
            RefreshUI();
            refreshAt = Time.time + 1f;
        }
    }

    public void Setup(UIElementReference ui)
    {
        this.ui = new(ui.Element);
        refreshAt = 0f;

        this.ui.ButtonExit.clicked += OnButtonExit;
        this.ui.ButtonSave.clicked += OnButtonSave;
    }

    void OnButtonExit()
    {
        ConnectionManager.DisconnectEveryone();
        UnityUtils.Quit();
    }

    void OnButtonSave()
    {
        if ((ConnectionManager.ServerWorld ?? ConnectionManager.LocalWorld) is null)
        {
            Debug.LogWarning($"{DebugEx.ClientPrefix} Cannot save on client side");
        }
        else
        {
            SaveManager.Save((ConnectionManager.ServerWorld ?? ConnectionManager.LocalWorld)!, "save.bin");
        }
    }

    public void RefreshUI()
    {
        if (!ui.IsVisible() || ConnectionManager.ClientOrDefaultWorld == null) return;

        ui.ButtonSave.style.display = (ConnectionManager.ServerWorld ?? ConnectionManager.LocalWorld) != null ? DisplayStyle.Flex : DisplayStyle.None;

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        using EntityQuery playersQ = entityManager.CreateEntityQuery(typeof(Player));
        using NativeArray<Entity> players = playersQ.ToEntityArray(Allocator.Temp);

        if (!PlayerSystemClient.GetInstance(ConnectionManager.ClientOrDefaultWorld.Unmanaged).TryGetLocalPlayer(out RealPlayer localPlayer)) localPlayer = default;

        ui.ListConnections.SyncList<Entity, ConnectionItemSchema>(
            players,
            UI_ConnectionItem,
            (playerE, element, recycled) =>
            {
                var player = entityManager.GetComponentData<Player>(playerE);

                element.Root.userData = playerE;
                element.LabelNickname.text = player.Nickname.ToString();
                element.LabelTeam.text = player.Team.ToString();
                if (ConnectionManager.ClientOrDefaultWorld.Unmanaged.IsLocal()
                    || !entityManager.TryGetComponentData<RealPlayer>(playerE, out var playerR))
                {
                    element.LabelPing.style.display = DisplayStyle.None;
                }
                else
                {
                    double ping = TimeSpan.FromTicks(playerR.Ping).TotalMilliseconds;
                    element.LabelPing.text = $"{Math.Ceiling(ping)} ms";
                    element.LabelPing.style.color = ping switch
                    {
                        <= 0 => new StyleColor(StyleKeyword.Null),
                        <= 30 => new StyleColor(Color.green),
                        <= 100 => new StyleColor(Color.yellow),
                        _ => new StyleColor(Color.red),
                    };
                }
                element.IconAdmin.style.display = player.IsAdmin ? DisplayStyle.Flex : DisplayStyle.None;
                if (!recycled) element.ButtonKick.clicked += () =>
                {
                    ConnectionManager.KickClient((int)element.Root.userData);
                    RefreshUI();
                };
                element.ButtonKick.style.display = entityManager.TryGetComponentData<RealPlayer>(playerE, out playerR) ? (ConnectionManager.ServerWorld != null && playerR.ConnectionId != 0 && playerR.ConnectionId != localPlayer.ConnectionId) ? DisplayStyle.Flex : DisplayStyle.None : DisplayStyle.Flex;
            },
            playerE => !entityManager.TryGetComponentData<RealPlayer>(playerE, out var playerR) || playerR.ConnectionState is not PlayerConnectionState.Disconnected and not PlayerConnectionState.Server);
    }

    public void Cleanup(UIElementReference ui)
    {
        refreshAt = float.PositiveInfinity;
        this.ui.ButtonExit.clicked -= OnButtonExit;
        this.ui.ButtonSave.clicked -= OnButtonSave;
    }
}
