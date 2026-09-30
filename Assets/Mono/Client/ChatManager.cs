using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using SaintsField;
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
    ChatSchema? ui = default;

    [SerializeField, NotNull] VisualTreeAsset? _chatMessageTemplate = default;
    [SerializeField, Min(0), PostFieldRichLabel("sec")] float MessageVisibleTime = 1f;

    readonly List<ChatMessage> _chatMessages = new();

    void OnEnable()
    {
        ui = new ChatSchema(_ui.rootVisualElement);

        ui.ButtonSend.clicked += OnButtonSend;

        ui.ContainerMessages.Clear();
        ui.ContainerInput.style.display = DisplayStyle.None;
    }

    void Update()
    {
        if (ui.ContainerMessages.childCount > 0)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (VisualElement child in ui.ContainerMessages.Children())
            {
                ChatMessage message = (ChatMessage)child.userData;
                child.EnableInClassList("old", (now - message.Time).TotalSeconds > MessageVisibleTime);
                child.EnableInClassList("very-old", (now - message.Time).TotalSeconds > MessageVisibleTime + 1);
            }
        }

        if (ui.ContainerInput.style.display != DisplayStyle.None && UIManager.Instance.GrapESC())
        {
            ui.ContainerInput.style.display = DisplayStyle.None;
            ui.ContainerMessages.EnableInClassList("show", false);
        }

        if (!Input.GetKeyDown(KeyCode.Return) || UIManager.Instance.AnyUIVisible || SelectionManager.Instance.IsUnitCommandsActive) return;

        if (ui.ContainerInput.style.display == DisplayStyle.None)
        {
            ui.ContainerInput.style.display = DisplayStyle.Flex;
            ui.InputMessage.Focus();
            ui.ContainerMessages.EnableInClassList("show", true);
            if (ui.ContainerMessages.childCount > 0) ui.ContainerMessages.ScrollTo(ui.ContainerMessages.Children().Last());
        }
        else
        {
            OnButtonSend();
            ui.ContainerInput.style.display = DisplayStyle.None;
            ui.ContainerMessages.EnableInClassList("show", false);
        }
    }

    void OnButtonSend()
    {
        ReadOnlySpan<char> message = ui.InputMessage.value.Trim();
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

        ui.InputMessage.value = string.Empty;
    }

    public void AppendChatMessageElement(int sender, string? message, DateTimeOffset time)
    {
        if (message is null) return;

        //for (int i = 0; i < _chatMessages.Count; i++)
        //{
        //    if (_chatMessages[i].Sender == sender && _chatMessages[i].Time == time)
        //    {
        //        _chatMessages[i] = new ChatMessage(sender, _chatMessages[i].Message + message, time);
        //        goto added;
        //    }
        //}
        _chatMessages.Add(new ChatMessage(sender, message, time));
        //added:

        RefreshChatContainer();
    }

    void RefreshChatContainer()
    {
        ui.ContainerMessages.SyncList<ChatMessage, ChatMessageSchema>(_chatMessages, _chatMessageTemplate, (item, element, reuse) =>
        {
            ChatMessageSenderKind senderKind = item.Sender switch
            {
                -1 => ChatMessageSenderKind.System,
                0 => World.DefaultGameObjectInjectionWorld.Unmanaged.IsLocal() ? ChatMessageSenderKind.Player : ChatMessageSenderKind.Server,
                _ => ChatMessageSenderKind.Player,
            };
            element.Root.EnableInClassList("server-message", senderKind == ChatMessageSenderKind.Server);
            element.Root.EnableInClassList("system-message", senderKind == ChatMessageSenderKind.System);
            element.Root.EnableInClassList("player-message", senderKind == ChatMessageSenderKind.Player);
            element.Root.userData = item;

            if (item.Sender > 0)
            {
                EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
                using EntityQuery playersQ = entityManager.CreateEntityQuery(typeof(Player), typeof(RealPlayer));
                using NativeArray<Player> players = playersQ.ToComponentDataArray<Player>(Allocator.Temp);
                using NativeArray<RealPlayer> playersR = playersQ.ToComponentDataArray<RealPlayer>(Allocator.Temp);

                string? senderDisplayName = null;

                for (int i = 0; i < players.Length; i++)
                {
                    if (playersR[i].ConnectionId != item.Sender) continue;
                    senderDisplayName = players[i].Nickname.ToString();
                    break;
                }

                senderDisplayName ??= $"Client#{item.Sender}";

                element.LabelMessage.text = $"<{senderDisplayName}> {item.Message}";
            }
            else
            {
                element.LabelMessage.text = item.Message;
            }
        });
    }
}
