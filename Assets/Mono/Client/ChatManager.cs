using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

public class ChatManager : Singleton<ChatManager>
{
    public enum ChatMessageSenderKind
    {
        System,
        Server,
        Player,
    }

    readonly struct ChatMessage
    {
        public readonly int Sender;
        public readonly string Message;
        public readonly DateTimeOffset Time;

        public ChatMessage(int sender, string message, DateTimeOffset time)
        {
            Sender = sender;
            Message = message;
            Time = time;
        }
    }

    [SerializeField, NotNull] UIDocument? _ui = default;

    [SerializeField, NotNull] VisualTreeAsset? _chatMessageTemplate = default;

    [NotNull] TextField? _inputMessage = default;
    [NotNull] Button? _buttonSend = default;
    [NotNull] ScrollView? _containerMessages = default;
    [NotNull] VisualElement? _containerInput = default;
    readonly List<ChatMessage> _chatMessages = new();

    void OnEnable()
    {
        _inputMessage = _ui.rootVisualElement.Q<TextField>("input-message");
        _buttonSend = _ui.rootVisualElement.Q<Button>("button-send");
        _containerMessages = _ui.rootVisualElement.Q<ScrollView>("container-messages");
        _containerInput = _ui.rootVisualElement.Q<VisualElement>("container-input");

        _buttonSend.clicked += OnButtonSend;

        _containerMessages.Clear();
        _containerInput.style.display = DisplayStyle.None;
    }

    void Update()
    {
        if (_containerMessages.childCount > 0)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (VisualElement child in _containerMessages.Children())
            {
                ChatMessage message = (ChatMessage)child.userData;
                child.EnableInClassList("old", (now - message.Time).TotalSeconds > 3);
                child.EnableInClassList("very-old", (now - message.Time).TotalSeconds > 4);
            }
        }

        if (_containerInput.style.display != DisplayStyle.None && UIManager.Instance.GrapESC())
        {
            _containerInput.style.display = DisplayStyle.None;
            _containerMessages.EnableInClassList("show", false);
        }

        if (!Input.GetKeyDown(KeyCode.Return) || UIManager.Instance.AnyUIVisible || SelectionManager.Instance.IsUnitCommandsActive) return;

        if (_containerInput.style.display == DisplayStyle.None)
        {
            _containerInput.style.display = DisplayStyle.Flex;
            _inputMessage.Focus();
            _containerMessages.EnableInClassList("show", true);
            if (_containerMessages.childCount > 0) _containerMessages.ScrollTo(_containerMessages.Children().Last());
        }
        else
        {
            OnButtonSend();
            _containerInput.style.display = DisplayStyle.None;
            _containerMessages.EnableInClassList("show", false);
        }
    }

    void OnButtonSend()
    {
        ReadOnlySpan<char> message = _inputMessage.value.Trim();
        if (message.Length is 0) return;

        long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        while (!message.IsEmpty)
        {
            const int chunkSize = 30;
            ReadOnlySpan<char> chunk;
            if (message.Length > chunkSize)
            {
                chunk = message[..chunkSize];
                message = message[chunkSize..];
            }
            else
            {
                chunk = message;
                message = ReadOnlySpan<char>.Empty;
            }

            NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new ChatMessageRequestRpc()
            {
                Message = chunk.ToString(),
                Time = time,
            });
        }

        _inputMessage.value = string.Empty;
    }

    public void AppendChatMessageElement(int sender, string? message, DateTimeOffset time)
    {
        if (message is null) return;

        for (int i = 0; i < _chatMessages.Count; i++)
        {
            if (_chatMessages[i].Sender == sender && _chatMessages[i].Time == time)
            {
                _chatMessages[i] = new ChatMessage(sender, _chatMessages[i].Message + message, time);
                goto added;
            }
        }
        _chatMessages.Add(new ChatMessage(sender, message, time));
    added:

        RefreshChatContainer();
    }

    void RefreshChatContainer()
    {
        _containerMessages.SyncList(_chatMessages, _chatMessageTemplate, (item, element, reuse) =>
        {
            ChatMessageSenderKind senderKind = item.Sender switch
            {
                -1 => ChatMessageSenderKind.System,
                0 => World.DefaultGameObjectInjectionWorld.Unmanaged.IsLocal() ? ChatMessageSenderKind.Player : ChatMessageSenderKind.Server,
                _ => ChatMessageSenderKind.Player,
            };
            element.EnableInClassList("server-message", senderKind == ChatMessageSenderKind.Server);
            element.EnableInClassList("system-message", senderKind == ChatMessageSenderKind.System);
            element.EnableInClassList("player-message", senderKind == ChatMessageSenderKind.Player);
            element.userData = item;

            if (item.Sender > 0)
            {
                EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
                using EntityQuery playersQ = entityManager.CreateEntityQuery(typeof(Player));
                using NativeArray<Player> players = playersQ.ToComponentDataArray<Player>(Allocator.Temp);

                string? senderDisplayName = null;

                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i].ConnectionId != item.Sender) continue;
                    senderDisplayName = players[i].Nickname.ToString();
                    break;
                }

                senderDisplayName ??= $"Client#{item.Sender}";

                element.Q<Label>("label-message").text = $"<{senderDisplayName}> {item.Message}";
            }
            else
            {
                element.Q<Label>("label-message").text = item.Message;
            }
        });
    }
}
