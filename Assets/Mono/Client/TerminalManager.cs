using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web;
using LanguageCore;
using LanguageCore.Runtime;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[Flags]
enum TerminalColor : byte
{
    Black = 0b_0000,
    Blue = 0b_0001,
    Green = 0b_0010,
    Cyan = 0b_0011,
    Red = 0b_0100,
    Magenta = 0b_0101,
    Yellow = 0b_0110,
    White = 0b_0111,
    BrightBlack = 0b_1000,
    BrightBlue = 0b_1001,
    BrightGreen = 0b_1010,
    BrightCyan = 0b_1011,
    BrightRed = 0b_1100,
    BrightMagenta = 0b_1101,
    BrightYellow = 0b_1110,
    BrightWhite = 0b_1111,
}

[Flags]
enum TerminalMode : byte
{
    None = 0b_0000_0000,
    Bold = 0b_0000_0001,
    Dim = 0b_0000_0010,
    Italic = 0b_0000_0100,
    Underline = 0b_0000_1000,
    Blinking = 0b_0001_0000,
    Inverse = 0b_0010_0000,
    Hidden = 0b_0100_0000,
    Strikethrough = 0b_1000_0000,
}

readonly struct TerminalCharacter
{
    public readonly char Character;
    public readonly TerminalMode Mode;
    public readonly TerminalColor Color;
    public TerminalColor Foreground => (TerminalColor)((int)Color & 0b1111);
    public TerminalColor Background => (TerminalColor)((int)Color >> 4);

    public TerminalCharacter(char character, TerminalMode mode, TerminalColor color)
    {
        Character = character;
        Mode = mode;
        Color = color;
    }

    public override string ToString() => Character.ToString();
}

public class TerminalRenderer
{
    TerminalColor compiledBg;
    TerminalColor compiledFg;
    TerminalMode compiledMod;
    readonly List<(List<TerminalCharacter> Columns, bool EndsWithEOL)> compiled;
    int cursorX;
    int cursorY;
    int compiledLines;

    public TerminalRenderer()
    {
        compiledBg = TerminalColor.Black;
        compiledFg = TerminalColor.White;
        compiledMod = TerminalMode.None;
        compiled = new() { (new(), false) };
        cursorX = 0;
        cursorY = 0;
        compiledLines = 0;
    }

    public void Reset()
    {
        compiledBg = TerminalColor.Black;
        compiledFg = TerminalColor.White;
        compiledMod = TerminalMode.None;
        Clear();
        cursorX = 0;
        cursorY = 0;
        compiledLines = 0;
    }

    void Clear()
    {
        for (int i = 0; i < compiled.Count; i++)
        {
            compiled[i].Columns.Clear();
            compiled[i] = (compiled[i].Columns, false);
        }
        compiledLines = 0;
    }

    void Compile(ReadOnlySpan<byte> stdout)
    {
        for (int i = 0; i < stdout.Length; i++)
        {
            switch (stdout[i])
            {
                case (byte)'\b':
                    if (cursorX > 0) compiled[cursorY].Columns.RemoveAt(--cursorX);
                    break;
                case (byte)'\n':
                    compiled[cursorY] = (compiled[cursorY].Columns, true);
                    cursorY++;
                    cursorX = 0;
                    if (cursorY >= compiled.Count) compiled.Add((new(), false));
                    compiledLines = Math.Max(compiledLines, cursorY);
                    break;
                case (byte)'\r':
                    cursorX = 0;
                    break;
                case (byte)'\x1b':
                    if (++i >= stdout.Length) break;
                    if (stdout[i] == (byte)'[')
                    {
                        if (++i >= stdout.Length) break;
                        switch (stdout[i])
                        {
                            case (byte)'H':
                            {
                                cursorX = 0;
                                cursorY = 0;
                                break;
                            }
                            case (byte)'J':
                            {
                                cursorX = 0;
                                cursorY = 0;
                                Clear();
                                break;
                            }
                            case >= ((byte)'0') and <= ((byte)'9'):
                            {
                                int num = 0;
                                while (stdout[i] is >= ((byte)'0') and <= ((byte)'9'))
                                {
                                    num *= 10;
                                    num += stdout[i] - '0';
                                    if (++i >= stdout.Length) break;
                                }

                                if (i >= stdout.Length) break;

                                if (stdout[i] == (byte)'m')
                                {
                                    switch (num)
                                    {
                                        case 0:
                                            compiledBg = TerminalColor.Black;
                                            compiledFg = TerminalColor.White;
                                            compiledMod = TerminalMode.None;
                                            break;
                                        case 1: compiledMod |= TerminalMode.Bold; break;
                                        case 2: compiledMod |= TerminalMode.Dim; break;
                                        case 3: compiledMod |= TerminalMode.Italic; break;
                                        case 4: compiledMod |= TerminalMode.Underline; break;
                                        case 5: compiledMod |= TerminalMode.Blinking; break;
                                        case 6: break;
                                        case 7: compiledMod |= TerminalMode.Inverse; break;
                                        case 8: compiledMod |= TerminalMode.Hidden; break;
                                        case 9: compiledMod |= TerminalMode.Strikethrough; break;
                                        case 22:
                                            compiledMod &= ~TerminalMode.Bold;
                                            compiledMod &= ~TerminalMode.Dim;
                                            break;
                                        case 23: compiledMod &= ~TerminalMode.Italic; break;
                                        case 24: compiledMod &= ~TerminalMode.Underline; break;
                                        case 25: compiledMod &= ~TerminalMode.Blinking; break;
                                        case 27: compiledMod &= ~TerminalMode.Inverse; break;
                                        case 28: compiledMod &= ~TerminalMode.Hidden; break;
                                        case 29: compiledMod &= ~TerminalMode.Strikethrough; break;

                                        case 30: compiledFg = TerminalColor.Black; break;
                                        case 31: compiledFg = TerminalColor.Red; break;
                                        case 32: compiledFg = TerminalColor.Green; break;
                                        case 33: compiledFg = TerminalColor.Yellow; break;
                                        case 34: compiledFg = TerminalColor.Blue; break;
                                        case 35: compiledFg = TerminalColor.Magenta; break;
                                        case 36: compiledFg = TerminalColor.Cyan; break;
                                        case 37: compiledFg = TerminalColor.White; break;
                                        case 39: compiledFg = TerminalColor.White; break;

                                        case 40: compiledBg = TerminalColor.Black; break;
                                        case 41: compiledBg = TerminalColor.Red; break;
                                        case 42: compiledBg = TerminalColor.Green; break;
                                        case 43: compiledBg = TerminalColor.Yellow; break;
                                        case 44: compiledBg = TerminalColor.Blue; break;
                                        case 45: compiledBg = TerminalColor.Magenta; break;
                                        case 46: compiledBg = TerminalColor.Cyan; break;
                                        case 47: compiledBg = TerminalColor.White; break;
                                        case 49: compiledBg = TerminalColor.Black; break;

                                        case 90: compiledFg = TerminalColor.BrightBlack; break;
                                        case 91: compiledFg = TerminalColor.BrightRed; break;
                                        case 92: compiledFg = TerminalColor.BrightGreen; break;
                                        case 93: compiledFg = TerminalColor.BrightYellow; break;
                                        case 94: compiledFg = TerminalColor.BrightBlue; break;
                                        case 95: compiledFg = TerminalColor.BrightMagenta; break;
                                        case 96: compiledFg = TerminalColor.BrightCyan; break;
                                        case 97: compiledFg = TerminalColor.BrightWhite; break;

                                        case 100: compiledBg = TerminalColor.BrightBlack; break;
                                        case 101: compiledBg = TerminalColor.BrightRed; break;
                                        case 102: compiledBg = TerminalColor.BrightGreen; break;
                                        case 103: compiledBg = TerminalColor.BrightYellow; break;
                                        case 104: compiledBg = TerminalColor.BrightBlue; break;
                                        case 105: compiledBg = TerminalColor.BrightMagenta; break;
                                        case 106: compiledBg = TerminalColor.BrightCyan; break;
                                        case 107: compiledBg = TerminalColor.BrightWhite; break;

                                        default: break;
                                    }
                                }
                                break;
                            }
                            default:
                            {
                                break;
                            }
                        }
                    }
                    else
                    {

                    }
                    break;
                default:
                    if (cursorX + 1 == compiled[cursorY].Columns.Count)
                    {
                        compiled[cursorY].Columns.Add(new((char)stdout[i], compiledMod, (TerminalColor)((byte)compiledBg << 4) | compiledFg));
                        cursorX++;
                    }
                    else
                    {
                        compiled[cursorY].Columns.Insert(cursorX++, new((char)stdout[i], compiledMod, (TerminalColor)((byte)compiledBg << 4) | compiledFg));
                    }
                    break;
            }
        }
    }

