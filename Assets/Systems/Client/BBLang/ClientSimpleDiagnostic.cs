using System;
using System.Collections.Immutable;
using LanguageCore;

public class ClientSimpleDiagnostic : DiagnosticAt
{
    public uint Id { get; }
    public new ImmutableArray<ClientSimpleDiagnostic> SubErrors { get; }

    public ClientSimpleDiagnostic(uint id, DiagnosticsLevel level, string message, Position position, Uri file, ImmutableArray<ClientSimpleDiagnostic> suberrors)
        : base(level, message, position, file, false, suberrors.As<Diagnostic>(), ImmutableArray<DiagnosticRelatedInformation>.Empty, DiagnosticTag.None)
    {
        Id = id;
        SubErrors = suberrors;
    }
}
