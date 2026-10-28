using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.UIElements;

public class HUDManager : Singleton<HUDManager>
{
    [SerializeField, NotNull] UIDocument? _ui = default;
    internal HUDSchema? ui;

    float _refreshAt;
    float _maxDeltaTime;

    void OnEnable()
    {
        ui = new(_ui.rootVisualElement);
        ConnectionManager.ClientOrDefaultWorld.GetSystem<DebugLabelSystemClient>().IsEnabled = ui.DebugOverlayToggle.value;
        ConnectionManager.ClientOrDefaultWorld.Unmanaged.GetSystem<DebugLinesSystemClient>().IsEnabled = ui.DebugOverlayToggle.value;
        ui.DebugOverlayToggle.RegisterValueChangedCallback(e =>
        {
            if (ConnectionManager.ClientOrDefaultWorld is null) return;
            ConnectionManager.ClientOrDefaultWorld.GetSystem<DebugLabelSystemClient>().IsEnabled = e.newValue;
            ConnectionManager.ClientOrDefaultWorld.Unmanaged.GetSystem<DebugLinesSystemClient>().IsEnabled = e.newValue;
        });
    }

    void Update()
    {
        float now = Time.time;
        _maxDeltaTime = MathF.Max(_maxDeltaTime, Time.deltaTime);
        if (now < _refreshAt) return;
        _refreshAt = now + 1f;

        if (!ui.IsVisible()) return;

        float fps = 1f / _maxDeltaTime;
        ui.LabelFps.text = float.IsInfinity(fps) || float.IsNaN(fps) ? "N/A" : MathF.Round(1f / _maxDeltaTime).ToString();
        _maxDeltaTime = 0f;

        if (PlayerSystemClient.GetInstance(ConnectionManager.ClientOrDefaultWorld.Unmanaged).TryGetLocalPlayer(out Player localPlayer))
        {
            ui.LabelResources.text = localPlayer.Resources.ToString();
            ui.LabelTeam.text = localPlayer.Team.ToString();

            ui.LabelResources.parent.style.display = DisplayStyle.Flex;
            ui.LabelTeam.parent.style.display = DisplayStyle.Flex;
        }
        else
        {
            ui.LabelResources.parent.style.display = DisplayStyle.None;
            ui.LabelTeam.parent.style.display = DisplayStyle.None;
        }
    }
}
