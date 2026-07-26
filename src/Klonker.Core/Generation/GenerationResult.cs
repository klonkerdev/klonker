using System.Collections.Immutable;
using Klonker.Core.Diagnostics;

namespace Klonker.Core.Generation;

public sealed record GenerationResult(
    GenerationStatus Status,
    string Message,
    ImmutableArray<ValidationIssue> Issues,
    Exception? DiagnosticException = null)
{
    public bool Succeeded => Status == GenerationStatus.Succeeded;
}
