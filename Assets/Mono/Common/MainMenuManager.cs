using System.Diagnostics.CodeAnalysis;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Serialization;
using UnityEngine.UIElements;

public class MainMenuManager : Singleton<MainMenuManager>, IUISetup
{
    [DontSerialize] public string? ConnectionError;

    public void Setup(VisualElement _ui)
    {
        MainMenuSchema ui = new(_ui);

        ui.ButtonSingleplayer.clicked += () =>
        {
            if (!HandleInput(ui, out _, out FixedString32Bytes nickname)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartSingleplayer(nickname, null));
        };
        ui.ButtonHost.clicked += () =>
        {
            if (!HandleInput(ui, out NetworkEndpoint endpoint, out FixedString32Bytes nickname)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartHost(endpoint, nickname, null));
        };
        ui.ButtonClient.clicked += () =>
        {
            if (!HandleInput(ui, out NetworkEndpoint endpoint, out FixedString32Bytes nickname)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartClient(endpoint, nickname));
        };
        ui.ButtonServer.clicked += () =>
        {
            if (!HandleInput(ui, out NetworkEndpoint endpoint, out _)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartServer(endpoint, null));
        };
        ui.ButtonStaging.clicked += () =>
        {
            if (!HandleInput(ui, out _, out FixedString32Bytes nickname)) return;
            ConnectionManager.Instance.StartCoroutine(ConnectionManager.Instance.StartStaging(nickname, null));
        };
        ui.ButtonExit.clicked += UnityUtils.Quit;

        if (ConnectionError is not null)
        {
            ui.ErrorConnection.text = ConnectionError;
            ui.ErrorConnection.style.display = DisplayStyle.Flex;
            ConnectionError = null;
        }
        else
        {
            ui.ErrorConnection.text = "";
            ui.ErrorConnection.style.display = DisplayStyle.None;
        }

        ui.InputErrorHost.text = "";
        ui.InputErrorHost.style.display = DisplayStyle.None;

        ui.InputErrorNickname.text = "";
        ui.InputErrorNickname.style.display = DisplayStyle.None;
    }

    bool HandleInput(MainMenuSchema ui, [NotNullWhen(true)] out NetworkEndpoint endpoint, out FixedString32Bytes nickname)
    {
        bool ok = true;

        Label inputErrorLabel = ui.InputErrorHost;
        inputErrorLabel.style.display = DisplayStyle.None;

        string inputNickname = ui.InputNickname.value.Trim();

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

        string inputHost = ui.InputHost.value;
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

    static bool ParseInput(string input, [NotNullWhen(true)] out NetworkEndpoint endpoint, [NotNullWhen(false)] out string? error)
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
