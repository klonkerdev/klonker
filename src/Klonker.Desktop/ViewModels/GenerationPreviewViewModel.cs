using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Core.Generation;

namespace Klonker.Desktop.ViewModels;

public sealed partial class GenerationPreviewViewModel : ViewModelBase
{
    public GenerationPreviewViewModel(GenerationPlan plan)
    {
        Plan = plan;
        DirectoryTree = BuildTree(plan.Files.Select(file => file.RelativePath));
        Files = new ObservableCollection<PreviewFileViewModel>(
            plan.Files.Select(file => new PreviewFileViewModel(file)));
        SelectedFile = Files.FirstOrDefault(file => file.File.IsText) ?? Files.FirstOrDefault();
    }

    public GenerationPlan Plan { get; }

    public string DirectoryTree { get; }

    public ObservableCollection<PreviewFileViewModel> Files { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedContent))]
    public partial PreviewFileViewModel? SelectedFile { get; set; }

    public string SelectedContent => SelectedFile?.Content ?? "Select a file to preview it.";

    private static string BuildTree(IEnumerable<string> paths)
    {
        var root = new TreeNode(string.Empty, isFile: false);
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var current = root;
            var segments = path.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                if (!current.Children.TryGetValue(segment, out var child))
                {
                    child = new TreeNode(segment, isFile: index == segments.Length - 1);
                    current.Children.Add(segment, child);
                }

                current = child;
            }
        }

        var builder = new StringBuilder();
        AppendChildren(root, builder, depth: 0);
        return builder.ToString().TrimEnd();
    }

    private static void AppendChildren(TreeNode node, StringBuilder builder, int depth)
    {
        foreach (var child in node.Children.Values
                     .OrderBy(item => item.IsFile)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            builder.Append(' ', depth * 2);
            builder.Append(child.Name);
            if (!child.IsFile)
            {
                builder.Append('/');
            }

            builder.AppendLine();
            AppendChildren(child, builder, depth + 1);
        }
    }

    private sealed class TreeNode
    {
        public TreeNode(string name, bool isFile)
        {
            Name = name;
            IsFile = isFile;
        }

        public string Name { get; }

        public bool IsFile { get; }

        public SortedDictionary<string, TreeNode> Children { get; } =
            new(StringComparer.Ordinal);
    }
}
