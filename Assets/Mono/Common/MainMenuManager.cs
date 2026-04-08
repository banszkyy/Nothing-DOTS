using System.Diagnostics.CodeAnalysis;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine.UIElements;

public class MainMenuManager : Singleton<MainMenuManager>, IUISetup, IUICleanup
{
    public string? ConnectionError;

    public void Setup(UIDocument ui)
    {
        ui.rootVisualElement.Q<Button>("button-singleplayer").clicked += () =>
        {
            if (!HandleInput(ui, out _, out FixedString32Bytes nickname)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartSingleplayerAsync(nickname, null));
        };
        ui.rootVisualElement.Q<Button>("button-host").clicked += () =>
        {
            if (!HandleInput(ui, out NetworkEndpoint endpoint, out FixedString32Bytes nickname)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartHostAsync(endpoint, nickname, null));
        };
        ui.rootVisualElement.Q<Button>("button-client").clicked += () =>
        {
            if (!HandleInput(ui, out NetworkEndpoint endpoint, out FixedString32Bytes nickname)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartClientAsync(endpoint, nickname));
        };
        ui.rootVisualElement.Q<Button>("button-server").clicked += () =>
        {
            if (!HandleInput(ui, out NetworkEndpoint endpoint, out _)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartServerAsync(endpoint, null));
        };
        ui.rootVisualElement.Q<Button>("button-staging").clicked += () =>
        {
            if (!HandleInput(ui, out _, out FixedString32Bytes nickname)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartStagingAsync(nickname, null));
        };

        if (ConnectionError is not null)
        {
            ui.rootVisualElement.Q<Label>("error-connection").text = ConnectionError;
            ui.rootVisualElement.Q<Label>("error-connection").style.display = DisplayStyle.Flex;
            ConnectionError = null;
        }
        else
        {
            ui.rootVisualElement.Q<Label>("error-connection").text = "";
            ui.rootVisualElement.Q<Label>("error-connection").style.display = DisplayStyle.None;
        }

        ui.rootVisualElement.Q<Label>("input-error-host").text = "";
        ui.rootVisualElement.Q<Label>("input-error-host").style.display = DisplayStyle.None;

        ui.rootVisualElement.Q<Label>("input-error-nickname").text = "";
        ui.rootVisualElement.Q<Label>("input-error-nickname").style.display = DisplayStyle.None;
    }

    public void Cleanup(UIDocument ui)
    {

    }

    bool HandleInput(UIDocument ui, [NotNullWhen(true)] out NetworkEndpoint endpoint, out FixedString32Bytes nickname)
    {
        bool ok = true;

        Label inputErrorLabel = ui.rootVisualElement.Q<Label>("input-error-host");
        inputErrorLabel.style.display = DisplayStyle.None;

        string inputNickname = ui.rootVisualElement.Q<TextField>("input-nickname").value.Trim();

        if (inputNickname.Length >= FixedString32Bytes.UTF8MaxLengthInBytes)
        {
            inputErrorLabel.text = "Too long nickname";
            inputErrorLabel.style.display = DisplayStyle.Flex;
            ok = false;
        }
        else if (string.IsNullOrEmpty(inputNickname))
        {
            inputErrorLabel.text = "Empty nickname";
            inputErrorLabel.style.display = DisplayStyle.Flex;
            ok = false;
        }

        nickname = inputNickname;

        string inputHost = ui.rootVisualElement.Q<TextField>("input-host").value;
        if (!ParseInput(inputHost, out endpoint, out string? inputErrorHost))
        {
            inputErrorLabel.text = inputErrorHost;
            inputErrorLabel.style.display = DisplayStyle.Flex;
            ok = false;
        }
        if (ok)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    bool ParseInput(
        string input,
        [NotNullWhen(true)] out NetworkEndpoint endpoint,
        [NotNullWhen(false)] out string? error)
    {
        if (!input.Contains(':'))
        {
            error = $"Invalid host input";
            endpoint = default;
            return false;
        }

        if (!ushort.TryParse(input.Split(':')[1], out ushort port))
        {
            error = $"Invalid host input";
            endpoint = default;
            return false;
        }

        if (!NetworkEndpoint.TryParse(input.Split(':')[0], port, out endpoint))
        {
            error = $"Invalid host input";
            return false;
        }

        error = null;
        return true;
    }
}
