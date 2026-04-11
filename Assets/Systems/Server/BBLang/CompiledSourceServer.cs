using System.Collections.Generic;
using System.Linq;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using Unity.Collections;

public class CompiledSourceServer : ICompiledSource
{
    public readonly FileId SourceFile;
    public long CompiledVersion;
    public long LatestVersion;
    public long HotReloadVersion;
    public CompilationStatus Status;

    public float Progress;
    public bool StatusChanged;
    public double LastStatusSync;

    public bool IsSuccess;

    public NativeArray<Instruction>? Code;
    public NativeArray<ExternalFunctionScopedSync>? GeneratedFunctions;
    public NativeArray<UnitCommandDefinition>? UnitCommandDefinitions;
    public CompiledDebugInformation DebugInformation;
    public DiagnosticsCollection Diagnostics;
    public CompilerResult Compiled;
    public BBLangGeneratorResult Generated;
    public Dictionary<FileId, ProgressRecord<(int Current, int Total)>> SubFiles;

    FileId ICompiledSource.SourceFile => SourceFile;
    CompilationStatus ICompiledSource.Status => Status;
    float ICompiledSource.Progress => Progress;
    bool ICompiledSource.IsSuccess => IsSuccess;
    IEnumerable<Diagnostic> ICompiledSource.Diagnostics => Diagnostics.Diagnostics.Append(Diagnostics.DiagnosticsWithoutContext);
    IReadOnlyDictionary<FileId, ProgressRecord<(int Current, int Total)>> ICompiledSource.SubFiles => SubFiles;

    public CompiledSourceServer(
        FileId sourceFile,
        long compiledVersion,
        long latestVersion,
        long hotReloadVersion,
        CompilationStatus status,
        float progress,
        bool isSuccess,
        NativeArray<Instruction>? code,
        NativeArray<UnitCommandDefinition>? unitCommandDefinitions,
        CompiledDebugInformation debugInformation,
        DiagnosticsCollection diagnostics)
    {
        SourceFile = sourceFile;
        CompiledVersion = compiledVersion;
        LatestVersion = latestVersion;
        HotReloadVersion = hotReloadVersion;
        Progress = progress;
        IsSuccess = isSuccess;
        Code = code;
        UnitCommandDefinitions = unitCommandDefinitions;
        DebugInformation = debugInformation;
        Diagnostics = diagnostics;
        Status = status;
        Compiled = CompilerResult.MakeEmpty(sourceFile.ToUri());
        SubFiles = new();
    }
}