    void Render(StringBuilder builder, int maxLines = -1)
    {
        TerminalColor appliedBg = TerminalColor.Black;
        TerminalColor appliedFg = TerminalColor.White;
        TerminalMode appliedMod = TerminalMode.None;

        int start = (maxLines == -1) ? 0 : Math.Max(0, compiledLines - maxLines);
        for (int i = start; i < compiledLines; i++)
        {
            foreach (TerminalCharacter c in compiled[i].Columns)
            {
                if (appliedMod != c.Mode)
                {
                    TerminalMode changed = c.Mode ^ appliedMod;

                    if (changed.HasFlag(TerminalMode.Bold)) builder.Append(c.Mode.HasFlag(TerminalMode.Bold) ? "<b>" : "</b>");
                    if (changed.HasFlag(TerminalMode.Italic)) builder.Append(c.Mode.HasFlag(TerminalMode.Italic) ? "<i>" : "</i>");
                    if (changed.HasFlag(TerminalMode.Strikethrough)) builder.Append(c.Mode.HasFlag(TerminalMode.Strikethrough) ? "<s>" : "</s>");
                    if (changed.HasFlag(TerminalMode.Underline)) builder.Append(c.Mode.HasFlag(TerminalMode.Underline) ? "<u>" : "</u>");
                    if (changed.HasFlag(TerminalMode.Blinking)) { }
                    if (changed.HasFlag(TerminalMode.Dim)) { }
                    if (changed.HasFlag(TerminalMode.Hidden)) { }
                    if (changed.HasFlag(TerminalMode.Inverse)) { }

                    appliedMod = c.Mode;
                }

                if (appliedFg != c.Foreground)
                {
                    builder.Append("<color=");
                    builder.Append(c.Foreground switch
                    {
                        TerminalColor.Black => "#000",
                        TerminalColor.Blue => "#2472c8",
                        TerminalColor.Green => "#0dbc79",
                        TerminalColor.Cyan => "#11a8cd",
                        TerminalColor.Red => "#cd3131",
                        TerminalColor.Magenta => "#bc3fbc",
                        TerminalColor.Yellow => "#e5e510",
                        TerminalColor.White => "#e5e5e5",
                        TerminalColor.BrightBlack => "#666666",
                        TerminalColor.BrightBlue => "#3b8eea",
                        TerminalColor.BrightGreen => "#23d18b",
                        TerminalColor.BrightCyan => "#29b8db",
                        TerminalColor.BrightRed => "#f14c4c",
                        TerminalColor.BrightMagenta => "#d670d6",
                        TerminalColor.BrightYellow => "#f5f543",
                        TerminalColor.BrightWhite => "#ffffff",
                        _ => throw new UnreachableException(),
                    });
                    builder.Append('>');
                    appliedFg = c.Foreground;
                }

                if (appliedBg != c.Foreground)
                {
                    builder.Append("<mark=");
                    builder.Append(c.Background switch
                    {
                        TerminalColor.Black => "#00000000",
                        TerminalColor.Blue => "#2472c8ff",
                        TerminalColor.Green => "#0dbc79ff",
                        TerminalColor.Cyan => "#11a8cdff",
                        TerminalColor.Red => "#cd3131ff",
                        TerminalColor.Magenta => "#bc3fbcff",
                        TerminalColor.Yellow => "#e5e510ff",
                        TerminalColor.White => "#e5e5e5ff",
                        TerminalColor.BrightBlack => "#666666ff",
                        TerminalColor.BrightBlue => "#3b8eeaff",
                        TerminalColor.BrightGreen => "#23d18bff",
                        TerminalColor.BrightCyan => "#29b8dbff",
                        TerminalColor.BrightRed => "#f14c4cff",
                        TerminalColor.BrightMagenta => "#d670d6ff",
                        TerminalColor.BrightYellow => "#f5f543ff",
                        TerminalColor.BrightWhite => "#ffffffff",
                        _ => throw new UnreachableException(),
                    });
                    builder.Append('>');
                    appliedBg = c.Background;
                }

                builder.Append(c.Character);
            }
            if (i + 1 < compiledLines || compiled[i].EndsWithEOL) builder.AppendLine();
        }
    }

    public void Rerender(ReadOnlySpan<byte> stdout, StringBuilder builder, int maxLines = -1)
    {
        Reset();
        Compile(stdout);
        Render(builder, maxLines);
    }

    public void Render(ReadOnlySpan<byte> stdout, StringBuilder builder, int maxLines = -1)
    {
        Compile(stdout);
        Render(builder, maxLines);
    }
}

public class TerminalManager : Singleton<TerminalManager>, IUISetup<Entity>, IUICleanup
{
    Entity selectedUnitEntity = Entity.Null;
    float refreshAt;
    ImmutableArray<string> selectingFile = ImmutableArray<string>.Empty;

    [Header("UI")]
    [SerializeField, NotNull] VisualTreeAsset? FileItem = default;
    [SerializeField, NotNull] VisualTreeAsset? ProgressItem = default;
    [SerializeField, NotNull] VisualTreeAsset? DiagnosticsItem = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsErrorIcon = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsWarningIcon = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsInfoIcon = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsHintIcon = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsOptimizationNoticeIcon = default;
    [SerializeField, NotNull] VisualTreeAsset? LogItem = default;
    ProcessorSchema? ui;

    TerminalSubscriptionClient? terminalSubscription;
    readonly StringBuilder _terminalBuilder = new();
    readonly TerminalRenderer _terminalRenderer = new();
    readonly Queue<byte> StandardInput = new();
    byte[]? _memory;
    ProgressRecord<(int, int)>? _memoryDownloadProgress;
    Awaitable<RemoteFile>? _memoryDownloadTask;
    string? _scheduledSource;
    float _terminalCursorBlinkRestart;
    UnitLogSystemClient.UnitLog? _unitLog;
    int? _localConnectionId;
    float _memoryLastCheck;

