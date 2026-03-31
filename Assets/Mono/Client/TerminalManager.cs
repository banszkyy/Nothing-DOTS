using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
    readonly List<List<TerminalCharacter>> compiled;
    int cursorX;
    int cursorY;

    public TerminalRenderer()
    {
        compiledBg = TerminalColor.Black;
        compiledFg = TerminalColor.White;
        compiledMod = TerminalMode.None;
        compiled = new() { new() };
        cursorX = 0;
        cursorY = 0;
    }

    public void Reset()
    {
        compiledBg = TerminalColor.Black;
        compiledFg = TerminalColor.White;
        compiledMod = TerminalMode.None;
        compiled.Clear();
        compiled.Add(new());
        cursorX = 0;
        cursorY = 0;
    }

    void Compile(ReadOnlySpan<byte> stdout)
    {
        for (int i = 0; i < stdout.Length; i++)
        {
            switch (stdout[i])
            {
                case (byte)'\b':
                    if (cursorX > 0) compiled[cursorY].RemoveAt(--cursorX);
                    break;
                case (byte)'\n':
                    cursorY++;
                    cursorX = 0;
                    if (cursorY >= compiled.Count) compiled.Add(new());
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
                                compiled.Clear();
                                compiled.Add(new());
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
                    if (cursorX + 1 == compiled[cursorY].Count)
                    {
                        compiled[cursorY].Add(new((char)stdout[i], compiledMod, (TerminalColor)((byte)compiledBg << 4) | compiledFg));
                        cursorX++;
                    }
                    else
                    {
                        compiled[cursorY].Insert(cursorX++, new((char)stdout[i], compiledMod, (TerminalColor)((byte)compiledBg << 4) | compiledFg));
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

        int start = (maxLines == -1) ? 0 : Math.Max(0, compiled.Count - maxLines);
        for (int i = start; i < compiled.Count; i++)
        {
            foreach (TerminalCharacter c in compiled[i])
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
                    builder.Append($"<color={c.Foreground switch
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
                    }}>");
                    appliedFg = c.Foreground;
                }

                if (appliedBg != c.Foreground)
                {
                    builder.Append($"<mark={c.Background switch
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
                    }}>");
                    appliedBg = c.Background;
                }

                builder.Append(c.Character);
            }
            builder.AppendLine();
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
    float refreshAt = default;
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
    [SerializeField, SaintsField.ReadOnly] UIDocument? ui = default;

    [NotNull] Button? ui_ButtonSelect = default;
    [NotNull] Button? ui_ButtonCompile = default;
    [NotNull] Button? ui_ButtonHotReload = default;
    [NotNull] Button? ui_ButtonHalt = default;
    [NotNull] Button? ui_ButtonReset = default;
    [NotNull] Button? ui_ButtonContinue = default;
    [NotNull] Label? ui_labelTerminal = default;

    [NotNull] ScrollView? ui_scrollTerminal = default;
    [NotNull] ScrollView? ui_scrollFiles = default;
    [NotNull] ScrollView? ui_scrollProgresses = default;
    [NotNull] ScrollView? ui_scrollDiagnostics = default;
    [NotNull] TabView? ui_tabView = default;

    [NotNull] TextField? ui_inputSourcePath = default;
    [NotNull] ProgressBar? ui_progressCompilation = default;

    TerminalSubscriptionClient? terminalSubscription;
    readonly StringBuilder _terminalBuilder = new();
    readonly TerminalRenderer _terminalRenderer = new();
    readonly Queue<char> StandardInput = new();
    byte[]? _memory;
    ProgressRecord<(int, int)>? _memoryDownloadProgress;
    Awaitable<RemoteFile>? _memoryDownloadTask;
    string? _scheduledSource;
    (Tab Condition, Tab Target) _requestedTabSwitch;
    Tab? _fulfilledTabSwitch;

    void Update()
    {
        if (ui == null || !ui.gameObject.activeSelf) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            return;
        }

        if (!_fulfilledTabSwitch.HasValue || _fulfilledTabSwitch.Value != _requestedTabSwitch.Target)
        {
            if ((int)_requestedTabSwitch.Condition == ui_tabView.selectedTabIndex)
            {
                ui_tabView.selectedTabIndex = (int)_requestedTabSwitch.Target;
            }
            _fulfilledTabSwitch = _requestedTabSwitch.Target;
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

    public void Setup(UIDocument ui, Entity unitEntity)
    {
        gameObject.SetActive(true);
        this.ui = ui;

        selectedUnitEntity = unitEntity;
        refreshAt = Time.time + .2f;
        selectingFile = ImmutableArray<string>.Empty;
        _memory = null;
        _memoryDownloadProgress = null;
        // try { _memoryDownloadTask?.Cancel(); } catch { }
        _memoryDownloadTask = null;
        _scheduledSource = null;

        ui_inputSourcePath = ui.rootVisualElement.Q<TextField>("input-source-path");
        ui_ButtonSelect = ui.rootVisualElement.Q<Button>("button-select");
        ui_ButtonCompile = ui.rootVisualElement.Q<Button>("button-compile");
        ui_ButtonHotReload = ui.rootVisualElement.Q<Button>("button-hotreload");
        ui_ButtonHalt = ui.rootVisualElement.Q<Button>("button-halt");
        ui_ButtonReset = ui.rootVisualElement.Q<Button>("button-reset");
        ui_ButtonContinue = ui.rootVisualElement.Q<Button>("button-continue");
        ui_labelTerminal = ui.rootVisualElement.Q<Label>("label-terminal");
        ui_scrollTerminal = ui.rootVisualElement.Q<ScrollView>("scroll-terminal");
        ui_scrollFiles = ui.rootVisualElement.Q<ScrollView>("scroll-files");
        ui_scrollProgresses = ui.rootVisualElement.Q<ScrollView>("scroll-progresses");
        ui_scrollDiagnostics = ui.rootVisualElement.Q<ScrollView>("scroll-diagnostics");
        ui_tabView = ui.rootVisualElement.Q<TabView>("tabs");
        ui_progressCompilation = ui.rootVisualElement.Q<ProgressBar>("progress-compilation");

        ui_labelTerminal.text = string.Empty;
        ui_scrollFiles.Clear();
        ui_scrollProgresses.Clear();
        ui_scrollDiagnostics.Clear();

        {
            EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
            Processor processor = entityManager.GetComponentData<Processor>(unitEntity);
            ui_inputSourcePath.value = processor.SourceFile.Name.ToString();
        }

        EndFileSelection();

        if (!string.IsNullOrWhiteSpace(FileChunkManagerSystem.BasePath))
        {
            ui_ButtonSelect.SetEnabled(true);
            ui_ButtonSelect.clickable = new Clickable(() =>
            {
                selectingFile = Directory.GetFiles(FileChunkManagerSystem.BasePath)
                    .Select(v => Path.GetRelativePath(FileChunkManagerSystem.BasePath, v))
                    .Where(v => !v.EndsWith(".meta"))
                    .ToImmutableArray();
                BeginFileSelection();
            });
        }
        else
        {
            ui_ButtonSelect.SetEnabled(false);
        }

        ui_ButtonCompile.clickable = new Clickable(() =>
        {
            World world = ConnectionManager.ClientOrDefaultWorld;

            if (world.IsServer())
            {
                Debug.LogError($"Not implemented");
                return;
            }

            string file =
                string.IsNullOrWhiteSpace(FileChunkManagerSystem.BasePath)
                ? ui_inputSourcePath.value
                : Path.Combine(FileChunkManagerSystem.BasePath, ui_inputSourcePath.value);

            NetcodeUtils.CreateRPC(world.Unmanaged, new SetProcessorSourceRequestRpc()
            {
                Source = ui_inputSourcePath.value,
                Entity = world.EntityManager.GetComponentData<GhostInstance>(unitEntity),
                IsHotReload = false,
            });

            _scheduledSource = ui_inputSourcePath.value;
        });

        ui_ButtonHotReload.clickable = new Clickable(() =>
        {
            World world = ConnectionManager.ClientOrDefaultWorld;

            if (world.IsServer())
            {
                Debug.LogError($"Not implemented");
                return;
            }

            string file =
                string.IsNullOrWhiteSpace(FileChunkManagerSystem.BasePath)
                ? ui_inputSourcePath.value
                : Path.Combine(FileChunkManagerSystem.BasePath, ui_inputSourcePath.value);

            NetcodeUtils.CreateRPC(world.Unmanaged, new SetProcessorSourceRequestRpc()
            {
                Source = ui_inputSourcePath.value,
                Entity = world.EntityManager.GetComponentData<GhostInstance>(unitEntity),
                IsHotReload = true,
            });

            _scheduledSource = ui_inputSourcePath.value;
        });

        ui_ButtonHalt.clickable = new Clickable(() =>
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
                Command = ProcessorCommand.Halt,
                Data = default,
            });
        });

        ui_ButtonReset.clickable = new Clickable(() =>
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

        ui_ButtonContinue.clickable = new Clickable(() =>
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
                Command = ProcessorCommand.Continue,
                Data = default,
            });
        });

        RefreshUI(unitEntity);
    }

    void BeginFileSelection()
    {
        ui_ButtonCompile.SetEnabled(false);
        ui_ButtonHotReload.SetEnabled(false);
        ui_ButtonHalt.SetEnabled(false);
        ui_ButtonReset.SetEnabled(false);
        ui_ButtonSelect.SetEnabled(false);
        ui_ButtonContinue.SetEnabled(false);
    }

    void EndFileSelection()
    {
        ui_ButtonCompile.SetEnabled(true);
        ui_ButtonHotReload.SetEnabled(true);
        ui_ButtonSelect.SetEnabled(true);
        ui_ButtonHalt.SetEnabled(true);
        ui_ButtonReset.SetEnabled(true);
        ui_ButtonContinue.SetEnabled(true);
        ui_scrollFiles.Clear();
    }

    static readonly string[] ProgressStatusClasses = new string[]
    {
        "error",
        "warning",
        "success",
    };

    void RefreshFileList()
    {
        ui_scrollFiles.SyncList(selectingFile, FileItem, (file, element, recycled) =>
        {
            element.userData = file;
            element.Q<Button>().text = file;
            if (!recycled)
            {
                element.Q<Button>().clicked += () =>
                {
                    ui_inputSourcePath.value = (string)element.userData;
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
    }

    enum Tab
    {
        Terminal,
        Diagnostics,
        Progress,
        Files,
    }

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

    public void RefreshUI(Entity unitEntity)
    {
        if (!selectingFile.IsEmpty)
        {
            RefreshFileList();
            return;
        }

        bool isBottom = true; // ui_scrollTerminal.scrollOffset == ui_labelTerminal.layout.max - ui_scrollTerminal.contentViewport.layout.size;

        void SetProgressStatus(string? status)
        {
            foreach (string item in ProgressStatusClasses)
            {
                ui_progressCompilation.EnableInClassList(item, item == status);
            }
        }

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        Processor processor = entityManager.GetComponentData<Processor>(unitEntity);
        ReadOnlySpan<byte> stdout = ReadOnlySpan<byte>.Empty;

        if (ConnectionManager.ClientOrDefaultWorld.IsClient())
        {
            GhostInstance t = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(unitEntity);
            if (terminalSubscription == null)
            {
                Debug.Log($"{DebugEx.AnyPrefix} Terminal not subscribed, subscribing ...");
                terminalSubscription = ConnectionManager.ClientOrDefaultWorld.GetSystem<TerminalSystemClient>().Subscribe(t);
            }
            else if (!terminalSubscription.Entity.Equals(t))
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

        if (processor.SourceFile == default)
        {
            if (_scheduledSource != null)
            {
                ui_progressCompilation.title = "Scheduled ...";
                ui_progressCompilation.value = 0f;
                SetProgressStatus(null);
            }
            else
            {
                ui_progressCompilation.title = "No source";
                ui_progressCompilation.value = 0f;
                SetProgressStatus(null);
            }
        }
        else
        {
            ICompiledSource? source = null;
            if (ConnectionManager.ClientOrDefaultWorld.IsClient())
            {
                if (ConnectionManager.ClientOrDefaultWorld.GetExistingSystemManaged<CompilerSystemClient>().TryGetSource(processor.SourceFile, out CompiledSourceClient? clientSource, ConnectionManager.ClientOrDefaultWorld.Unmanaged))
                {
                    source = clientSource;
                }
            }
            else
            {
                if (ConnectionManager.ClientOrDefaultWorld.GetExistingSystemManaged<CompilerSystemServer>().CompiledSources.TryGetValue(processor.SourceFile, out CompiledSourceServer? serverSource))
                {
                    source = serverSource;
                }
            }

            _scheduledSource = null;
            const string SpinnerChars = "-\\|/";
            char spinner = SpinnerChars[(int)(MonoTime.Now * 8f) % SpinnerChars.Length];

            void SyncDiagnosticItems(VisualElement container, IEnumerable<Diagnostic> diagnostics, DiagnosticsLevel parentLevel)
            {
                container.SyncList(
                    diagnostics
                        .Where(v => v.Level is not DiagnosticsLevel.OptimizationNotice and not DiagnosticsLevel.FailedOptimization)
                        .ToArray(),
                    DiagnosticsItem,
                    (item, element, recycled) =>
                    {
                        element.userData = item;
                        VisualElement icon = element.Q<VisualElement>("diagnostic-icon");
                        Label label = element.Q<Label>("diagnostic-label");
                        Foldout foldout = element.Q<Foldout>("diagnostic-foldout");
                        DiagnosticsLevel fixedLevel = item.Level > parentLevel ? item.Level : parentLevel;

                        icon.style.backgroundImage = fixedLevel switch
                        {
                            DiagnosticsLevel.Error => new StyleBackground(DiagnosticsErrorIcon),
                            DiagnosticsLevel.Warning => new StyleBackground(DiagnosticsWarningIcon),
                            DiagnosticsLevel.Information => new StyleBackground(DiagnosticsInfoIcon),
                            DiagnosticsLevel.Hint => new StyleBackground(DiagnosticsHintIcon),
                            DiagnosticsLevel.OptimizationNotice => new StyleBackground(DiagnosticsOptimizationNoticeIcon),
                            DiagnosticsLevel.FailedOptimization => new StyleBackground(DiagnosticsWarningIcon),
                            _ => new StyleBackground(DiagnosticsInfoIcon),
                        };

                        if (item is DiagnosticAt diagnosticAt)
                        {
                            label.text = $"{item.Message} <color=#888>{diagnosticAt.File}:{diagnosticAt.Position.Range.Start.ToStringMin()}</color>";
                        }
                        else
                        {
                            label.text = item.Message;
                        }

                        if (item.SubErrors.Any())
                        {
                            SyncDiagnosticItems(foldout, item.SubErrors, fixedLevel);
                        }
                        else
                        {
                            foldout.style.display = DisplayStyle.None;
                        }
                    });
            }

            if (source != null)
            {
                SyncDiagnosticItems(ui_scrollDiagnostics, source.Diagnostics, default);
                if (source.Status != CompilationStatus.Done && !float.IsNaN(source.Progress))
                {
                    ui_scrollProgresses.SyncList(source.SubFiles.ToArray(), ProgressItem, (file, element, recycled) =>
                    {
                        ProgressBar progressBar = element.Q<ProgressBar>();
                        progressBar.title = file.Key.Name.ToString();
                        if (file.Value.Progress.Total == 0)
                        {
                            progressBar.value = 0f;
                        }
                        else
                        {
                            progressBar.value = (float)file.Value.Progress.Current / (float)file.Value.Progress.Total;
                        }
                    });

                    ui_progressCompilation.value = source.Progress;

                    _requestedTabSwitch = (Tab.Files, Tab.Progress);
                }
            }

            switch (source == null ? CompilationStatus.None : source.Status)
            {
                case CompilationStatus.Secuedued:
                {
                    ui_progressCompilation.title = "Secuedued ...";
                    ui_progressCompilation.value = 0f;
                    SetProgressStatus(null);
                    break;
                }
                case CompilationStatus.Compiling:
                {
                    ui_progressCompilation.title = "Compiling ...";
                    ui_progressCompilation.value = 1f;
                    SetProgressStatus(null);
                    break;
                }
                case CompilationStatus.Uploading:
                {
                    ui_progressCompilation.title = "Uploading ...";
                    SetProgressStatus(null);
                    break;
                }
                case CompilationStatus.Generating:
                {
                    ui_progressCompilation.title = "Generating ...";
                    ui_progressCompilation.value = 1f;
                    SetProgressStatus(null);
                    break;
                }
                case CompilationStatus.Generated:
                {
                    ui_progressCompilation.title = "Generated";
                    ui_progressCompilation.value = 1f;
                    SetProgressStatus(null);
                    break;
                }
                case CompilationStatus.Done:
                case CompilationStatus.None:
                {
                    if (source != null && source.IsSuccess)
                    {
                        _requestedTabSwitch = (Tab.Progress, Tab.Terminal);

                        if (ui_tabView.selectedTabIndex == (int)Tab.Terminal)
                        {
                            foreach (char c in Input.inputString)
                            {
                                StandardInput.Enqueue(c);
                            }
                        }

                        bool isErrored = false;
                        switch (processor.Signal)
                        {
                            case Signal.None:
                                ui_progressCompilation.title = "Running";
                                ui_progressCompilation.value = 1f;
                                SetProgressStatus("success");
                                _memory = null;
                                _memoryDownloadProgress = null;
                                // try { _memoryDownloadTask?.Cancel(); } catch { }
                                _memoryDownloadTask = null;

                                if (StandardInput.TryDequeue(out char c))
                                {
                                    NetcodeUtils.CreateRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new ProcessorCommandRequestRpc()
                                    {
                                        Entity = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(unitEntity),
                                        Command = ProcessorCommand.Key,
                                        Data = c,
                                    });
                                }
                                else if (ui_labelTerminal.panel.focusController.focusedElement == ui_labelTerminal && Time.time % 1f < .5f)
                                {
                                    _terminalBuilder.Append("<mark=#ffffffff>_</mark>");
                                }
                                else
                                {
                                    _terminalBuilder.Append("<color=black>_</color>");
                                }
                                break;
                            case Signal.UserCrash:
                                ui_progressCompilation.title = "User-crashed";
                                ui_progressCompilation.value = 1f;
                                SetProgressStatus("error");
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

                                        if (_memoryDownloadTask == null)
                                        {
                                            GhostInstance ghostInstance = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<GhostInstance>(selectedUnitEntity);
                                            Debug.Log($"{DebugEx.ClientPrefix} Requesting memory for ghost {{ id: {ghostInstance.ghostId} spawnTick: {ghostInstance.spawnTick} ({ghostInstance.spawnTick.SerializedData}) }} ...");
                                            _memoryDownloadTask = FileChunkManagerSystem.GetInstance(ConnectionManager.ClientOrDefaultWorld)
                                                .RequestFile(new FileId($"/i/e/{ghostInstance.ghostId}.{ghostInstance.spawnTick.SerializedData}/m", NetcodeEndPoint.Server), _memoryDownloadProgress);
                                        }

                                        Awaitable<RemoteFile>.Awaiter awaiter = _memoryDownloadTask.GetAwaiter();
                                        if (awaiter.IsCompleted)
                                        {
                                            Debug.Log($"{DebugEx.ClientPrefix} Memory loaded");
                                            RemoteFile result = awaiter.GetResult();
                                            switch (result.Kind)
                                            {
                                                case FileResponseStatus.NotFound:
                                                case FileResponseStatus.Unknown:
                                                case FileResponseStatus.ErrorDisconnected:
                                                    ui_progressCompilation.title = "Crashed (no memory)";
                                                    ui_progressCompilation.value = 1f;
                                                    break;
                                                case FileResponseStatus.OK:
                                                case FileResponseStatus.NotChanged:
                                                default:
                                                    _memory = result.File.Data;
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            ui_progressCompilation.title = "Crashed (loading memory)";
                                            ui_progressCompilation.value = (float)_memoryDownloadProgress.Progress.Item1 / (float)_memoryDownloadProgress.Progress.Item2;
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
                                ui_progressCompilation.title = "Crashed";
                                ui_progressCompilation.value = 1f;
                                isErrored = true;
                                SetProgressStatus("error");
                                break;
                            case Signal.Halt:
                                ui_progressCompilation.title = "Halted";
                                ui_progressCompilation.value = 1f;
                                SetProgressStatus("warning");
                                break;
                            case Signal.UndefinedExternalFunction:
                                ui_progressCompilation.title = "Crashed";
                                ui_progressCompilation.value = 1f;
                                SetProgressStatus("error");
                                isErrored = true;
                                break;
                            case Signal.PointerOutOfRange:
                                ui_progressCompilation.title = "Crashed";
                                ui_progressCompilation.value = 1f;
                                SetProgressStatus("error");
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
                    }
                    else if (source == null)
                    {
                        ui_progressCompilation.title = "Remote source";
                        ui_progressCompilation.value = 1f;
                        SetProgressStatus(null);
                    }
                    else if (!source.IsSuccess)
                    {
                        ui_progressCompilation.title = "Compile failed";
                        ui_progressCompilation.value = 1f;
                        SetProgressStatus("error");

                        _requestedTabSwitch = (Tab.Progress, Tab.Diagnostics);
                    }
                    else
                    {
                        ui_progressCompilation.title = "Invalid source";
                        ui_progressCompilation.value = 1f;
                        SetProgressStatus("error");

                        _requestedTabSwitch = (Tab.Progress, Tab.Diagnostics);
                    }
                    break;
                }
                default: throw new UnreachableException();
            }
        }

        if (ui_tabView.selectedTabIndex == (int)Tab.Terminal)
        {
            ui_labelTerminal.text = _terminalBuilder.ToString();

            if (isBottom)
            {
                ui_scrollTerminal.scrollOffset = ui_labelTerminal.layout.max - ui_scrollTerminal.contentViewport.layout.size;
            }
        }
    }

    public void Cleanup(UIDocument ui)
    {
        selectedUnitEntity = Entity.Null;
        refreshAt = float.PositiveInfinity;
        selectingFile = ImmutableArray<string>.Empty;
        _memory = null;
        _memoryDownloadProgress = null;
        // try { _memoryDownloadTask?.Cancel(); } catch { }
        _memoryDownloadTask = null;
        _scheduledSource = null;
        if (terminalSubscription != null && ConnectionManager.ClientOrDefaultWorld.IsClient())
        {
            ConnectionManager.ClientOrDefaultWorld.GetSystem<TerminalSystemClient>().Unsubscribe(terminalSubscription.Entity);
            terminalSubscription = null;
        }

        if (ui != null &&
            ui.rootVisualElement != null)
        {
            ui_ButtonSelect.clickable = null;
            ui_ButtonCompile.clickable = null;
            ui_ButtonHotReload.clickable = null;
            ui_ButtonHalt.clickable = null;
            ui_ButtonReset.clickable = null;
            ui_ButtonContinue.clickable = null;
            ui_labelTerminal.text = string.Empty;
            EndFileSelection();
        }
        gameObject.SetActive(false);
    }
}
