using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LanguageCore;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

public class ScriptsManager : Singleton<ScriptsManager>, IUISetup, IUICleanup
{
    float refreshAt;

    [Header("UI")]
    [SerializeField, NotNull] VisualTreeAsset? SourceItem = default;
    [SerializeField, NotNull] VisualTreeAsset? ProgressItem = default;
    [SerializeField, NotNull] VisualTreeAsset? DiagnosticsItem = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsErrorIcon = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsWarningIcon = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsInfoIcon = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsHintIcon = default;
    [SerializeField, NotNull] Texture2D? DiagnosticsOptimizationNoticeIcon = default;

    [NotNull] SourcesSchema? ui = default;
    int _localConnectionId = -1;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.O) && (!UI.IsUIFocused || ui.IsVisible()))
        {
            if (ui.IsVisible())
            {
                UIManager.Instance.CloseUI(this);
                return;
            }
            else if (!UIManager.Instance.AnyUIVisible)
            {
                UIManager.Instance.OpenUI(UIManager.Instance.Scripts)
                    .Setup(this);
            }
        }

        if (!ui.IsVisible()) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            return;
        }

        if (Time.time >= refreshAt)
        {
            RefreshUI();
            refreshAt = Time.time + .2f;
        }
    }

    public void Setup(VisualElement _ui)
    {
        ui = new(_ui);

        refreshAt = Time.time + .2f;

        ui.ScrollSources.Clear();

        if (ConnectionManager.ClientOrDefaultWorld.IsClient())
        {
            if (ConnectionManager.ClientOrDefaultWorld.Unmanaged.GetSystem<PlayerSystemClient>().TryGetLocalPlayer(out RealPlayer player))
            {
                _localConnectionId = player.ConnectionId;
            }
            else
            {
                _localConnectionId = -1;
            }
        }
        else
        {
            _localConnectionId = 0;
        }

        RefreshUI();
    }

    void SyncDiagnosticItems(VisualElement container, IReadOnlyCollection<Diagnostic> diagnostics, DiagnosticsLevel parentLevel)
    {
        container.SyncList<Diagnostic, TerminalDiagnosticsItemSchema>(
            diagnostics,
            DiagnosticsItem,
            (item, element, recycled) =>
            {
                element.Root.userData = item;
                DiagnosticsLevel fixedLevel = item.Level > parentLevel ? item.Level : parentLevel;

                element.DiagnosticIcon.text = fixedLevel switch
                {
                    DiagnosticsLevel.Error => "\uf06a",
                    DiagnosticsLevel.Warning => "\uf071",
                    DiagnosticsLevel.Information => "\uf05a",
                    DiagnosticsLevel.Hint => "\uf0eb",
                    DiagnosticsLevel.OptimizationNotice => "\uf129",
                    DiagnosticsLevel.FailedOptimization => "\uf129",
                    _ => throw new UnreachableException(),
                };
                element.DiagnosticIcon.SetExclusiveClass(fixedLevel switch
                {
                    DiagnosticsLevel.Error => "error",
                    DiagnosticsLevel.Warning => "warning",
                    DiagnosticsLevel.Information => "info",
                    DiagnosticsLevel.Hint => "secondary",
                    DiagnosticsLevel.OptimizationNotice => "secondary",
                    DiagnosticsLevel.FailedOptimization => "secondary",
                    _ => throw new UnreachableException(),
                }, new string[] { "error", "warning", "secondary", "info" });

                element.DiagnosticLabel.text = item.Message;

                if (item is DiagnosticAt diagnosticAt)
                {
                    string? file = FileChunkManagerSystem.ResolveFileUI(diagnosticAt.File, _localConnectionId, out bool isLocal);
                    if (isLocal)
                    {
                        //element.DiagnosticButtonVscode.style.display = DisplayStyle.Flex;
                        //var b = element.DiagnosticButtonVscode;
                        //element.DiagnosticButtonVscode.clickable = new Clickable(() =>
                        //{
                        //    Application.OpenURL($"vscode://file/{HttpUtility.UrlPathEncode(localPath)}:{diagnosticAt.Position.Range.Start.ToStringMin()}");
                        //});
                    }
                    element.DiagnosticLocationRemote.style.display = isLocal ? DisplayStyle.None : DisplayStyle.Flex;
                    element.DiagnosticLocation.style.display = DisplayStyle.Flex;
                    element.DiagnosticLocation.text = $"{file}:{diagnosticAt.Position.Range.Start.ToStringMin()}";
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

    public void RefreshUI()
    {
        IEnumerable<ICompiledSource> sources = (IEnumerable<ICompiledSource>?)ConnectionManager.ClientWorld?.GetSystem<CompilerSystemClient>().Sources.Values ?? ConnectionManager.ServerOrDefaultWorld.GetSystem<CompilerSystemServer>().CompiledSources.Values;

        ui.ScrollSources.SyncList(sources, SourceItem, (source, _e, recycled) =>
        {
            SourceItemSchema e = new(_e);

            e.SourceUri.text = FileChunkManagerSystem.ResolveFileUI(source.SourceFile.ToUri(), _localConnectionId, out _);

            if (source.Status == CompilationStatus.Done)
            {
                Diagnostic[] d = source.Diagnostics.Where(v => v.Level is not DiagnosticsLevel.OptimizationNotice and not DiagnosticsLevel.FailedOptimization).ToArray();
                if (d.Length == 0)
                {
                    e.FoldoutDiagnostics.style.display = DisplayStyle.None;
                }
                else
                {
                    e.FoldoutDiagnostics.style.display = DisplayStyle.Flex;
                    SyncDiagnosticItems(e.ScrollDiagnostics, d, default);
                }
            }
            else
            {
                e.FoldoutDiagnostics.style.display = DisplayStyle.None;
            }

            if (source.Status != CompilationStatus.Done && !float.IsNaN(source.Progress))
            {
                e.ScrollProgresses.SyncList(source.SubFiles.ToArray(), ProgressItem, (file, element, recycled) =>
                {
                    ProgressBar progressBar = element.Q<ProgressBar>();
                    progressBar.title = file.Key.Name.ToString();
                    progressBar.value = file.Value.Progress.Total == 0 ? 0f : (float)file.Value.Progress.Current / file.Value.Progress.Total;
                });
            }
            else
            {
                e.ScrollProgresses.Clear();
            }

            e.StatusIcon.text = source.Status switch
            {
                CompilationStatus.None => "\uf29c",
                CompilationStatus.Secuedued => "\uf192",
                CompilationStatus.Uploading => "\uf01b",
                CompilationStatus.Compiling => "\udb86\udd78",
                CompilationStatus.Generating => "\udb86\udd78",
                CompilationStatus.Generated => "\udb86\udd78",
                CompilationStatus.Done => source.IsSuccess ? "\uf05d" : "\uf05c",
                _ => throw new UnreachableException(),
            };
            e.StatusIcon.tooltip = source.Status switch
            {
                CompilationStatus.None => null,
                CompilationStatus.Secuedued => "Secuedued",
                CompilationStatus.Uploading => "Uploading",
                CompilationStatus.Compiling => "Compiling",
                CompilationStatus.Generating => "Generating",
                CompilationStatus.Generated => "Generated",
                CompilationStatus.Done => source.IsSuccess ? "Success" : "Failed",
                _ => throw new UnreachableException(),
            };
            e.StatusIcon.SetExclusiveClass(source.Status == CompilationStatus.Done ? source.IsSuccess ? "success" : "error" : null, new string[] { "error", "warning", "success" });

            e.ButtonCompile.clickable = new Clickable(() =>
            {
                World world = ConnectionManager.ClientOrDefaultWorld;

                if (world.IsServer())
                {
                    Debug.LogError($"Not implemented");
                    return;
                }

                NetcodeUtils.CreateRPC(world.Unmanaged, new SetProcessorSourceRequestRpc()
                {
                    Source = source.SourceFile.Name,
                    Entity = default,
                    IsHotReload = false,
                });
            });

            e.ButtonHotreload.clickable = new Clickable(() =>
            {
                World world = ConnectionManager.ClientOrDefaultWorld;

                if (world.IsServer())
                {
                    Debug.LogError($"Not implemented");
                    return;
                }

                NetcodeUtils.CreateRPC(world.Unmanaged, new SetProcessorSourceRequestRpc()
                {
                    Source = source.SourceFile.Name,
                    Entity = default,
                    IsHotReload = true,
                });
            });

            Tooltips.Instance.Reregister(e.Root);
        });
    }

    public void Cleanup()
    {
        refreshAt = float.PositiveInfinity;
        ui.ScrollSources.Clear();
        ui = null;
    }
}
