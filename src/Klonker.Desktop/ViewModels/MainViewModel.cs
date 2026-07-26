using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly ITemplateCatalog? catalog;

    public MainViewModel()
    {
    }

    public MainViewModel(ITemplateCatalog catalog)
    {
        this.catalog = catalog;
    }

    public ObservableCollection<TemplateListItemViewModel> Templates { get; } = [];

    public ObservableCollection<ParameterEditorViewModel> Parameters { get; } = [];

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasPreview => Preview is not null;

    public Exception? DiagnosticException { get; private set; }

    [ObservableProperty]
    public partial TemplateListItemViewModel? SelectedTemplate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial GenerationPreviewViewModel? Preview { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Loading the development template catalog…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public void Load()
    {
        if (catalog is null)
        {
            StatusMessage = "Design-time preview";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        DiagnosticException = null;
        Templates.Clear();

        try
        {
            var result = catalog.Load();
            if (!result.IsSuccess)
            {
                SetIssues(result.Issues, "The template catalog could not be loaded.");
                return;
            }

            foreach (var package in result.Value!.Packages)
            {
                Templates.Add(new TemplateListItemViewModel(package));
            }

            if (Templates.Count == 0)
            {
                ErrorMessage = "The local registry does not contain any templates.";
                StatusMessage = "No templates available";
                return;
            }

            SelectedTemplate = Templates[0];
            StatusMessage = $"{Templates.Count} development template loaded";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DiagnosticException = exception;
            ErrorMessage = "Klonker could not read the development sample catalog.";
            StatusMessage = "Template loading failed";
        }
        finally
        {
            IsBusy = false;
            PreviewCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        Preview = null;
        StatusMessage = "Rendering preview…";
        PreviewCommand.NotifyCanExecuteChanged();

        try
        {
            var values = Parameters.ToDictionary(
                parameter => parameter.Id,
                parameter => parameter.GetValue(),
                StringComparer.Ordinal);
            var result = await TemplatePlanner.CreatePlanAsync(
                SelectedTemplate.Package,
                values);

            if (!result.IsSuccess)
            {
                SetIssues(result.Issues, "The template configuration is invalid.");
                return;
            }

            Preview = new GenerationPreviewViewModel(result.Value!);
            StatusMessage = $"Preview ready · {result.Value!.Files.Length} files";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticException = exception;
            ErrorMessage = "Klonker could not read the selected template's content.";
            StatusMessage = "Preview failed";
        }
        finally
        {
            IsBusy = false;
            PreviewCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanPreview() => SelectedTemplate is not null && !IsBusy;

    partial void OnSelectedTemplateChanged(TemplateListItemViewModel? value)
    {
        foreach (var parameter in Parameters)
        {
            parameter.ValueChanged -= OnParameterValueChanged;
        }

        Parameters.Clear();
        Preview = null;
        ErrorMessage = null;

        if (value is not null)
        {
            foreach (var definition in value.Package.Manifest.Parameters)
            {
                var parameter = new ParameterEditorViewModel(definition);
                parameter.ValueChanged += OnParameterValueChanged;
                Parameters.Add(parameter);
            }
        }

        PreviewCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        PreviewCommand.NotifyCanExecuteChanged();
    }

    private void OnParameterValueChanged(object? sender, EventArgs eventArgs)
    {
        Preview = null;
        ErrorMessage = null;
        StatusMessage = "Configuration changed · preview to refresh";
    }

    private void SetIssues(
        IEnumerable<ValidationIssue> issues,
        string fallbackMessage)
    {
        var errors = issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        ErrorMessage = errors.Length == 0
            ? fallbackMessage
            : string.Join(Environment.NewLine, errors);
        StatusMessage = fallbackMessage;
    }
}
