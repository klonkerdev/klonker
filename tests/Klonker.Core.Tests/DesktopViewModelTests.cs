using System.Collections.Immutable;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Registry;
using Klonker.Core.Templates;
using Klonker.Core.Tests.TestSupport;
using Klonker.Desktop.Services;
using Klonker.Desktop.ViewModels;

namespace Klonker.Core.Tests;

public sealed class DesktopViewModelTests
{
    [Fact]
    public async Task Load_CatalogFailure_CreatesVisibleErrorState()
    {
        var catalog = new StubCatalog(
            new OperationResult<TemplateCatalogSnapshot>(
                null,
                [
                    new ValidationIssue(
                        ValidationSeverity.Error,
                        "catalog.failure",
                        "Sample catalog failed."),
                ]));
        var viewModel = new MainViewModel(catalog);

        viewModel.Load();
        await viewModel.CatalogLoadTask;

        Assert.True(viewModel.HasError);
        Assert.Contains("Sample catalog failed.", viewModel.ErrorMessage);
        Assert.Empty(viewModel.Templates);
        Assert.Empty(viewModel.Packages);
    }

    [Fact]
    public async Task Preview_ValidConfiguration_CreatesSelectablePreview()
    {
        var viewModel = CreateLoadedViewModel();

        viewModel.OpenConfigurationCommand.Execute(viewModel.SelectedTemplate);
        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConfigurationView);
        Assert.False(viewModel.HasError);
        Assert.NotNull(viewModel.Preview);
        Assert.Equal(5, viewModel.Preview!.Files.Count);
        Assert.NotNull(viewModel.Preview.SelectedFile);
        Assert.Contains("CMake", viewModel.Preview.DirectoryTree);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Preview.SelectedContent));
    }

    [Fact]
    public async Task Preview_BuildsHierarchicalTreeWithSemanticFileIcons()
    {
        var viewModel = CreateLoadedViewModel();

        await viewModel.PreviewCommand.ExecuteAsync(null);

        var preview = Assert.IsType<GenerationPreviewViewModel>(viewModel.Preview);
        Assert.Equal(
            ["src", "CMakeLists.txt", "README.md"],
            preview.TreeNodes.Select(node => node.Name));

        var source = Assert.Single(preview.TreeNodes, node => node.Name == "src");
        Assert.True(source.IsDirectory);
        var cli = Assert.Single(source.Children, node => node.Name == "cli");
        Assert.True(cli.IsDirectory);
        Assert.All(
            cli.Children,
            node => Assert.Equal(ProjectTreeIconKind.Code, node.IconKind));

        var cmake = Assert.Single(
            preview.TreeNodes,
            node => node.Name == "CMakeLists.txt");
        var readme = Assert.Single(
            preview.TreeNodes,
            node => node.Name == "README.md");
        Assert.Equal(ProjectTreeIconKind.Build, cmake.IconKind);
        Assert.Equal(ProjectTreeIconKind.Markdown, readme.IconKind);
    }

    [Fact]
    public async Task Preview_SelectingTreeFileUpdatesRenderedContent()
    {
        var viewModel = CreateLoadedViewModel();
        await viewModel.PreviewCommand.ExecuteAsync(null);
        var preview = Assert.IsType<GenerationPreviewViewModel>(viewModel.Preview);
        var readme = Assert.Single(
            preview.TreeNodes,
            node => node.Name == "README.md");

        preview.SelectedNode = readme;

        Assert.Equal("README.md", preview.SelectedFile?.Path);
        Assert.Contains("Klonker generated", preview.SelectedContent);
    }

    [Fact]
    public async Task Preview_CopiedCppFilesAreStrictlyDecodedForDisplay()
    {
        var viewModel = CreateLoadedViewModel();
        await viewModel.PreviewCommand.ExecuteAsync(null);
        var preview = Assert.IsType<GenerationPreviewViewModel>(viewModel.Preview);
        var arguments = Assert.Single(
            preview.Files,
            file => file.Path == "src/cli/Arguments.cpp");

        preview.SelectedFile = arguments;

        Assert.False(arguments.File.IsText);
        Assert.Contains("#include", preview.SelectedContent);
        Assert.DoesNotContain("Binary file", preview.SelectedContent);
    }

    [Fact]
    public void Preview_InvalidUtf8InKnownTextExtensionRemainsBinary()
    {
        var plannedFile = new Klonker.Core.Generation.PlannedFile(
            "invalid.cpp",
            ImmutableArray.Create<byte>(0xFF, 0xFE),
            IsText: false,
            TextContent: null,
            SourceTemplatePath: "invalid.cpp");

        var previewFile = new PreviewFileViewModel(plannedFile);

        Assert.Contains("Binary file", previewFile.Content);
    }

    [Fact]
    public void Preview_CopiedLuaFileIsTextAndUsesCodeTreeIcon()
    {
        const string source = "local loaded = true\n-- ModAPI starter";
        var file = new PlannedFile(
            "init.lua",
            TestPackage.Text(source).ToImmutableArray(),
            IsText: false,
            TextContent: null,
            SourceTemplatePath: "init.lua");
        var plan = new GenerationPlan(
            new TemplateIdentity(
                "tests",
                "gof2.modapi.event-starter",
                "gof2.modapi",
                "event-starter",
                "0.1.0"),
            [],
            [file],
            []);

        var preview = new GenerationPreviewViewModel(plan);

        Assert.Equal(source, Assert.Single(preview.Files).Content);
        Assert.Equal(
            ProjectTreeIconKind.Code,
            Assert.Single(preview.TreeNodes).IconKind);
    }

    [Fact]
    public async Task Preview_CollapsingFolderDoesNotCollapseSiblingOrChildFolders()
    {
        var viewModel = CreateLoadedViewModel();
        await viewModel.PreviewCommand.ExecuteAsync(null);
        var preview = Assert.IsType<GenerationPreviewViewModel>(viewModel.Preview);
        var source = Assert.Single(preview.TreeNodes, node => node.Name == "src");
        var cli = Assert.Single(source.Children, node => node.Name == "cli");

        source.IsExpanded = false;

        Assert.False(source.IsExpanded);
        Assert.True(cli.IsExpanded);
    }

    [Fact]
    public async Task Preview_InvalidConfiguration_DoesNotCreateSuccessfulPreview()
    {
        var viewModel = CreateLoadedViewModel();
        var projectName = Assert.Single(
            viewModel.Parameters,
            parameter => parameter.Id == "project_name");
        projectName.Value = "not a C++ identifier";

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Preview);
        Assert.True(viewModel.HasError);
        Assert.Contains("valid C++ identifier", viewModel.ErrorMessage);
    }

    [Fact]
    public void Load_GroupsRegistryVariantsIntoOnePackageBeforeVariantSelection()
    {
        var viewModel = CreateLoadedViewModel(
            navigateToVariants: false,
            includeSixVariants: true);

        Assert.True(viewModel.IsCatalogView);
        Assert.True(viewModel.IsPackageSelection);
        var package = Assert.Single(viewModel.FilteredPackages);
        Assert.Same(package, viewModel.SelectedPackage);
        Assert.Null(viewModel.SelectedTemplate);
        Assert.Equal(6, package.Variants.Count);
        Assert.Equal(["Linux", "Windows"], package.Platforms);
        Assert.Equal(["CMake", "GNU Make", "xmake"], package.BuildSystems);
        Assert.All(
            package.Variants,
            variant => Assert.NotNull(variant.Package.LogoPath));
        Assert.Contains("native", package.Tags);
        Assert.True(
            package.TagChips
                .Select(tag => tag.Foreground.ToString())
                .Distinct(StringComparer.Ordinal)
                .Count() > 1);

        viewModel.ConfirmPackageCommand.Execute(null);

        Assert.True(viewModel.IsVariantSelection);
        Assert.Equal(6, viewModel.FilteredVariants.Count);
        Assert.NotNull(viewModel.SelectedTemplate);
        Assert.Contains(
            viewModel.FilteredVariants,
            variant => variant.IsWindows && variant.IsCMake);
        Assert.Contains(
            viewModel.FilteredVariants,
            variant => variant.IsLinux && variant.IsXmake);
        Assert.Equal(
            [
                MainViewModel.AllBuildSystems,
                "CMake",
                "GNU Make",
                "xmake",
            ],
            viewModel.BuildSystems);

        viewModel.SelectedPlatform = "Linux";
        viewModel.SelectedBuildSystem = "xmake";

        var linuxXmake = Assert.Single(viewModel.FilteredVariants);
        Assert.Equal("linux-xmake", linuxXmake.Variant);

        viewModel.BackToPackagesCommand.Execute(null);

        Assert.True(viewModel.IsPackageSelection);
        Assert.Null(viewModel.SelectedTemplate);
        Assert.Single(viewModel.FilteredPackages);
    }

    [Fact]
    public void TemplateFavorite_CanBeChangedForCurrentCatalogSession()
    {
        var viewModel = CreateLoadedViewModel();
        var template = Assert.IsType<TemplateListItemViewModel>(
            viewModel.SelectedTemplate);

        template.IsFavorite = false;

        Assert.False(template.IsFavorite);
        Assert.True(template.Package.Manifest.IsFavorite);
    }

    [Fact]
    public void TemplateCard_LuaVariantWithoutBuildSystemUsesVariantIdentity()
    {
        var manifest = TestManifests.Valid
            .Replace("id = \"test.console.windows\"", "id = \"gof2.modapi.imgui-menu\"")
            .Replace("family_id = \"test.console\"", "family_id = \"gof2.modapi\"")
            .Replace("variant_id = \"windows\"", "variant_id = \"imgui-menu\"")
            .Replace("name = \"Test Console\"", "name = \"GOF2 ModAPI\"")
            .Replace("build_system = \"cmake\"", "build_system = \"none\"")
            .Replace("language = \"cpp\"", "language = \"lua\"");
        using var testPackage = new TestPackage(
            manifest,
            new Dictionary<string, byte[]>
            {
                ["init.lua"] = TestPackage.Text("print('loaded')"),
            });
        var package = testPackage.Load() with { RegistryId = "tests" };
        var template = new RegistryTemplatePackage(
            "tests",
            "Test registry",
            new RegistryTemplateEntry(
                package.Manifest.FamilyId,
                package.Manifest.VariantId,
                package.Manifest.Id,
                package.Manifest.Name,
                package.Manifest.Description,
                package.Manifest.Version,
                package.Manifest.TargetOs,
                package.Manifest.BuildSystem,
                "packages/test.zip",
                package.Manifest.SourceLicense,
                new string('0', 64),
                1,
                package.Manifest.Language),
            package);

        var variant = new TemplateListItemViewModel(template);
        using var family = new PackageListItemViewModel([variant]);

        Assert.Equal("Lua", variant.Language);
        Assert.True(variant.IsLua);
        Assert.False(variant.HasBuildSystem);
        Assert.Equal("ImGui Menu", variant.VariantDisplayName);
        Assert.Equal("Windows", variant.Metadata);
        Assert.Equal(2, variant.PlatformColumnSpan);
        Assert.False(family.HasBuildSystems);
        Assert.Equal("No build system", family.BuildSystemSummary);
        Assert.Equal(2, family.PlatformColumnSpan);
    }

    [Fact]
    public void Filters_SearchAndPlatformUpdateVisibleTemplates()
    {
        var viewModel = CreateLoadedViewModel();

        viewModel.SearchText = "missing";

        Assert.Empty(viewModel.FilteredVariants);
        Assert.Null(viewModel.SelectedTemplate);

        viewModel.SearchText = "C++";
        viewModel.SelectedPlatform = "Windows";

        Assert.Single(viewModel.FilteredVariants);
        Assert.Equal("C++ CLI", viewModel.FilteredVariants[0].Name);

        viewModel.SearchText = "native";

        Assert.Single(viewModel.FilteredVariants);
        Assert.Equal(
            [MainViewModel.AllTags, "cli", "cpp", "native", "starter"],
            viewModel.AvailableTags);

        viewModel.SearchText = string.Empty;
        viewModel.SelectedTag = "native";

        Assert.Single(viewModel.FilteredVariants);

        viewModel.SelectedTag = "graphics";

        Assert.Empty(viewModel.FilteredVariants);
    }

    [Fact]
    public void Navigation_OpenAndBackSwitchesScreens()
    {
        var viewModel = CreateLoadedViewModel();

        viewModel.OpenConfigurationCommand.Execute(viewModel.SelectedTemplate);

        Assert.True(viewModel.IsConfigurationView);
        Assert.False(viewModel.IsCatalogView);

        viewModel.BackToCatalogCommand.Execute(null);

        Assert.True(viewModel.IsCatalogView);
        Assert.False(viewModel.IsConfigurationView);
        Assert.True(viewModel.IsVariantSelection);
    }

    [Fact]
    public async Task Generate_RequiresPreviewDestinationAndExplicitConfirmation()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = Path.Combine(temporaryDirectory.Path, "Generated");
        var viewModel = CreateLoadedViewModel(
            new CoreProjectGenerationService(),
            new StubDestinationPicker(destination));
        await viewModel.BrowseDestinationCommand.ExecuteAsync(null);

        Assert.Equal(destination, viewModel.DestinationPath);
        Assert.False(viewModel.RequestGenerationCommand.CanExecute(null));

        await viewModel.PreviewCommand.ExecuteAsync(null);
        viewModel.RequestGenerationCommand.Execute(null);

        Assert.True(viewModel.IsGenerationConfirmationVisible);
        Assert.Null(viewModel.GenerationResult);
        Assert.False(Directory.Exists(destination));

        await viewModel.ConfirmGenerationCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsGenerationConfirmationVisible);
        Assert.True(viewModel.GenerationResult?.Succeeded);
        Assert.False(viewModel.RequestGenerationCommand.CanExecute(null));
        Assert.True(File.Exists(Path.Combine(destination, "CMakeLists.txt")));
        Assert.Contains(
            "klonker.samples.local:",
            viewModel.Preview!.Plan.Template.QualifiedId);
    }

    [Fact]
    public async Task Generate_NonEmptyDestinationIsRejectedBeforeConfirmation()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = Path.Combine(temporaryDirectory.Path, "Existing");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");
        var viewModel = CreateLoadedViewModel(new CoreProjectGenerationService());
        viewModel.DestinationPath = destination;
        await viewModel.PreviewCommand.ExecuteAsync(null);

        viewModel.RequestGenerationCommand.Execute(null);

        Assert.False(viewModel.IsGenerationConfirmationVisible);
        Assert.True(viewModel.HasError);
        Assert.Contains("new or empty", viewModel.ErrorMessage);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "keep.txt")));
    }

    [Fact]
    public async Task Generate_FailureProvidesSafeDiagnosticDetails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = Path.Combine(temporaryDirectory.Path, "Generated");
        var viewModel = CreateLoadedViewModel(
            new FailingGenerationService(),
            new StubDestinationPicker(destination));
        await viewModel.BrowseDestinationCommand.ExecuteAsync(null);
        await viewModel.PreviewCommand.ExecuteAsync(null);
        viewModel.RequestGenerationCommand.Execute(null);

        await viewModel.ConfirmGenerationCommand.ExecuteAsync(null);

        Assert.True(viewModel.GenerationResult?.Failed);
        Assert.True(viewModel.GenerationResult?.HasDiagnosticDetails);
        Assert.Contains(
            "IOException",
            viewModel.GenerationResult?.DiagnosticDetails);
        Assert.DoesNotContain(
            " at ",
            viewModel.GenerationResult?.DiagnosticDetails,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ExposesDeclaredAfterGenerationPrerequisites()
    {
        var viewModel = CreateLoadedViewModel();

        Assert.True(viewModel.HasPrerequisites);
        Assert.Equal(["CMake 3.20 or later", "A C++20/23 toolchain"],
            viewModel.Prerequisites.Select(item => item.Name));
        Assert.All(
            viewModel.Prerequisites,
            item => Assert.Contains("build", item.RequiredFor));
    }

    [Fact]
    public async Task Preview_NavigationSelectsAdjacentFilesAndControlsExpansion()
    {
        var viewModel = CreateLoadedViewModel();
        await viewModel.PreviewCommand.ExecuteAsync(null);
        var preview = Assert.IsType<GenerationPreviewViewModel>(viewModel.Preview);
        var first = Assert.IsType<PreviewFileViewModel>(preview.SelectedFile);

        preview.SelectNextFileCommand.Execute(null);

        Assert.NotSame(first, preview.SelectedFile);
        Assert.Equal("2 / 5", preview.SelectionPosition);

        preview.CollapseAllCommand.Execute(null);
        Assert.All(
            Flatten(preview.TreeNodes).Where(node => node.IsDirectory),
            node => Assert.False(node.IsExpanded));

        preview.ExpandAllCommand.Execute(null);
        Assert.All(
            Flatten(preview.TreeNodes).Where(node => node.IsDirectory),
            node => Assert.True(node.IsExpanded));
    }

    [Fact]
    public void SyntaxHighlighter_CppPreservesTextAndClassifiesTokens()
    {
        const string source = "const int answer = 42; // fixed";

        var tokens = SyntaxHighlighter.Highlight(source, "main.cpp");

        Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
        Assert.Contains(
            tokens,
            token => token.Kind == SyntaxTokenKind.Keyword && token.Text.Contains("const"));
        Assert.Contains(
            tokens,
            token => token.Kind == SyntaxTokenKind.Type && token.Text.Contains("int"));
        Assert.Contains(tokens, token => token.Kind == SyntaxTokenKind.Number);
        Assert.Contains(tokens, token => token.Kind == SyntaxTokenKind.Comment);
    }

    [Fact]
    public void SyntaxHighlighter_CMakeAndMarkdownUseFileSpecificRules()
    {
        var cmake = SyntaxHighlighter.Highlight(
            "project(MyApp)\n# note",
            "CMakeLists.txt");
        var markdown = SyntaxHighlighter.Highlight(
            "# Title\nUse `cmake`.",
            "README.md");

        Assert.Contains(cmake, token => token.Kind == SyntaxTokenKind.Function);
        Assert.Contains(cmake, token => token.Kind == SyntaxTokenKind.Comment);
        Assert.Contains(markdown, token => token.Kind == SyntaxTokenKind.Heading);
        Assert.Contains(
            markdown,
            token => token.Kind == SyntaxTokenKind.StringLiteral);
    }

    [Fact]
    public void SyntaxHighlighter_LuaPreservesTextAndClassifiesModApiCalls()
    {
        const string source =
            "local loaded = false\nRegisterEvent(\"IsInGame\", function()\n" +
            "  loaded = true -- once\nend)";

        var tokens = SyntaxHighlighter.Highlight(source, "init.lua");

        Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
        Assert.Equal("Lua", SyntaxHighlighter.GetLanguageName("init.lua"));
        Assert.Contains(
            tokens,
            token => token.Kind == SyntaxTokenKind.Keyword &&
                     token.Text.Contains("local", StringComparison.Ordinal));
        Assert.Contains(
            tokens,
            token => token.Kind == SyntaxTokenKind.Function &&
                     token.Text.Contains("RegisterEvent", StringComparison.Ordinal));
        Assert.Contains(tokens, token => token.Kind == SyntaxTokenKind.Comment);
    }

    private static MainViewModel CreateLoadedViewModel(
        IProjectGenerationService? generationService = null,
        IDestinationPicker? destinationPicker = null,
        bool navigateToVariants = true,
        bool includeSixVariants = false)
    {
        var packageResult = TemplatePackageLoader.Load(RepositoryPaths.SamplePackage);
        Assert.True(packageResult.IsSuccess);
        var package = packageResult.Value! with
        {
            RegistryId = "klonker.samples.local",
        };
        var packages = CreateRegistryPackages(package, includeSixVariants);
        var catalog = new StubCatalog(
            new OperationResult<TemplateCatalogSnapshot>(
                new TemplateCatalogSnapshot(
                    packages,
                    "test-registries.json",
                    "test-cache",
                    Offline: false),
                []));
        var viewModel = new MainViewModel(
            catalog,
            generationService,
            destinationPicker);
        viewModel.Load();
        if (navigateToVariants)
        {
            viewModel.ConfirmPackageCommand.Execute(null);
        }

        return viewModel;
    }

    private static ImmutableArray<RegistryTemplatePackage> CreateRegistryPackages(
        TemplatePackage sourcePackage,
        bool includeSixVariants)
    {
        var variants = includeSixVariants
            ? new[]
            {
                ("linux-cmake", "linux", "cmake"),
                ("linux-make", "linux", "make"),
                ("linux-xmake", "linux", "xmake"),
                ("windows-cmake", "windows", "cmake"),
                ("windows-make", "windows", "make"),
                ("windows-xmake", "windows", "xmake"),
            }
            :
            [
                (
                    sourcePackage.Manifest.VariantId,
                    sourcePackage.Manifest.TargetOs,
                    sourcePackage.Manifest.BuildSystem),
            ];

        return variants
            .Select(variant =>
            {
                var manifest = sourcePackage.Manifest with
                {
                    Id = $"{sourcePackage.Manifest.FamilyId}.{variant.Item1}",
                    VariantId = variant.Item1,
                    Description =
                        $"A {variant.Item2} C++ command-line application using {variant.Item3}.",
                    TargetOs = variant.Item2,
                    BuildSystem = variant.Item3,
                    IsFavorite = variant.Item1 == "windows-cmake",
                };
                var package = sourcePackage with
                {
                    Manifest = manifest,
                };
                var entry = new RegistryTemplateEntry(
                    manifest.FamilyId,
                    manifest.VariantId,
                    manifest.Id,
                    manifest.Name,
                    manifest.Description,
                    manifest.Version,
                    manifest.TargetOs,
                    manifest.BuildSystem,
                    $"packages/{manifest.Id}.zip",
                    manifest.SourceLicense,
                    new string('0', 64),
                    1);
                return new RegistryTemplatePackage(
                    package.RegistryId,
                    "Klonker development samples",
                    entry,
                    package);
            })
            .ToImmutableArray();
    }

    private static IEnumerable<ProjectTreeNodeViewModel> Flatten(
        IEnumerable<ProjectTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private sealed class StubCatalog : ITemplateCatalog
    {
        private readonly OperationResult<TemplateCatalogSnapshot> result;

        public StubCatalog(OperationResult<TemplateCatalogSnapshot> result)
        {
            this.result = result;
        }

        public Task<OperationResult<TemplateCatalogSnapshot>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class StubDestinationPicker : IDestinationPicker
    {
        private readonly string destination;

        public StubDestinationPicker(string destination)
        {
            this.destination = destination;
        }

        public Task<string?> PickAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(destination);
        }
    }

    private sealed class FailingGenerationService : IProjectGenerationService
    {
        public Task<Klonker.Core.Generation.GenerationResult> GenerateAsync(
            GenerationPlan plan,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Klonker.Core.Generation.GenerationResult(
                GenerationStatus.Failed,
                "The project could not be generated.",
                [
                    new ValidationIssue(
                        ValidationSeverity.Error,
                        "generation.test_failure",
                        "A controlled test failure occurred."),
                ],
                new IOException("Disk unavailable.")));
    }
}