    void Update()
    {
        if (!ui.IsVisible()) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            return;
        }

        if (Time.time >= refreshAt || !selectingFile.IsDefault)
        {
            if (!ConnectionManager.ClientOrDefaultWorld.EntityManager.Exists(selectedUnitEntity))
            {
                UIManager.Instance.CloseUI(this);
                return;
            }

            RefreshUI(selectedUnitEntity);
            refreshAt = Time.time + .2f;
            return;
        }
    }

    public void Setup(UIElementReference _ui, Entity unitEntity)
    {
        ui = new(_ui.Element);

        selectedUnitEntity = unitEntity;
        refreshAt = Time.time + .2f;
        selectingFile = ImmutableArray<string>.Empty;
        _memory = null;
        _unitLog?.Dispose();
        _unitLog = null;
        _memoryDownloadProgress = null;
        // try { _memoryDownloadTask?.Cancel(); } catch { }
        _memoryDownloadTask = null;
        _scheduledSource = null;

        if (ConnectionManager.ClientOrDefaultWorld.IsLocal() || ConnectionManager.ClientOrDefaultWorld.IsServer())
        {
            _localConnectionId = 0;
        }
        else if (PlayerSystemClient.GetInstance(ConnectionManager.ClientOrDefaultWorld.Unmanaged).TryGetLocalPlayer(out RealPlayer localPlayer))
        {
            _localConnectionId = localPlayer.ConnectionId;
        }
        else
        {
            _localConnectionId = null;
        }

        ui.PanelDebug.style.display = DisplayStyle.None;
        ui.ScrollDiagnostics.style.display = DisplayStyle.None;
        ui.ScrollProgresses.style.display = DisplayStyle.None;
        ui.ScrollLogs.style.display = DisplayStyle.None;
        ui.FilesContainer.style.display = DisplayStyle.None;
        ui.PanelHeap.style.display = DisplayStyle.None;

        ui.LabelTerminal.text = string.Empty;
        ui.ScrollFiles.Clear();
        ui.ScrollProgresses.Clear();
        ui.ScrollDiagnostics.Clear();
        ui.LabelBasePath.text = FileChunkManagerSystem.BasePath;

        {
            EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
            Processor processor = entityManager.GetComponentData<Processor>(unitEntity);
            ui.InputSourcePath.value = processor.SourceFile.Name.ToString();
        }

        EndFileSelection();

        if (!string.IsNullOrWhiteSpace(FileChunkManagerSystem.BasePath))
        {
            ui.ButtonSelect.SetEnabled(true);
            ui.ButtonSelect.clickable = new Clickable(() =>
            {
                if (selectingFile.IsDefaultOrEmpty)
                {
                    selectingFile = Directory.GetFiles(FileChunkManagerSystem.BasePath)
                        .Select(v => Path.GetRelativePath(FileChunkManagerSystem.BasePath, v))
                        .Where(v => !v.EndsWith(".meta"))
                        .ToImmutableArray();
                    BeginFileSelection();
                }
                else
                {
                    selectingFile = ImmutableArray<string>.Empty;
                    EndFileSelection();
                }
            });
        }
        else
        {
            ui.ButtonSelect.SetEnabled(false);
        }

        ui.ButtonCompile.clickable = new Clickable(() =>
        {
            World world = ConnectionManager.ClientOrDefaultWorld;

            if (world.IsServer())
            {
                Debug.LogError($"Not implemented");
                return;
            }

            string file =
                string.IsNullOrWhiteSpace(FileChunkManagerSystem.BasePath)
                ? ui.InputSourcePath.value
                : Path.Combine(FileChunkManagerSystem.BasePath, ui.InputSourcePath.value);

            NetcodeUtils.CreateRPC(world.Unmanaged, new SetProcessorSourceRequestRpc()
            {
                Source = ui.InputSourcePath.value,
                Entity = world.EntityManager.GetComponentData<GhostInstance>(unitEntity),
                IsHotReload = false,
            });

            _scheduledSource = ui.InputSourcePath.value;
        });

        ui.ButtonHotreload.clickable = new Clickable(() =>
        {
            World world = ConnectionManager.ClientOrDefaultWorld;

            if (world.IsServer())
            {
                Debug.LogError($"Not implemented");
                return;
            }

            string file =
                string.IsNullOrWhiteSpace(FileChunkManagerSystem.BasePath)
                ? ui.InputSourcePath.value
                : Path.Combine(FileChunkManagerSystem.BasePath, ui.InputSourcePath.value);

            NetcodeUtils.CreateRPC(world.Unmanaged, new SetProcessorSourceRequestRpc()
            {
                Source = ui.InputSourcePath.value,
                Entity = world.EntityManager.GetComponentData<GhostInstance>(unitEntity),
                IsHotReload = true,
            });

            _scheduledSource = ui.InputSourcePath.value;
        });

        ui.ButtonReset.clickable = new Clickable(() =>
        {
            World world = ConnectionManager.ClientOrDefaultWorld;

            if (world.IsServer())
            {
                Debug.LogError($"Not implemented");
                return;
            }

            NetcodeUtils.CreateRPC(world.Unmanaged, new ProcessorCommandRequestRpc()
            {
                Entity = world.EntityManager.GetComponentData<GhostInstance>(unitEntity),
                Command = ProcessorCommand.Reset,
                Data = default,
            });
        });

        ui.ButtonHaltContinue.clickable = new Clickable(() =>
        {
            World world = ConnectionManager.ClientOrDefaultWorld;

            if (world.IsServer())
            {
                Debug.LogError($"Not implemented");
                return;
            }

            NetcodeUtils.CreateRPC(world.Unmanaged, new ProcessorCommandRequestRpc()
            {
                Entity = world.EntityManager.GetComponentData<GhostInstance>(unitEntity),
                Command = ProcessorCommand.HaltContinue,
                Data = default,
            });
        });

        ui.ButtonLock.clickable = new Clickable(() =>
        {
            World world = ConnectionManager.ClientOrDefaultWorld;
            NetcodeUtils.CreateRPC(world.Unmanaged, new ToggleSourceLockRequestRpc()
            {
                Entity = world.EntityManager.GetComponentData<GhostInstance>(unitEntity),
            });
        });

        ui.ButtonStopDebug.clickable = new Clickable(() =>
        {
            World world = ConnectionManager.ClientOrDefaultWorld;
            NetcodeUtils.CreateRPC(world.Unmanaged, new StopDebugRequestRpc()
            {
                Entity = world.EntityManager.GetComponentData<GhostInstance>(unitEntity),
            });
        });

        ui.ButtonStartDebug.SetEnabled(true);
        ui.ButtonStartDebug.clickable = new Clickable(() =>
        {
            Processor processor = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Processor>(unitEntity);

            if (processor.IsVersionLocked)
            {
                return;
            }

            World world = ConnectionManager.ClientOrDefaultWorld;
            if (world.IsLocal())
            {
                ui.ButtonStartDebug.SetEnabled(false);
                Application.OpenURL($"vscode://banszky.nothingame/debug?entity={ExtensionHostUtils.Stringify(unitEntity)}&token={default(Guid)}");
            }
            else
            {
                Guid guid = PlayerSystemClient.GetInstance(world.Unmanaged).PlayerGuid;
                if (guid == default)
                {
                    Debug.LogError($"{DebugEx.ClientPrefix} Invalid local guid {guid}");
                    return;
                }
                SpawnedGhost ghost = world.EntityManager.GetComponentData<GhostInstance>(unitEntity);
                ui.ButtonStartDebug.SetEnabled(false);
                Application.OpenURL($"vscode://banszky.nothingame/debug?ghost={ExtensionHostUtils.Stringify(ghost)}&token={guid}");
            }
        });

        ui.LabelTerminal.RegisterCallback<FocusInEvent>(e =>
        {
            _terminalCursorBlinkRestart = Time.time;
        });

        RefreshUI(unitEntity);
    }

    void BeginFileSelection()
    {
        ui.ButtonCompile.SetEnabled(false);
        ui.ButtonHotreload.SetEnabled(false);
        ui.ButtonHaltContinue.SetEnabled(false);
        ui.ButtonReset.SetEnabled(false);

        ui.FilesContainer.style.display = DisplayStyle.Flex;
        ui.ScrollProgresses.style.display = DisplayStyle.None;
        ui.ScrollDiagnostics.style.display = DisplayStyle.None;
        ui.ScrollLogs.style.display = DisplayStyle.None;
    }

    void EndFileSelection()
    {
        ui.ButtonCompile.SetEnabled(true);
        ui.ButtonHotreload.SetEnabled(true);
        ui.ButtonHaltContinue.SetEnabled(true);
        ui.ButtonReset.SetEnabled(true);
        ui.ScrollFiles.Clear();

        ui.FilesContainer.style.display = DisplayStyle.None;
    }

    void RefreshMemoryUsage(Entity unitEntity)
    {
        if (!ConnectionManager.ClientOrDefaultWorld.Unmanaged.IsLocal())
        {
            ui.PanelHeap.style.display = DisplayStyle.None;
            return;
        }

        Processor processor = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Processor>(unitEntity);

        if (processor.IsVersionLocked)
        {
            ui.PanelHeap.style.display = DisplayStyle.None;
            return;
        }

        if (!ConnectionManager.ClientOrDefaultWorld.GetSystem<CompilerSystemServer>().CompiledSources.TryGetValue(processor.SourceFile, out var source))
        {
            ui.PanelHeap.style.display = DisplayStyle.None;
            return;
        }

        BytecodeProcessor bytecodeProcessor = new(
            ProcessorSystemServer.BytecodeInterpreterSettings,
            source.Generated.Code,
            processor.Memory.Memory.ToBytes(),
            source.Generated.DebugInfo,
            source.Compiled.ExternalFunctions,
            source.GeneratedFunctions?.ToArray().AsImmutableArrayUnsafe() ?? default
        );

        ImmutableArray<HeapUtils.HeapBlock> blocks;

        try
        {
            if (!HeapUtils.AnalyzeMemorySync(bytecodeProcessor, source.Generated.ExposedFunctions, out blocks, out string? error))
            {
                ui.PanelHeap.style.display = DisplayStyle.None;
                return;
            }
        }
        catch (Exception ex)
        {
            if (ex is not RuntimeException) Debug.LogException(ex);
            ui.PanelHeap.style.display = DisplayStyle.None;
            return;
        }

        ui.HeapObjectsContainer.Clear();

        int totalMemory = blocks.Sum(v => v.Size);
        if (totalMemory == 0)
        {
            ui.PanelHeap.style.display = DisplayStyle.None;
            return;
        }

        ui.PanelHeap.style.display = DisplayStyle.Flex;

        foreach (HeapUtils.HeapBlock block in blocks)
        {
            VisualElement e = new();
            ui.HeapObjectsContainer.Add(e);

            Label l = new();
            e.Add(l);

            e.AddToClassList("heap-block");
            e.AddToClassList(block.IsUsed ? "heap-block-used" : "heap-block-free");
            e.style.flexBasis = new StyleLength(new Length(block.Size * 100f / totalMemory, LengthUnit.Percent));

            l.text = $"{block.Size} bytes";
        }

        Tooltips.Instance.Reregister(ui.HeapObjectsContainer);
    }

    static readonly string[] ProgressStatusClasses = new string[]
    {
        "error",
        "warning",
        "success",
    };

    static bool ReadKey(out char c)
    {
        c = default;
        if (Keyboard.current.digit0Key.wasPressedThisFrame) c = '0';
        else if (Keyboard.current.digit1Key.wasPressedThisFrame) c = '1';
        else if (Keyboard.current.digit2Key.wasPressedThisFrame) c = '2';
        else if (Keyboard.current.digit3Key.wasPressedThisFrame) c = '3';
        else if (Keyboard.current.digit4Key.wasPressedThisFrame) c = '4';
        else if (Keyboard.current.digit5Key.wasPressedThisFrame) c = '5';
        else if (Keyboard.current.digit6Key.wasPressedThisFrame) c = '6';
        else if (Keyboard.current.digit7Key.wasPressedThisFrame) c = '7';
        else if (Keyboard.current.digit8Key.wasPressedThisFrame) c = '8';
        else if (Keyboard.current.digit9Key.wasPressedThisFrame) c = '9';
        else if (Keyboard.current.aKey.wasPressedThisFrame) c = 'a';
        else if (Keyboard.current.bKey.wasPressedThisFrame) c = 'b';
        else if (Keyboard.current.cKey.wasPressedThisFrame) c = 'c';
        else if (Keyboard.current.dKey.wasPressedThisFrame) c = 'd';
        else if (Keyboard.current.eKey.wasPressedThisFrame) c = 'e';
        else if (Keyboard.current.fKey.wasPressedThisFrame) c = 'f';
        else if (Keyboard.current.gKey.wasPressedThisFrame) c = 'g';
        else if (Keyboard.current.hKey.wasPressedThisFrame) c = 'h';
        else if (Keyboard.current.iKey.wasPressedThisFrame) c = 'i';
        else if (Keyboard.current.jKey.wasPressedThisFrame) c = 'j';
        else if (Keyboard.current.kKey.wasPressedThisFrame) c = 'k';
        else if (Keyboard.current.lKey.wasPressedThisFrame) c = 'l';
        else if (Keyboard.current.mKey.wasPressedThisFrame) c = 'm';
        else if (Keyboard.current.nKey.wasPressedThisFrame) c = 'n';
        else if (Keyboard.current.oKey.wasPressedThisFrame) c = 'o';
        else if (Keyboard.current.pKey.wasPressedThisFrame) c = 'p';
        else if (Keyboard.current.qKey.wasPressedThisFrame) c = 'q';
        else if (Keyboard.current.rKey.wasPressedThisFrame) c = 'r';
        else if (Keyboard.current.sKey.wasPressedThisFrame) c = 's';
        else if (Keyboard.current.tKey.wasPressedThisFrame) c = 't';
        else if (Keyboard.current.uKey.wasPressedThisFrame) c = 'u';
        else if (Keyboard.current.vKey.wasPressedThisFrame) c = 'v';
        else if (Keyboard.current.wKey.wasPressedThisFrame) c = 'w';
        else if (Keyboard.current.xKey.wasPressedThisFrame) c = 'x';
        else if (Keyboard.current.yKey.wasPressedThisFrame) c = 'y';
        else if (Keyboard.current.zKey.wasPressedThisFrame) c = 'z';
        else if (Keyboard.current.spaceKey.wasPressedThisFrame) c = ' ';
        else if (Keyboard.current.enterKey.wasPressedThisFrame) c = '\r';
        else if (Keyboard.current.commaKey.wasPressedThisFrame) c = ',';
        else if (Keyboard.current.periodKey.wasPressedThisFrame) c = '.';
        else if (Keyboard.current.minusKey.wasPressedThisFrame) c = '-';
        else return false;
        return true;
    }

    static unsafe ProcessorState MakeProcessorState(ref Processor processor, Span<byte> memory) => new(
        ProcessorSystemServer.BytecodeInterpreterSettings,
        processor.Registers,
        memory.IsEmpty ? new Span<byte>(Unsafe.AsPointer(ref processor.Memory.Memory), 2048) : memory,
        processor.Source.Code.AsSpan(),
        processor.Source.GeneratedFunctions.AsSpan()
    )
    {
        Crash = processor.Crash,
        HotFunctions = processor.HotFunctions,
        Registers = processor.Registers,
        Signal = processor.Signal,
    };

    static bool TryGetRuntimeException(ref Processor processor, Span<byte> memory, [NotNullWhen(true)] out RuntimeException? runtimeException)
    {
        runtimeException = MakeProcessorState(ref processor, memory).GetRuntimeException();

        if (runtimeException is null) return false;

        if (processor.IsVersionLocked) return true;

        if (!ConnectionManager.ServerOrDefaultWorld.IsServer() && !ConnectionManager.ServerOrDefaultWorld.Unmanaged.IsLocal())
        {
            return true;
        }

        CompilerSystemServer compilerSystem = ConnectionManager.ServerOrDefaultWorld.GetExistingSystemManaged<CompilerSystemServer>();
        if (compilerSystem.CompiledSources.TryGetValue(processor.SourceFile, out CompiledSourceServer? source))
        {
            runtimeException.DebugInformation = source.DebugInformation;
        }

        return true;
    }

    bool TryDownloadMemory()
    {
        if (ConnectionManager.ClientOrDefaultWorld.Unmanaged.IsLocal()) return false;
        if (_memoryDownloadTask is not null && !_memoryDownloadTask.GetAwaiter().IsCompleted) return false;

        GhostInstance ghostInstance = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(selectedUnitEntity);
        Debug.Log($"{DebugEx.ClientPrefix} Requesting memory for ghost {{ id: {ghostInstance.ghostId} spawnTick: {ghostInstance.spawnTick} ({ghostInstance.spawnTick.SerializedData}) }} ...");
        _memoryDownloadProgress = new ProgressRecord<(int, int)>(null);
        _memoryDownloadTask = FileChunkManagerSystem.GetInstance(ConnectionManager.ClientOrDefaultWorld)
            .RequestFileAsync(new FileId($"/i/e/{ghostInstance.ghostId}.{ghostInstance.spawnTick.SerializedData}/m", NetcodeEndPoint.Server), _memoryDownloadProgress);

        return true;
    }

    static bool TryGetSouce(in Processor processor, [NotNullWhen(true)] out ICompiledSource? source)
    {
        source = null;
        if (processor.IsVersionLocked) return false;

        if (ConnectionManager.ClientOrDefaultWorld.IsClient())
        {
            if (ConnectionManager.ClientOrDefaultWorld.GetExistingSystemManaged<CompilerSystemClient>().TryGetSource(processor.SourceFile, out CompiledSourceClient? clientSource, ConnectionManager.ClientOrDefaultWorld.Unmanaged))
            {
                source = clientSource;
                return true;
            }
        }
        else
        {
            if (ConnectionManager.ClientOrDefaultWorld.GetExistingSystemManaged<CompilerSystemServer>().CompiledSources.TryGetValue(processor.SourceFile, out CompiledSourceServer? serverSource))
            {
                source = serverSource;
                return true;
            }
        }

        return false;
    }

    public void RefreshUI(Entity unitEntity)
    {
        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        Processor processor = entityManager.GetComponentData<Processor>(unitEntity);

        ui.PanelDebug.style.display = processor.DebugContext.IsBeingDebugged ? DisplayStyle.Flex : DisplayStyle.None;
        ui.ButtonStartDebug.style.display = processor.DebugContext.IsBeingDebugged ? DisplayStyle.None : DisplayStyle.Flex;
        ui.ButtonStartDebug.SetEnabled(!processor.DebugContext.IsBeingDebugged);

        ui.ButtonLock.text = processor.IsVersionLocked ? "\uf023" : "\uf2fc";
        ui.ButtonLock.tooltip = processor.IsVersionLocked ? "Unlock Source" : "Lock Source";

        ui.ButtonHaltContinue.text = processor.Signal == Signal.Halt ? "\udb81\udc0a" : "\udb80\udfe4";

        ui.ButtonStartDebug.SetEnabled(!processor.IsVersionLocked);

        if (_memoryDownloadProgress is null && !processor.IsVersionLocked && Time.unscaledTime - _memoryLastCheck > 5f)
        {
            _memoryLastCheck = Time.unscaledTime;
            RefreshMemoryUsage(unitEntity);
        }

        if (!selectingFile.IsEmpty)
        {
            ui.LabelBasePath.text = FileChunkManagerSystem.BasePath;

            ui.ScrollFiles.SyncList<string, TerminalFileItemSchema>(selectingFile, FileItem, (file, element, recycled) =>
            {
                element.Root.userData = file;
                element.ButtonSelect.text = file;
                if (!recycled)
                {
                    element.ButtonSelect.clicked += () =>
                    {
                        ui.InputSourcePath.value = (string)element.Root.userData;
                        selectingFile = ImmutableArray<string>.Empty;
                        EndFileSelection();
                    };
                }
            });

            if (Input.GetKeyDown(KeyCode.Q))
            {
                selectingFile = ImmutableArray<string>.Empty;
                EndFileSelection();
            }
            return;
        }

        bool isBottom = true; // ui.scrollTerminal.scrollOffset == ui.labelTerminal.layout.max - ui.scrollTerminal.contentViewport.layout.size;

        ReadOnlySpan<byte> stdout = ReadOnlySpan<byte>.Empty;

        if (ConnectionManager.ClientOrDefaultWorld.IsClient())
        {
            GhostInstance t = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(unitEntity);
            if (terminalSubscription == null)
            {
                Debug.Log($"{DebugEx.AnyPrefix} Terminal not subscribed, subscribing ...");
                terminalSubscription = ConnectionManager.ClientOrDefaultWorld.GetSystem<TerminalSystemClient>().Subscribe(unitEntity);
            }
            else if (!terminalSubscription.Ghost.Equals(t))
            {
                Debug.Log($"{DebugEx.AnyPrefix} Wrong terminal subscribed, unsubscribing ...");
                ConnectionManager.ClientOrDefaultWorld.GetSystem<TerminalSystemClient>().Unsubscribe(t);
                terminalSubscription = null;
            }
            else
            {
                stdout = terminalSubscription.Data.AsReadOnly().AsReadOnlySpan();
            }
        }
        else
        {
            unsafe
            {
                stdout = new ReadOnlySpan<byte>(processor.StdOutBuffer.GetUnsafePtr(), processor.StdOutBuffer.Length);
            }
        }

        _terminalBuilder.Clear();
        _terminalRenderer.Rerender(stdout, _terminalBuilder);

        void SetStatus(string icon, string label, string? className)
        {
            ui.IconStatus.text = icon;
            ui.LabelStatus.text = label;
            foreach (var v in ProgressStatusClasses)
            {
                if (v == className)
                {
                    ui.IconStatus.EnableInClassList(v, true);
                    ui.LabelStatus.EnableInClassList(v, true);
                }
                else
                {
                    ui.IconStatus.EnableInClassList(v, false);
                    ui.LabelStatus.EnableInClassList(v, false);
                }
            }
        }

        if (processor.SourceFile == default)
        {
            if (_scheduledSource != null)
            {
                SetStatus("\udb86\udd78", "Scheduled ...", null);
            }
            else
            {
                SetStatus("\udb84\udcd3", "No source", null);
            }
            ui.ScrollProgresses.Clear();
            ui.ScrollDiagnostics.style.display = DisplayStyle.None;
        }
        else
        {
            TryGetSouce(in processor, out ICompiledSource? source);

            _scheduledSource = null;
            const string SpinnerChars = "-\\|/";
            char spinner = SpinnerChars[(int)(MonoTime.Now * 8f) % SpinnerChars.Length];

            void SyncDiagnosticItems(VisualElement container, IEnumerable<Diagnostic> diagnostics, DiagnosticsLevel parentLevel)
            {
                container.SyncList<Diagnostic, TerminalDiagnosticsItemSchema>(
                    diagnostics
                        .Where(v => v.Level is not DiagnosticsLevel.OptimizationNotice and not DiagnosticsLevel.FailedOptimization)
                        .ToArray(),
                    DiagnosticsItem,
                    (item, element, recycled) =>
                    {
                        element.Root.userData = item;
                        DiagnosticsLevel fixedLevel = item.Level > parentLevel ? item.Level : parentLevel;

                        element.DiagnosticIcon.style.backgroundImage = fixedLevel switch
                        {
                            DiagnosticsLevel.Error => new StyleBackground(DiagnosticsErrorIcon),
                            DiagnosticsLevel.Warning => new StyleBackground(DiagnosticsWarningIcon),
                            DiagnosticsLevel.Information => new StyleBackground(DiagnosticsInfoIcon),
                            DiagnosticsLevel.Hint => new StyleBackground(DiagnosticsHintIcon),
                            DiagnosticsLevel.OptimizationNotice => new StyleBackground(DiagnosticsOptimizationNoticeIcon),
                            DiagnosticsLevel.FailedOptimization => new StyleBackground(DiagnosticsWarningIcon),
                            _ => new StyleBackground(DiagnosticsInfoIcon),
                        };

                        element.DiagnosticLabel.text = item.Message;

                        if (item is DiagnosticAt diagnosticAt)
                        {
                            string? localPath = null;
                            bool isExternal = diagnosticAt.File.Scheme == "netcode";
                            if (diagnosticAt.File.Scheme == "netcode" && diagnosticAt.File.Host == _localConnectionId.ToString())
                            {
                                isExternal = false;
                                localPath = FileChunkManagerSystem.ResolveFile(diagnosticAt.File.PathAndQuery);
                                //element.DiagnosticButtonVscode.style.display = DisplayStyle.Flex;
                                //var b = element.DiagnosticButtonVscode;
                                //element.DiagnosticButtonVscode.clickable = new Clickable(() =>
                                //{
                                //    Application.OpenURL($"vscode://file/{HttpUtility.UrlPathEncode(localPath)}:{diagnosticAt.Position.Range.Start.ToStringMin()}");
                                //});
                            }
                            element.DiagnosticLocationRemote.style.display = isExternal ? DisplayStyle.Flex : DisplayStyle.None;
                            element.DiagnosticLocation.style.display = DisplayStyle.Flex;
                            element.DiagnosticLocation.text = $"{(localPath is null ? null : localPath.StartsWith(FileChunkManagerSystem.BasePath) ? Path.GetRelativePath(FileChunkManagerSystem.BasePath, localPath) : localPath) ?? diagnosticAt.File.ToString()}:{diagnosticAt.Position.Range.Start.ToStringMin()}";
                        }
                        else
                        {
                            element.DiagnosticLocationRemote.style.display = DisplayStyle.None;
                            element.DiagnosticLocation.style.display = DisplayStyle.None;
                            element.DiagnosticLocation.text = string.Empty;
                        }

                        if (item.SubErrors.Any())
                        {
                            SyncDiagnosticItems(element.DiagnosticFoldout, item.SubErrors, fixedLevel);
                        }
                        else
                        {
                            element.DiagnosticFoldout.style.display = DisplayStyle.None;
                        }
                    });
            }

            if (source != null && source.Status == CompilationStatus.Done)
            {
                SyncDiagnosticItems(ui.ScrollDiagnostics, source.Diagnostics, default);

                ui.ScrollDiagnostics.style.display = selectingFile.IsDefaultOrEmpty && source.Diagnostics.Any(v => v.Level == DiagnosticsLevel.Error) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                ui.ScrollDiagnostics.Clear();

                if (processor.IsVersionLocked)
                {
                    VisualElement element = DiagnosticsItem.Instantiate();
                    ui.ScrollDiagnostics.Add(element);
                    TerminalDiagnosticsItemSchema schema = new();
                    schema.Q(element);
                    schema.DiagnosticIcon.style.backgroundImage = new StyleBackground(DiagnosticsInfoIcon);
                    schema.DiagnosticLabel.text = "Diagnostics not available on locked source";
                    schema.DiagnosticFoldout.style.display = DisplayStyle.None;
                    schema.DiagnosticLocationRemote.style.display = DisplayStyle.None;
                    schema.DiagnosticLocation.style.display = DisplayStyle.None;
                    schema.DiagnosticLocation.text = string.Empty;
                }
            }

            if (source != null && source.Status != CompilationStatus.Done && !float.IsNaN(source.Progress))
            {
                ui.ScrollProgresses.SyncList(source.SubFiles.ToArray(), ProgressItem, (file, element, recycled) =>
                {
                    ProgressBar progressBar = element.Q<ProgressBar>();
                    progressBar.title = file.Key.Name.ToString();
                    progressBar.value = file.Value.Progress.Total == 0 ? 0f : (float)file.Value.Progress.Current / (float)file.Value.Progress.Total;
                });
            }
            else
            {
                ui.ScrollProgresses.Clear();
            }

            switch (source == null ? CompilationStatus.None : source.Status)
            {
                case CompilationStatus.Secuedued:
                {
                    SetStatus("\udb86\udd78", "Secuedued ...", null);
                    break;
                }
                case CompilationStatus.Compiling:
                {
                    SetStatus("\udb86\udd78", "Compiling ...", null);
                    break;
                }
                case CompilationStatus.Uploading:
                {
                    SetStatus("\udb86\udd78", "Uploading ...", null);
                    break;
                }
                case CompilationStatus.Generating:
                {
                    SetStatus("\udb86\udd78", "Generating ...", null);
                    break;
                }
                case CompilationStatus.Generated:
                {
                    SetStatus("\udb86\udd78", "Loading ...", null);
                    break;
                }
                case CompilationStatus.Done:
                case CompilationStatus.None:
                {
                    if (!processor.IsVersionLocked && source is null)
                    {
                        SetStatus("\udb80\udeb1", "Remote source ...", null);
                        break;
                    }

                    if (source is not null && !source.IsSuccess)
                    {
                        SetStatus("\udb80\udd59", "Compile failed ...", "error");
                        break;
                    }

                    if (ui.LabelTerminal.visible && ui.LabelTerminal.panel.focusController.focusedElement == ui.LabelTerminal)
                    {
                        foreach (byte c in Encoding.UTF8.GetBytes(Input.inputString))
                        {
                            StandardInput.Enqueue(c);
                        }
                    }

                    bool isErrored = false;
                    switch (processor.Signal)
                    {
                        case Signal.None:
                            SetStatus("\udb81\udf65", "Running", "success");
                            _memory = null;
                            _memoryDownloadProgress = null;
                            // try { _memoryDownloadTask?.Cancel(); } catch { }
                            _memoryDownloadTask = null;

                            if (StandardInput.TryDequeue(out byte c))
                            {
                                NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new ProcessorCommandRequestRpc()
                                {
                                    Entity = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(unitEntity),
                                    Command = ProcessorCommand.Key,
                                    Data = c,
                                });
                                _terminalCursorBlinkRestart = Time.time;
                            }

                            const float BlinkInterval = 1f;
                            if (ui.LabelTerminal.panel.focusController.focusedElement == ui.LabelTerminal && (Time.time - _terminalCursorBlinkRestart) % BlinkInterval < BlinkInterval * 0.5f)
                            {
                                _terminalBuilder.Append("<mark=#ffffffff>_</mark>");
                            }
                            else
                            {
                                _terminalBuilder.Append("<color=black>_</color>");
                            }
                            break;
                        case Signal.UserCrash:
                            SetStatus("\udb80\udc28", "User-crashed", "error");
                            isErrored = true;
                            if (_memory is null)
                            {
                                if (ConnectionManager.ClientOrDefaultWorld.Unmanaged.IsLocal())
                                {
                                    _memory = processor.Memory.Memory.ToBytes();
                                }
                                else
                                {
                                    _memoryDownloadProgress ??= new ProgressRecord<(int, int)>(null);

                                    if (_memoryDownloadTask is null)
                                    {
                                        if (!TryDownloadMemory())
                                        {
                                            Debug.LogError($"{DebugEx.ClientPrefix} Cannot download memory");
                                        }
                                    }

                                    if (_memoryDownloadTask is not null)
                                    {
                                        Awaitable<RemoteFile>.Awaiter awaiter = _memoryDownloadTask.GetAwaiter();
                                        if (awaiter.IsCompleted)
                                        {
                                            Debug.Log($"{DebugEx.ClientPrefix} Memory loaded");
                                            RemoteFile result = awaiter.GetResult();
                                            switch (result.Status)
                                            {
                                                case FileResponseStatus.NotFound:
                                                case FileResponseStatus.Unknown:
                                                case FileResponseStatus.HoldOn:
                                                case FileResponseStatus.ErrorDisconnected:
                                                case FileResponseStatus.ErrorInvalidTransaction:
                                                    break;
                                                case FileResponseStatus.OK:
                                                case FileResponseStatus.NotChanged:
                                                default:
                                                    _memory = result.Data.Data;
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            SetStatus("\udb80\udc28", "User-crashed (loading memory)", "error");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                string? message = HeapUtils.GetString(_memory, processor.Crash);
                                _terminalBuilder.AppendLine();
                                _terminalBuilder.Append("<color=red>");
                                _terminalBuilder.Append(message ?? "null");
                                _terminalBuilder.Append("</color>");
                                _terminalBuilder.AppendLine();
                            }
                            break;
                        case Signal.StackOverflow:
                            SetStatus("\udb80\udc28", "Crashed", "error");
                            isErrored = true;
                            break;
                        case Signal.Halt:
                            SetStatus("\udb80\udfe5", "Halted", "warning");
                            break;
                        case Signal.UndefinedExternalFunction:
                            SetStatus("\udb80\udc28", "Crashed", "error");
                            isErrored = true;
                            break;
                        case Signal.PointerOutOfRange:
                            SetStatus("\udb80\udc28", "Crashed", "error");
                            isErrored = true;
                            break;
                        default:
                            throw new UnreachableException();
                    }

                    if (isErrored)
                    {
                        _terminalBuilder.AppendLine();
                        _terminalBuilder.Append("<color=red>");
                        if (TryGetRuntimeException(ref processor, _memory, out RuntimeException? error))
                        {
                            _terminalBuilder.Append(error);
                        }
                        else
                        {
                            _terminalBuilder.Append(ProcessorState.GetSimpleRuntimeErrorMessage(processor.Signal, processor.Crash));
                        }
                        _terminalBuilder.AppendLine("</color>");
                    }
                    break;
                }
                default: throw new UnreachableException();
            }
        }

        if (ui.LabelTerminal.visible)
        {
            ui.LabelTerminal.text = _terminalBuilder.ToString();

            if (isBottom)
            {
                ui.ScrollTerminal.scrollOffset = ui.LabelTerminal.layout.max - ui.ScrollTerminal.contentViewport.layout.size;
            }
        }

        if (ui.ScrollLogs.visible && false)
        {
            if (_unitLog is null)
            {
                if (ConnectionManager.ClientOrDefaultWorld.EntityManager.HasComponent<GhostInstance>(unitEntity))
                {
                    GhostInstance ghost = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(unitEntity);
                    _unitLog = ConnectionManager.ClientOrDefaultWorld.GetSystem<UnitLogSystemClient>().GetUnitLog(ghost);
                }
                else
                {
                    return;
                }
            }

            _unitLog.WindowStart = MonoTime.UnixSeconds;
            _unitLog.WindowLength = 20;

            List<(long Timestamp, object Item)> _logs = new();

            static int ItemComparison((long Timestamp, object Item) a, (long Timestamp, object Item) b) => (int)(a.Timestamp - b.Timestamp);

            foreach ((_, _, byte[] logData) in _unitLog.Data)
            {
                ReadOnlySpan<byte> buffer = logData.AsSpan();

                int i = 0;
                while (i < buffer.Length)
                {
                    LogPieceType type = (LogPieceType)buffer[i];
                    switch (type)
                    {
                        case LogPieceType.Message: { var v = UnitLog_Message.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        case LogPieceType.CombatTurret_Shoot: { var v = UnitLog_CombatTurret_Shoot.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        case LogPieceType.Command: { var v = UnitLog_Command.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        case LogPieceType.Radar: { var v = UnitLog_Radar.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        case LogPieceType.Transmission_WiredOut: { var v = UnitLog_Transmission_WiredOut.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        case LogPieceType.Transmission_WiredIn: { var v = UnitLog_Transmission_WiredIn.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        case LogPieceType.Transmission_WirelessOut: { var v = UnitLog_Transmission_WirelessOut.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        case LogPieceType.Transmission_WirelessIn: { var v = UnitLog_Transmission_WirelessIn.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        case LogPieceType.ProcessorSignal: { var v = UnitLog_ProcessorSignal.Read(buffer, ref i); _logs.AddSorted((v.Header.Timestamp, v), ItemComparison); break; }
                        default: Debug.LogError($"Invalid log piece type {(int)type}"); goto skip;
                    }
                }
            }

        skip:;

            ui.ScrollLogs.SyncList<(long Timestamp, object Item), TerminalLogItemSchema>(_logs, LogItem, (item, element, _) =>
            {
                LogPieceHeader header = default;
                switch (item.Item)
                {
                    case UnitLog_Message v:
                        header = v.Header;
                        element.LogSummaryLabel.text = $"Message";
                        element.LogDetailsFoldout.style.display = DisplayStyle.None;
                        element.LogIcon.style.display = DisplayStyle.None;
                        break;
                    case UnitLog_CombatTurret_Shoot v:
                        header = v.Header;
                        element.LogSummaryLabel.text = $"Turret Shoot";
                        element.LogDetailsFoldout.style.display = DisplayStyle.None;
                        element.LogIcon.text = $"\uf05b";
                        break;
                    case UnitLog_Command v:
                        header = v.Header;
                        element.LogSummaryLabel.text = $"Command";
                        element.LogIcon.text = $"\uf007";
                        break;
                    case UnitLog_Radar v:
                        header = v.Header;
                        element.LogIcon.text = $"\uf7c0";
                        if (v.Success)
                        {
                            element.LogSummaryLabel.text = $"Radar (DETECTED)";
                            element.LogDetailsLabel.text =
                                $"Point: {v.RadarResponse.Point}\n" +
                                $"Clutter: {v.RadarResponse.Clutter}\n" +
                                $"Fingerprint: {v.RadarResponse.Fingerprint}\n" +
                                $"Meta: {v.RadarResponse.Meta}\n" +
                                $"SpeedSignal: {v.RadarResponse.SpeedSignal}\n";
                        }
                        else
                        {
                            element.LogSummaryLabel.text = $"Radar (none)";
                            element.LogDetailsFoldout.style.display = DisplayStyle.None;
                        }
                        break;
                    case UnitLog_Transmission_WiredOut v:
                        header = v.Header;
                        element.LogIcon.text = $"\uf796";
                        element.LogSummaryLabel.text = $"Transmission Out (Wired)";
                        element.LogDetailsLabel.text =
                            $"Port: {v.Metadata.Port}\n" +
                            $"Data: {string.Join(" ", v.Data.ToArray().Select(v => Convert.ToString(v, 16).PadLeft(2, '0')))}";
                        break;
                    case UnitLog_Transmission_WiredIn v:
                        header = v.Header;
                        element.LogIcon.text = $"\uf796";
                        element.LogSummaryLabel.text = $"Transmission In (Wired)";
                        element.LogDetailsLabel.text =
                            $"Port: {v.Metadata.Port}\n" +
                            $"Data: {string.Join(" ", v.Data.ToArray().Select(v => Convert.ToString(v, 16).PadLeft(2, '0')))}";
                        break;
                    case UnitLog_Transmission_WirelessOut v:
                        header = v.Header;
                        element.LogIcon.text = $"\uf519";
                        element.LogSummaryLabel.text = $"Transmission Out (Wireless)";
                        element.LogDetailsLabel.text =
                            $"Angle: {v.Metadata.Angle}\n" +
                            $"Direction: {v.Metadata.Direction}\n" +
                            $"Data: {string.Join(" ", v.Data.ToArray().Select(v => Convert.ToString(v, 16).PadLeft(2, '0')))}";
                        break;
                    case UnitLog_Transmission_WirelessIn v:
                        header = v.Header;
                        element.LogIcon.text = $"\uf519";
                        element.LogSummaryLabel.text = $"Transmission In (Wireless)";
                        element.LogDetailsLabel.text =
                            $"Data: {string.Join(" ", v.Data.ToArray().Select(v => Convert.ToString(v, 16).PadLeft(2, '0')))}";
                        break;
                    case UnitLog_ProcessorSignal v:
                        header = v.Header;
                        element.LogIcon.text = v.Signal switch
                        {
                            Signal.None => $"\uf2db",
                            Signal.UserCrash => $"<color=red>\uf06a</color>",
                            Signal.StackOverflow => $"<color=red>\uf06a</color>",
                            Signal.Halt => $"\uf04c",
                            Signal.UndefinedExternalFunction => $"<color=red>\uf06a</color>",
                            Signal.PointerOutOfRange => $"<color=red>\uf06a</color>",
                            _ => $"<color=red>\uf06a</color>",
                        };
                        element.LogSummaryLabel.text = $"Processor: {v.Signal switch
                        {
                            Signal.None => $"Signal: None",
                            Signal.UserCrash => $"Signal: UserCrash",
                            Signal.StackOverflow => $"Signal: StackOverflow",
                            Signal.Halt => $"Signal: Halt",
                            Signal.UndefinedExternalFunction => $"Signal: UndefinedExternalFunction",
                            Signal.PointerOutOfRange => $"Signal: PointerOutOfRange",
                            _ => $"Signal: {(int)v.Signal}",
                        }}";
                        element.LogDetailsLabel.style.display = DisplayStyle.None;
                        break;
                    default:
                        element.LogSummaryLabel.text = item.GetType().Name;
                        element.LogDetailsFoldout.style.display = DisplayStyle.None;
                        break;
                }

                if (header.Timestamp != default)
                {
                    try
                    {
                        element.LogTimeLabel.text = DateTimeOffset.FromUnixTimeSeconds(header.Timestamp).ToString("yyyy-MM-dd HH:mm:ss.ff");
                    }
                    catch (Exception)
                    {
                        element.LogTimeLabel.text = header.Timestamp.ToString();
                    }
                }
                else
                {
                    element.LogTimeLabel.style.display = DisplayStyle.None;
                }
            });
        }
    }

    public void Cleanup(UIElementReference _ui)
    {
        selectedUnitEntity = Entity.Null;
        refreshAt = float.PositiveInfinity;
        selectingFile = ImmutableArray<string>.Empty;
        _memory = null;
        _unitLog?.Dispose();
        _unitLog = null;
        _memoryDownloadProgress = null;
        // try { _memoryDownloadTask?.Cancel(); } catch { }
        _memoryDownloadTask = null;
        _scheduledSource = null;
        _memoryLastCheck = 0f;
        if (terminalSubscription != null && ConnectionManager.ClientOrDefaultWorld.IsClient())
        {
            ConnectionManager.ClientOrDefaultWorld.GetSystem<TerminalSystemClient>().Unsubscribe(terminalSubscription.Ghost);
            terminalSubscription = null;
        }

        ui.ButtonSelect.clickable = null;
        ui.ButtonCompile.clickable = null;
        ui.ButtonHotreload.clickable = null;
        ui.ButtonHaltContinue.clickable = null;
        ui.ButtonHaltContinue.text = "\udb80\udfe4";
        ui.ButtonReset.clickable = null;
        ui.ButtonStopDebug.clickable = null;
        ui.ButtonStartDebug.clickable = null;
        ui.LabelTerminal.text = string.Empty;
        EndFileSelection();
    }
}
