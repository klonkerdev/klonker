using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Klonker.Desktop.ViewModels;

namespace Klonker.Desktop.Controls;

public partial class ProjectTreeView : UserControl
{
    public static readonly StyledProperty<IEnumerable?> NodesProperty =
        AvaloniaProperty.Register<ProjectTreeView, IEnumerable?>(nameof(Nodes));

    public static readonly StyledProperty<ProjectTreeNodeViewModel?> SelectedNodeProperty =
        AvaloniaProperty.Register<ProjectTreeView, ProjectTreeNodeViewModel?>(
            nameof(SelectedNode),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ExpandDirectoriesProperty =
        AvaloniaProperty.Register<ProjectTreeView, bool>(
            nameof(ExpandDirectories),
            defaultValue: true);

    public static readonly StyledProperty<string> AutomationNameProperty =
        AvaloniaProperty.Register<ProjectTreeView, string>(
            nameof(AutomationName),
            defaultValue: "Project tree");

    public ProjectTreeView()
    {
        InitializeComponent();
    }

    public IEnumerable? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public ProjectTreeNodeViewModel? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public bool ExpandDirectories
    {
        get => GetValue(ExpandDirectoriesProperty);
        set => SetValue(ExpandDirectoriesProperty, value);
    }

    public string AutomationName
    {
        get => GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodesProperty ||
            change.Property == ExpandDirectoriesProperty)
        {
            ApplyExpansionPreference();
        }
    }

    private void ApplyExpansionPreference()
    {
        if (Nodes is not IEnumerable<ProjectTreeNodeViewModel> nodes)
        {
            return;
        }

        foreach (var node in nodes)
        {
            SetExpansion(node, ExpandDirectories);
        }
    }

    private static void SetExpansion(ProjectTreeNodeViewModel node, bool isExpanded)
    {
        if (node.IsDirectory)
        {
            node.IsExpanded = isExpanded;
        }

        foreach (var child in node.Children)
        {
            SetExpansion(child, isExpanded);
        }
    }
}
