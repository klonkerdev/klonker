using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Klonker.Core.Generation;

namespace Klonker.Desktop.ViewModels;

public sealed partial class GenerationPreviewViewModel : ViewModelBase
{
    public GenerationPreviewViewModel(GenerationPlan plan)
    {
        Plan = plan;
        Files = new ObservableCollection<PreviewFileViewModel>(
            plan.Files.Select(file => new PreviewFileViewModel(file)));
        TreeNodes = BuildTree(plan.Directories, Files);
        DirectoryTree = BuildTreeText(TreeNodes);
        SelectedFile = Files.FirstOrDefault(file => file.File.IsText) ?? Files.FirstOrDefault();
    }

    public GenerationPlan Plan { get; }

    public string DirectoryTree { get; }

    public ObservableCollection<PreviewFileViewModel> Files { get; }

    public IReadOnlyList<ProjectTreeNodeViewModel> TreeNodes { get; }

    [ObservableProperty]
    public partial ProjectTreeNodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedContent))]
    public partial PreviewFileViewModel? SelectedFile { get; set; }

    public string SelectedContent => SelectedFile?.Content ?? "Select a file to preview it.";

    public string SelectionPosition
    {
        get
        {
            var index = SelectedFile is null ? -1 : Files.IndexOf(SelectedFile);
            return index < 0 ? $"0 / {Files.Count}" : $"{index + 1} / {Files.Count}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectPreviousFile))]
    private void SelectPreviousFile()
    {
        var index = SelectedFile is null ? -1 : Files.IndexOf(SelectedFile);
        if (index > 0)
        {
            SelectedFile = Files[index - 1];
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextFile))]
    private void SelectNextFile()
    {
        var index = SelectedFile is null ? -1 : Files.IndexOf(SelectedFile);
        if (index >= 0 && index < Files.Count - 1)
        {
            SelectedFile = Files[index + 1];
        }
    }

    [RelayCommand]
    private void ExpandAll() => SetExpansion(TreeNodes, isExpanded: true);

    [RelayCommand]
    private void CollapseAll() => SetExpansion(TreeNodes, isExpanded: false);

    private bool CanSelectPreviousFile() =>
        SelectedFile is not null && Files.IndexOf(SelectedFile) > 0;

    private bool CanSelectNextFile() =>
        SelectedFile is not null &&
        Files.IndexOf(SelectedFile) < Files.Count - 1;

    partial void OnSelectedNodeChanged(ProjectTreeNodeViewModel? value)
    {
        if (value?.File is not null && !ReferenceEquals(SelectedFile, value.File))
        {
            SelectedFile = value.File;
        }
    }

    partial void OnSelectedFileChanged(PreviewFileViewModel? value)
    {
        var node = value is null ? null : FindFileNode(TreeNodes, value);
        if (!ReferenceEquals(SelectedNode, node))
        {
            SelectedNode = node;
        }

        if (node is not null)
        {
            ExpandAncestors(TreeNodes, node.RelativePath);
        }

        OnPropertyChanged(nameof(SelectionPosition));
        SelectPreviousFileCommand.NotifyCanExecuteChanged();
        SelectNextFileCommand.NotifyCanExecuteChanged();
    }

    private static void SetExpansion(
        IEnumerable<ProjectTreeNodeViewModel> nodes,
        bool isExpanded)
    {
        foreach (var node in nodes)
        {
            if (node.IsDirectory)
            {
                node.IsExpanded = isExpanded;
            }

            SetExpansion(node.Children, isExpanded);
        }
    }

    private static void ExpandAncestors(
        IEnumerable<ProjectTreeNodeViewModel> nodes,
        string filePath)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory ||
                !filePath.StartsWith(
                    $"{node.RelativePath}/",
                    StringComparison.Ordinal))
            {
                continue;
            }

            node.IsExpanded = true;
            ExpandAncestors(node.Children, filePath);
        }
    }

    private static ProjectTreeNodeViewModel[] BuildTree(
        IEnumerable<string> directories,
        IEnumerable<PreviewFileViewModel> files)
    {
        var root = new MutableTreeNode(string.Empty, string.Empty);
        foreach (var directory in directories.Order(StringComparer.Ordinal))
        {
            AddPath(root, directory, file: null);
        }

        foreach (var file in files.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            AddPath(root, file.Path, file);
        }

        return root.Children.Values
            .OrderBy(node => node.File is not null)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .Select(ToViewModel)
            .ToArray();
    }

    private static void AddPath(
        MutableTreeNode root,
        string path,
        PreviewFileViewModel? file)
    {
        var current = root;
        var relativePath = string.Empty;
        var segments = path.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            relativePath = relativePath.Length == 0
                ? segment
                : $"{relativePath}/{segment}";
            if (!current.Children.TryGetValue(segment, out var child))
            {
                child = new MutableTreeNode(segment, relativePath);
                current.Children.Add(segment, child);
            }

            current = child;
        }

        current.File = file;
    }

    private static ProjectTreeNodeViewModel ToViewModel(MutableTreeNode node)
    {
        var children = node.Children.Values
            .OrderBy(child => child.File is not null)
            .ThenBy(child => child.Name, StringComparer.Ordinal)
            .Select(ToViewModel)
            .ToArray();
        return new ProjectTreeNodeViewModel(
            node.Name,
            node.RelativePath,
            GetIconKind(node),
            children,
            node.File);
    }

    private static ProjectTreeIconKind GetIconKind(MutableTreeNode node)
    {
        if (node.File is null)
        {
            return ProjectTreeIconKind.Folder;
        }

        var extension = Path.GetExtension(node.Name);
        if (extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".c", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".h", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectTreeIconKind.Code;
        }

        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectTreeIconKind.Markdown;
        }

        if (node.Name.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmake", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectTreeIconKind.Build;
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".toml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectTreeIconKind.Configuration;
        }

        return node.File.File.IsText
            ? ProjectTreeIconKind.File
            : ProjectTreeIconKind.Binary;
    }

    private static string BuildTreeText(IEnumerable<ProjectTreeNodeViewModel> nodes)
    {
        var builder = new StringBuilder();
        AppendChildren(nodes, builder, depth: 0);
        return builder.ToString().TrimEnd();
    }

    private static void AppendChildren(
        IEnumerable<ProjectTreeNodeViewModel> nodes,
        StringBuilder builder,
        int depth)
    {
        foreach (var node in nodes)
        {
            builder.Append(' ', depth * 2);
            builder.Append(node.Name);
            if (node.IsDirectory)
            {
                builder.Append('/');
            }

            builder.AppendLine();
            AppendChildren(node.Children, builder, depth + 1);
        }
    }

    private static ProjectTreeNodeViewModel? FindFileNode(
        IEnumerable<ProjectTreeNodeViewModel> nodes,
        PreviewFileViewModel file)
    {
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node.File, file))
            {
                return node;
            }

            var match = FindFileNode(node.Children, file);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private sealed class MutableTreeNode
    {
        public MutableTreeNode(string name, string relativePath)
        {
            Name = name;
            RelativePath = relativePath;
        }

        public string Name { get; }

        public string RelativePath { get; }

        public PreviewFileViewModel? File { get; set; }

        public SortedDictionary<string, MutableTreeNode> Children { get; } =
            new(StringComparer.Ordinal);
    }
}
