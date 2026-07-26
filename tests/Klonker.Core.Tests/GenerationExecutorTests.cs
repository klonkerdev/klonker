using System.Collections.Immutable;
using Klonker.Core.Generation;
using Klonker.Core.Tests.TestSupport;

namespace Klonker.Core.Tests;

public sealed class GenerationExecutorTests
{
    [Fact]
    public async Task Execute_NewDestination_WritesCompletePlan()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(temporaryDirectory.Path, "Generated");
        var plan = CreatePlan(
            ("README.md", "hello"),
            ("src/main.cpp", "int main() {}"));

        var result = await GenerationExecutor.ExecuteAsync(plan, destination);

        Assert.True(result.Succeeded);
        Assert.Equal("hello", File.ReadAllText(System.IO.Path.Combine(destination, "README.md")));
        Assert.Equal(
            "int main() {}",
            File.ReadAllText(System.IO.Path.Combine(destination, "src", "main.cpp")));
        Assert.Empty(Directory.GetDirectories(temporaryDirectory.Path, "*.staging"));
    }

    [Fact]
    public async Task Execute_ExistingEmptyDestination_ReplacesItTransactionally()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(temporaryDirectory.Path, "Empty");
        Directory.CreateDirectory(destination);

        var result = await GenerationExecutor.ExecuteAsync(
            CreatePlan(("created.txt", "created")),
            destination);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "created",
            File.ReadAllText(System.IO.Path.Combine(destination, "created.txt")));
    }

    [Fact]
    public async Task Execute_NonEmptyDestination_IsRefusedWithoutOverwrite()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(temporaryDirectory.Path, "Existing");
        Directory.CreateDirectory(destination);
        var existingFile = System.IO.Path.Combine(destination, "keep.txt");
        File.WriteAllText(existingFile, "original");

        var result = await GenerationExecutor.ExecuteAsync(
            CreatePlan(("keep.txt", "replacement")),
            destination);

        Assert.Equal(GenerationStatus.Rejected, result.Status);
        Assert.Equal("original", File.ReadAllText(existingFile));
        Assert.Single(Directory.GetFiles(destination));
    }

    [Fact]
    public async Task Execute_UnsafePlan_IsRejectedAndCannotEscapeDestination()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(temporaryDirectory.Path, "Generated");
        var outside = System.IO.Path.Combine(temporaryDirectory.Path, "outside.txt");
        var plan = CreatePlan(("../outside.txt", "escaped"));

        var result = await GenerationExecutor.ExecuteAsync(plan, destination);

        Assert.Equal(GenerationStatus.Rejected, result.Status);
        Assert.False(File.Exists(outside));
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(temporaryDirectory.Path, "*.staging"));
    }

    [Fact]
    public async Task Execute_PreCancelledOperation_LeavesNoDestinationOrStaging()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(temporaryDirectory.Path, "Cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await GenerationExecutor.ExecuteAsync(
            CreatePlan(("file.txt", "content")),
            destination,
            cancellation.Token);

        Assert.Equal(GenerationStatus.Cancelled, result.Status);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(temporaryDirectory.Path, "*.staging"));
    }

    [Fact]
    public async Task Execute_AllWrittenFilesRemainUnderDestination()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(temporaryDirectory.Path, "Project");
        var result = await GenerationExecutor.ExecuteAsync(
            CreatePlan(
                ("one.txt", "one"),
                ("a/b/two.txt", "two"),
                ("a/c/three.txt", "three")),
            destination);

        Assert.True(result.Succeeded);
        var destinationFull = System.IO.Path.GetFullPath(destination);
        foreach (var file in Directory.GetFiles(destination, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(destinationFull, file);
            Assert.DoesNotContain("..", relative, StringComparison.Ordinal);
            Assert.False(System.IO.Path.IsPathRooted(relative));
        }
    }

    private static GenerationPlan CreatePlan(params (string Path, string Content)[] files)
    {
        var plannedFiles = files.Select(file =>
            new PlannedFile(
                file.Path,
                TestPackage.Text(file.Content).ToImmutableArray(),
                IsText: true,
                file.Content,
                file.Path)).ToImmutableArray();
        var directories = plannedFiles
            .SelectMany(file => ParentPaths(file.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        return new GenerationPlan(
            new TemplateIdentity("test", "test", "default", "1.0.0"),
            directories,
            plannedFiles,
            []);
    }

    private static IEnumerable<string> ParentPaths(string path)
    {
        var segments = path.Split('/');
        for (var length = 1; length < segments.Length; length++)
        {
            yield return string.Join('/', segments.Take(length));
        }
    }
}
