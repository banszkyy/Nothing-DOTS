using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.UIElements;

public class HUDManager : Singleton<HUDManager>
{
    [SerializeField, NotNull] UIDocument? _ui = default;

    [NotNull] public Label? _labelResources = default;
    [NotNull] public Label? _labelTeam = default;
    [NotNull] public Label? _labelFps = default;
    [NotNull] public Label? _labelSelectedUnits = default;

    float _refreshAt = default;
    float _maxDeltaTime = default;

    void OnEnable()
    {
        _labelResources = _ui.rootVisualElement.Q<Label>("label-resources");
        _labelFps = _ui.rootVisualElement.Q<Label>("label-fps");
        _labelTeam = _ui.rootVisualElement.Q<Label>("label-team");
        _labelSelectedUnits = _ui.rootVisualElement.Q<Label>("label-selected-units");
    }

    void Update()
    {
        float now = Time.time;
        _maxDeltaTime = MathF.Max(_maxDeltaTime, Time.deltaTime);
        if (now < _refreshAt) return;
        _refreshAt = now + 1f;

        float fps = 1f / _maxDeltaTime;
        _labelFps.text = float.IsInfinity(fps) || float.IsNaN(fps) ? "N/A" : MathF.Round(1f / _maxDeltaTime).ToString();
        _maxDeltaTime = 0f;

        if (PlayerSystemClient.GetInstance(ConnectionManager.ClientOrDefaultWorld.Unmanaged).TryGetLocalPlayer(out Player localPlayer))
        {
            _labelResources.text = localPlayer.Resources.ToString();
            _labelTeam.text = localPlayer.Team.ToString();

            _labelResources.parent.style.display = DisplayStyle.Flex;
            _labelTeam.parent.style.display = DisplayStyle.Flex;
        }
        else
        {
            _labelResources.parent.style.display = DisplayStyle.None;
            _labelTeam.parent.style.display = DisplayStyle.None;
        }
    }
}
