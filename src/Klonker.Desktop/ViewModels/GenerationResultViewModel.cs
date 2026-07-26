using Klonker.Core.Generation;

namespace Klonker.Desktop.ViewModels;

public sealed class GenerationResultViewModel
{
    public GenerationResultViewModel(
        GenerationResult result,
        string destinationPath)
    {
        Result = result;
        DestinationPath = destinationPath;
        Title = result.Status switch
        {
            GenerationStatus.Succeeded => "Project generated",
            GenerationStatus.Cancelled => "Generation cancelled",
            GenerationStatus.Rejected => "Destination rejected",
            _ => "Generation failed",
        };
        DiagnosticDetails = BuildDiagnosticDetails(result);
    }

    public GenerationResult Result { get; }

    public string DestinationPath { get; }

    public string Title { get; }

    public string Message => Result.Message;

    public bool Succeeded => Result.Succeeded;

    public bool Failed => !Succeeded;

    public bool HasDiagnosticDetails =>
        !string.IsNullOrWhiteSpace(DiagnosticDetails);

    public string? DiagnosticDetails { get; }

    private static string? BuildDiagnosticDetails(GenerationResult result)
    {
        var details = result.Issues
            .Select(issue =>
                $"{issue.Code}: {issue.Message}" +
                (issue.Path is null ? string.Empty : $" [{issue.Path}]"))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (result.DiagnosticException is not null)
        {
            details.Add(
                $"{result.DiagnosticException.GetType().Name} " +
                $"(0x{result.DiagnosticException.HResult:X8}): " +
                result.DiagnosticException.Message);
        }

        return details.Count == 0
            ? null
            : string.Join(Environment.NewLine, details);
    }
}
