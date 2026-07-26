using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Core.Templates;

namespace Klonker.Desktop.ViewModels;

public sealed partial class ParameterEditorViewModel : ViewModelBase
{
    public ParameterEditorViewModel(TemplateParameterDefinition definition)
    {
        Definition = definition;
        if (definition.DefaultValue is bool booleanDefault)
        {
            BooleanValue = booleanDefault;
        }
        else
        {
            Value = definition.DefaultValue as string;
        }
    }

    public event EventHandler? ValueChanged;

    public TemplateParameterDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Label => Definition.Label;

    public string? Description => Definition.Description;

    public bool IsText => Definition.Type == TemplateParameterType.Text;

    public bool IsChoice => Definition.Type == TemplateParameterType.Choice;

    public bool IsBoolean => Definition.Type == TemplateParameterType.Boolean;

    public ImmutableArray<string> Choices => Definition.Values;

    [ObservableProperty]
    public partial string? Value { get; set; }

    [ObservableProperty]
    public partial bool BooleanValue { get; set; }

    public object? GetValue() => IsBoolean ? BooleanValue : Value;

    partial void OnValueChanged(string? value)
    {
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnBooleanValueChanged(bool value)
    {
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
