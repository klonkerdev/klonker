using System.Collections.Immutable;

namespace Klonker.Core.Diagnostics;

public sealed record OperationResult<T>
    where T : class
{
    public OperationResult(T? value, IEnumerable<ValidationIssue> issues)
    {
        Value = value;
        Issues = issues.ToImmutableArray();
    }

    public T? Value { get; }

    public ImmutableArray<ValidationIssue> Issues { get; }

    public bool IsSuccess =>
        Value is not null &&
        !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);
}
