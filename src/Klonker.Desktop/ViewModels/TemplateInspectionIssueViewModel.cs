using Klonker.Core.Diagnostics;

namespace Klonker.Desktop.ViewModels;

public sealed class TemplateInspectionIssueViewModel
{
    public TemplateInspectionIssueViewModel(ValidationIssue issue)
    {
        Issue = issue;
    }

    public ValidationIssue Issue { get; }

    public string Severity => Issue.Severity.ToString();

    public string Message => Issue.Message;

    public string? Path => Issue.Path;

    public bool HasPath => !string.IsNullOrWhiteSpace(Path);

    public bool IsError => Issue.Severity == ValidationSeverity.Error;

    public bool IsWarning => Issue.Severity == ValidationSeverity.Warning;
}
