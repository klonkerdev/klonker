using CommunityToolkit.Mvvm.ComponentModel;

namespace Klonker.Desktop.ViewModels;

public enum ProjectTreeIconKind
{
    Folder,
    Code,
    Markdown,
    Build,
    Configuration,
    Binary,
    File,
}

public sealed partial class ProjectTreeNodeViewModel : ViewModelBase
{
    public ProjectTreeNodeViewModel(
        string name,
        string relativePath,
        ProjectTreeIconKind iconKind,
        IReadOnlyList<ProjectTreeNodeViewModel> children,
        PreviewFileViewModel? file = null,
        bool isExpanded = true)
    {
        Name = name;
        RelativePath = relativePath;
        IconKind = iconKind;
        Children = children;
        File = file;
        IsExpanded = isExpanded;
    }

    public string Name { get; }

    public string RelativePath { get; }

    public ProjectTreeIconKind IconKind { get; }

    public IReadOnlyList<ProjectTreeNodeViewModel> Children { get; }

    public PreviewFileViewModel? File { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public bool IsDirectory => IconKind == ProjectTreeIconKind.Folder;

    public bool IsCode => IconKind == ProjectTreeIconKind.Code;

    public bool IsMarkdown => IconKind == ProjectTreeIconKind.Markdown;

    public bool IsBuild => IconKind == ProjectTreeIconKind.Build;

    public bool IsConfiguration => IconKind == ProjectTreeIconKind.Configuration;

    public bool IsBinary => IconKind == ProjectTreeIconKind.Binary;

    public bool IsGenericFile => IconKind == ProjectTreeIconKind.File;
}
