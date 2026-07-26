namespace Klonker.Core.Diagnostics;

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    string? ParameterId = null,
    string? Path = null);
