using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Pool;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial class ProcessorScreenSystemClient : SystemBase
{
    public readonly struct ProcessorScreenInstance
    {
        public readonly GameObject Object;
        public readonly Canvas Canvas;
        public readonly TextMeshProUGUI Text;

        public ProcessorScreenInstance(GameObject @object, Canvas canvas, TextMeshProUGUI text)
        {
            Object = @object;
            Canvas = canvas;
            Text = text;
        }
    }

    public struct TerminalInstance
    {
        public readonly ProcessorScreenInstance Screen;
        public readonly TerminalSubscriptionClient? Subscription;
        public readonly TerminalRenderer Renderer;
        public readonly StringBuilder Builder;
        public ulong Offset;

        public TerminalInstance(ProcessorScreenInstance screen, TerminalSubscriptionClient? subscription)
        {
            Screen = screen;
            Subscription = subscription;
            Renderer = new TerminalRenderer();
            Builder = new();
            Offset = 0;
        }
    }

    readonly Dictionary<Entity, TerminalInstance> Screens = new();
    [NotNull] ObjectPool<GameObject>? ScreenPool = null;
    [NotNull] GameObject? Prefab = null;

    ProcessorScreenInstance CreateScreen(ProcessorScreen options)
    {
        GameObject o = ScreenPool.Get();
        Canvas canvas = o.GetComponentInChildren<Canvas>();
        TextMeshProUGUI text = o.GetComponentInChildren<TextMeshProUGUI>();

        canvas.GetComponent<RectTransform>().sizeDelta = options.Size;
        canvas.transform.SetLocalPositionAndRotation(options.Position, options.Rotation);
        text.fontSize = options.FontSize;

        return new(o, canvas, text);
    }

    protected override void OnStartRunning()
    {
        Screens.Clear();
        ScreenPool?.Clear();

        ScreenPool ??= new(
            createFunc: () => Object.Instantiate(Prefab),
            actionOnGet: o => o.SetActive(true),
            actionOnRelease: o => o.SetActive(false),
            actionOnDestroy: static o => Object.Destroy(o)
        );

        Prefab = SystemAPI.ManagedAPI.GetSingleton<ProcessorScreenOptions>().ScreenPrefab;
    }

    protected override void OnUpdate()
    {
        foreach (var (processor, screenOptions, transform, ghostInstance, entity) in
            SystemAPI.Query<RefRO<Processor>, RefRO<ProcessorScreen>, RefRO<LocalToWorld>, RefRO<GhostInstance>>()
            .WithEntityAccess())
        {
            bool isVisible = math.distance(MainCamera.Camera.transform.position, transform.ValueRO.Position) < 50;

            if (!Screens.TryGetValue(entity, out TerminalInstance screenInstance))
            {
                if (!isVisible) continue;
                TerminalSubscriptionClient? terminalSubscription = World.IsClient() ? World.GetSystem<TerminalSystemClient>().Subscribe(ghostInstance.ValueRO) : null;
                Screens.Add(entity, screenInstance = new(CreateScreen(screenOptions.ValueRO), terminalSubscription));
            }
            else
            {
                if (!isVisible)
                {
                    if (World.IsClient()) World.GetSystem<TerminalSystemClient>().Unsubscribe(ghostInstance.ValueRO);
                    Screens.Remove(entity);
                    ScreenPool.Release(screenInstance.Screen.Object);
                    continue;
                }
            }

            screenInstance.Screen.Object.transform.SetPositionAndRotation(transform.ValueRO.Position, transform.ValueRO.Rotation);

            if (World.IsClient())
            {
                // The subscription will never be null on client side
                System.ReadOnlySpan<byte> stdout = screenInstance.Subscription!.Data.AsReadOnly().AsReadOnlySpan();
                if (screenInstance.Offset != screenInstance.Subscription.Offset)
                {
                    screenInstance.Builder.Clear();
                    screenInstance.Renderer.Rerender(stdout, screenInstance.Builder, (int)(screenInstance.Screen.Canvas.renderingDisplaySize.y / screenInstance.Screen.Text.fontSize));

                    screenInstance.Offset = screenInstance.Subscription.Offset;

                    string stdoutStr = screenInstance.Builder.ToString();
                    if (screenInstance.Screen.Text.text != stdoutStr)
                    {
                        screenInstance.Screen.Text.SetText(stdoutStr);
                    }
                }
            }
            else
            {
                unsafe
                {
                    System.ReadOnlySpan<byte> stdout = new(processor.ValueRO.StdOutBuffer.GetUnsafePtr(), processor.ValueRO.StdOutBuffer.Length);
                    screenInstance.Builder.Clear();
                    screenInstance.Renderer.Rerender(stdout, screenInstance.Builder);

                    string stdoutStr = screenInstance.Builder.ToString();
                    if (screenInstance.Screen.Text.text != stdoutStr)
                    {
                        screenInstance.Screen.Text.SetText(stdoutStr);
                    }
                }
            }
        }
    }
}
