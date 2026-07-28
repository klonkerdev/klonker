using System.Collections.Immutable;
using Klonker.Core.Authoring;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Registry;
using Klonker.Core.Templates;
using Klonker.Core.Tests.TestSupport;
using Klonker.Desktop.Services;
using Klonker.Desktop.ViewModels;

namespace Klonker.Core.Tests;

public sealed class TemplateAuthoringTests
{
    [Fact]
    public async Task Planner_CreatesPublishableMultiPlatformSourcePackage()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = Path.Combine(
            temporaryDirectory.Path,
            "cpp-starter");
        var request = CreateRequest(
            destination,
            platforms: ["linux", "windows"],
            seeds:
            [
                new TemplateAuthoringSeedFile(
                    "src/main.cpp.sbn",
                    "int main() { return 0; }",
                    VariantSpecific: false),
                new TemplateAuthoringSeedFile(
                    "CMakeLists.txt.sbn",
                    "project({{ project_name }})",
                    VariantSpecific: true),
            ]);

        var planned = TemplateAuthoringPlanner.CreatePlan(request);

        Assert.True(planned.IsSuccess);
        Assert.Contains(
            planned.Value!.Files,
            file => file.RelativePath == "package.toml");
        Assert.Contains(
            planned.Value.Files,
            file => file.RelativePath ==
                "variants/linux-cmake/variant.toml");
        Assert.Contains(
            planned.Value.Files,
            file => file.RelativePath ==
                "variants/windows-cmake/content/CMakeLists.txt.sbn");
        Assert.Contains(
            planned.Value.Files,
            file => file.RelativePath == "content/README.md.sbn");

        var generated = await GenerationExecutor.ExecuteAsync(
            planned.Value,
            destination);
        var inspected = ExistingTemplateInspector.Inspect(destination);

        Assert.True(generated.Succeeded);
        Assert.Equal(
            ExistingTemplateKind.RegistrySourcePackage,
            inspected.Kind);
        Assert.False(inspected.HasErrors);
        Assert.Equal(
            ["linux", "windows"],
            inspected.Metadata?.Platforms);
    }

    [Fact]
    public void Planner_CreatesPlatformBuildMatrixWithIsolatedBuildSeeds()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var request = CreateRequest(
            Path.Combine(temporaryDirectory.Path, "matrix"),
            platforms: ["linux", "windows"],
            buildSystems: ["cmake", "xmake"],
            seeds:
            [
                new TemplateAuthoringSeedFile(
                    "CMakeLists.txt.sbn",
                    "project({{ project_name }})",
                    VariantSpecific: true,
                    BuildSystem: "cmake"),
                new TemplateAuthoringSeedFile(
                    "xmake.lua.sbn",
                    "target({{ project_name }})",
                    VariantSpecific: true,
                    BuildSystem: "xmake"),
            ]);

        var planned = TemplateAuthoringPlanner.CreatePlan(request);

        Assert.True(planned.IsSuccess);
        Assert.Equal(
            4,
            planned.Value!.Files.Count(file =>
                file.RelativePath.EndsWith(
                    "/variant.toml",
                    StringComparison.Ordinal)));
        Assert.Contains(
            planned.Value.Files,
            file => file.RelativePath ==
                "variants/windows-cmake/content/CMakeLists.txt.sbn");
        Assert.DoesNotContain(
            planned.Value.Files,
            file => file.RelativePath ==
                "variants/windows-cmake/content/xmake.lua.sbn");
        Assert.Contains(
            planned.Value.Files,
            file => file.RelativePath ==
                "variants/linux-xmake/content/xmake.lua.sbn");
    }

    [Fact]
    public void Planner_RejectsAnyPlatformMixedWithSpecificPlatform()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        var planned = TemplateAuthoringPlanner.CreatePlan(CreateRequest(
            Path.Combine(temporaryDirectory.Path, "invalid"),
            platforms: ["any", "windows"]));

        Assert.False(planned.IsSuccess);
        Assert.Contains(
            planned.Issues,
            issue => issue.Code == "authoring.platform_any_exclusive");
    }

    [Fact]
    public void Planner_ImportsScribanAndBinaryFilesButExcludesToolOutput()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var source = Path.Combine(temporaryDirectory.Path, "source");
        var destination = Path.Combine(temporaryDirectory.Path, "package");
        Directory.CreateDirectory(Path.Combine(source, "src"));
        Directory.CreateDirectory(Path.Combine(source, "bin"));
        File.WriteAllText(
            Path.Combine(source, "src", "main.cpp.sbn"),
            "class {{ project_name }} {};");
        File.WriteAllBytes(
            Path.Combine(source, "logo.png"),
            [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(source, "bin", "ignored.obj"), "output");

        var planned = TemplateAuthoringPlanner.CreatePlan(
            CreateRequest(
                destination,
                existingContentPath: source));

        Assert.True(planned.IsSuccess);
        Assert.Contains(
            planned.Value!.Files,
            file => file.RelativePath == "content/src/main.cpp.sbn");
        Assert.Contains(
            planned.Value.Files,
            file => file.RelativePath == "content/logo.png" && !file.IsText);
        Assert.DoesNotContain(
            planned.Value.Files,
            file => file.RelativePath.Contains(
                "ignored.obj",
                StringComparison.Ordinal));
        Assert.Contains(
            planned.Issues,
            issue => issue.Code == "authoring.source_excluded");
    }

    [Fact]
    public void Planner_RejectsDestinationInsideImportedSource()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = Path.Combine(
            temporaryDirectory.Path,
            "nested-package");

        var planned = TemplateAuthoringPlanner.CreatePlan(
            CreateRequest(
                destination,
                existingContentPath: temporaryDirectory.Path));

        Assert.False(planned.IsSuccess);
        Assert.Contains(
            planned.Issues,
            issue => issue.Code == "authoring.destination_inside_source");
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void Inspector_ReportsActionableRegistrySourceErrors()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temporaryDirectory.Path, "package.toml"),
            """
            schema_version = 1
            namespace = "Invalid Namespace"
            id = "demo"
            favorite = true
            """);

        var inspected = ExistingTemplateInspector.Inspect(
            temporaryDirectory.Path);

        Assert.True(inspected.HasErrors);
        Assert.Equal(
            ExistingTemplateKind.RegistrySourcePackage,
            inspected.Kind);
        Assert.Contains(
            inspected.Issues,
            issue => issue.Code == "authoring.schema_invalid");
        Assert.Contains(
            inspected.Issues,
            issue => issue.Code == "authoring.favorite_forbidden");
        Assert.Contains(
            inspected.Issues,
            issue => issue.Code == "authoring.variants_missing");
        Assert.Contains(
            inspected.Issues,
            issue => issue.Code == "authoring.property_required");
    }

    [Fact]
    public void Inspector_OrdinaryFolderProposesDetachedConversion()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temporaryDirectory.Path, "main.lua.sbn"),
            "print('{{ project_name }}')");

        var inspected = ExistingTemplateInspector.Inspect(
            temporaryDirectory.Path);

        Assert.False(inspected.HasErrors);
        Assert.Equal(ExistingTemplateKind.ProjectFolder, inspected.Kind);
        Assert.Equal(temporaryDirectory.Path, inspected.ContentSourcePath);
        Assert.Contains("main.lua.sbn", inspected.Files);
        Assert.Contains(
            inspected.Issues,
            issue => issue.Code == "authoring.inspect_manifest_missing" &&
                issue.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void OptionsLoader_UsesDataDrivenLanguageBuildSystemMapping()
    {
        const string json =
            """
            {
              "schema_version": 1,
              "licenses": [
                {
                  "id": "mit",
                  "name": "MIT",
                  "source_license": "MIT",
                  "summary": "Generated source: MIT"
                }
              ],
              "platforms": [
                {
                  "id": "windows",
                  "name": "Windows",
                  "description": "Windows"
                }
              ],
              "build_systems": [
                {
                  "id": "cmake",
                  "name": "CMake",
                  "description": "CMake",
                  "seed_files": []
                }
              ],
              "languages": [
                {
                  "id": "cpp",
                  "name": "C++",
                  "description": "Native",
                  "build_systems": ["cmake"],
                  "seed_files": []
                }
              ]
            }
            """;

        var options = TemplateAuthoringOptionsLoader.Parse(json);

        var language = Assert.Single(options.Languages);
        Assert.Equal("cpp", language.Id);
        Assert.Equal(["cmake"], language.BuildSystems.ToArray());
    }

    [Fact]
    public void DefaultOptions_AllStarterTemplatesUseRestrictedRenderer()
    {
        var options = TemplateAuthoringOptionsLoader.LoadDefault();
        var parameters = new ResolvedParameters(
        [
            new KeyValuePair<string, object>("project_name", "My Project"),
        ]);

        Assert.True(options.Languages.Length >= 5);
        Assert.True(options.Platforms.Length >= 3);
        foreach (var seed in options.Languages
                     .SelectMany(language => language.SeedFiles)
                     .Concat(options.BuildSystems.SelectMany(
                         buildSystem => buildSystem.SeedFiles)))
        {
            var path = RestrictedTemplateRenderer.Render(
                seed.Path,
                $"{seed.Path} (path)",
                parameters);
            var content = RestrictedTemplateRenderer.Render(
                seed.Content,
                seed.Path,
                parameters);
            Assert.True(path.IsSuccess);
            Assert.True(content.IsSuccess);
        }
    }

    [Fact]
    public void Wizard_NewTemplateFlowBuildsPreviewThroughClearSteps()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var options = TemplateAuthoringOptionsLoader.LoadDefault();
        var viewModel = new TemplateWizardViewModel(
            options,
            new CoreTemplateAuthoringService(),
            new StubFolderPicker());

        viewModel.ChooseNewTemplateCommand.Execute(null);

        Assert.Equal(
            TemplateWizardStepKind.Destination,
            viewModel.CurrentPage.Kind);
        Assert.Equal(6, viewModel.Steps.Count);

        viewModel.DestinationPath = Path.Combine(
            temporaryDirectory.Path,
            "wizard-package");
        Assert.True(viewModel.DestinationIsValid);
        Assert.True(viewModel.DestinationHasSuccess);
        viewModel.NextCommand.Execute(null);
        Assert.Equal(TemplateWizardStepKind.Basics, viewModel.CurrentPage.Kind);

        viewModel.NextCommand.Execute(null);
        Assert.Equal(
            TemplateWizardStepKind.Technology,
            viewModel.CurrentPage.Kind);
        Assert.Contains(
            viewModel.AvailableBuildSystems,
            option => option.Id == "cmake");

        viewModel.NextCommand.Execute(null);
        Assert.Equal(TemplateWizardStepKind.Metadata, viewModel.CurrentPage.Kind);

        viewModel.NextCommand.Execute(null);

        Assert.Equal(TemplateWizardStepKind.Preview, viewModel.CurrentPage.Kind);
        Assert.NotNull(viewModel.Preview);
        Assert.Contains(
            viewModel.Preview.Files,
            file => file.Path == "package.toml");
    }

    [Fact]
    public void Wizard_TechnologySupportsAnyAndMultipleBuildSystems()
    {
        var viewModel = new TemplateWizardViewModel(
            TemplateAuthoringOptionsLoader.LoadDefault(),
            new CoreTemplateAuthoringService(),
            new StubFolderPicker());

        Assert.True(Assert.Single(
            viewModel.Platforms,
            platform => platform.Id == "any").IsSelected);
        var windows = Assert.Single(
            viewModel.Platforms,
            platform => platform.Id == "windows");
        windows.IsSelected = true;
        Assert.False(Assert.Single(
            viewModel.Platforms,
            platform => platform.Id == "any").IsSelected);

        var cmake = Assert.Single(
            viewModel.AvailableBuildSystems,
            build => build.Id == "cmake");
        var xmake = Assert.Single(
            viewModel.AvailableBuildSystems,
            build => build.Id == "xmake");
        cmake.IsSelected = true;
        xmake.IsSelected = true;

        Assert.True(cmake.IsSelected);
        Assert.True(xmake.IsSelected);
    }

    [Fact]
    public void Wizard_CatalogTemplateCopiesContentAndManifestContract()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var loaded = TemplatePackageLoader.Load(RepositoryPaths.SamplePackage);
        Assert.True(loaded.IsSuccess);
        var package = loaded.Value! with { RegistryId = "tests" };
        var catalogTemplate = new RegistryTemplatePackage(
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
                "packages/sample",
                package.Manifest.SourceLicense,
                new string('0', 64),
                1,
                package.Manifest.Language),
            package);
        var viewModel = new TemplateWizardViewModel(
            TemplateAuthoringOptionsLoader.LoadDefault(),
            new CoreTemplateAuthoringService(),
            new StubFolderPicker(),
            [catalogTemplate]);

        viewModel.ChooseCatalogTemplateCommand.Execute(null);
        viewModel.SelectedCatalogTemplate =
            Assert.Single(viewModel.CatalogTemplates);

        Assert.Equal(
            TemplateWizardStepKind.CatalogTemplate,
            viewModel.CurrentPage.Kind);
        Assert.Equal("cpp", viewModel.SelectedLanguage?.Id);
        Assert.True(Assert.Single(
            viewModel.AvailableBuildSystems,
            build => build.Id == "cmake").IsSelected);
        Assert.True(Assert.Single(
            viewModel.Platforms,
            platform => platform.Id == "windows").IsSelected);

        viewModel.NextCommand.Execute(null);
        viewModel.DestinationPath = Path.Combine(
            temporaryDirectory.Path,
            "catalog-copy");
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);

        Assert.Equal(
            TemplateWizardStepKind.Preview,
            viewModel.CurrentPage.Kind);
        var packageManifest = Assert.Single(
            viewModel.Preview!.Files,
            file => file.Path == "package.toml");
        Assert.Contains("cpp_standard", packageManifest.Content);
        Assert.Contains(
            viewModel.Preview.Files,
            file => file.Path.EndsWith(
                "content/CMakeLists.txt.sbn",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegistryWorkspace_BuildsAndSignsImportedSourceGenerically()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var packageRoot = Path.Combine(temporaryDirectory.Path, "source-package");
        var packagePlan = TemplateAuthoringPlanner.CreatePlan(CreateRequest(
            packageRoot,
            platforms: ["any"],
            buildSystems: ["cmake", "xmake"],
            seeds:
            [
                new TemplateAuthoringSeedFile(
                    "src/main.cpp.sbn",
                    "int main() { return 0; }",
                    VariantSpecific: false),
                new TemplateAuthoringSeedFile(
                    "CMakeLists.txt.sbn",
                    "project({{ project_name }})",
                    VariantSpecific: true,
                    BuildSystem: "cmake"),
                new TemplateAuthoringSeedFile(
                    "xmake.lua.sbn",
                    "target({{ project_name }})",
                    VariantSpecific: true,
                    BuildSystem: "xmake"),
            ]));
        Assert.True(packagePlan.IsSuccess);
        Assert.True((await GenerationExecutor.ExecuteAsync(
            packagePlan.Value!,
            packageRoot)).Succeeded);

        var workspaceRoot = Path.Combine(temporaryDirectory.Path, "registry");
        var privateKeyPath = Path.Combine(
            temporaryDirectory.Path,
            "secure",
            "publisher.pem");
        var key = RegistrySigningKeyService.Create();
        var workspacePlan = RegistryWorkspacePlanner.CreatePlan(
            new RegistryWorkspaceRequest(
                workspaceRoot,
                "test-registry",
                "Test Registry",
                IsProduction: true,
                PublisherId: "test-publisher",
                SigningKeyId: "primary-2026",
                key.PublicKeySpki,
                packageRoot));
        Assert.True(workspacePlan.IsSuccess);
        Assert.True((await GenerationExecutor.ExecuteAsync(
            workspacePlan.Value!,
            workspaceRoot)).Succeeded);
        var moduleRoot = Path.Combine(
            workspaceRoot,
            "modules",
            "test",
            "math");
        Directory.CreateDirectory(Path.Combine(moduleRoot, "content"));
        File.WriteAllText(
            Path.Combine(moduleRoot, "module.toml"),
            """
            schema_version = 0
            id = "test.math"
            name = "Math helper"
            description = "Adds one math helper."
            version = "1.0.0"
            language = "cpp"
            source_license = "MIT"
            tags = ["cpp"]
            """);
        File.WriteAllText(
            Path.Combine(moduleRoot, "content", "math.hpp"),
            "#pragma once\n");
        Assert.True((await RegistrySigningKeyService.WritePrivateKeyAsync(
            privateKeyPath,
            key.PrivateKeyPem)).IsSuccess);

        var built = await RegistryDevelopmentBuilder.BuildAsync(
            new RegistryBuildRequest(
                workspaceRoot,
                Path.Combine(workspaceRoot, "dist"),
                privateKeyPath));

        Assert.True(
            built.IsSuccess,
            string.Join(Environment.NewLine, built.Issues.Select(
                issue => issue.Message)));
        Assert.Equal(2, built.Value!.PackageCount);
        Assert.Equal(1, built.Value.ModuleCount);
        Assert.True(built.Value.IsSigned);
        Assert.True(File.Exists(
            Path.Combine(
                workspaceRoot,
                "dist",
                "registry.json.sig.json")));
        var local = LocalRegistryLoader.Load(built.Value.IndexPath);
        Assert.True(local.IsSuccess);
        Assert.Equal(2, local.Value!.Templates.Length);
        Assert.Single(local.Value.Modules);
    }

    [Fact]
    public void Wizard_DestinationValidationUpdatesAsPathChanges()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var nonEmpty = Path.Combine(temporaryDirectory.Path, "non-empty");
        Directory.CreateDirectory(nonEmpty);
        File.WriteAllText(Path.Combine(nonEmpty, "keep.txt"), "keep");
        var viewModel = new TemplateWizardViewModel(
            TemplateAuthoringOptionsLoader.LoadDefault(),
            new CoreTemplateAuthoringService(),
            new StubFolderPicker());
        viewModel.ChooseNewTemplateCommand.Execute(null);

        viewModel.DestinationPath = nonEmpty;

        Assert.False(viewModel.DestinationIsValid);
        Assert.True(viewModel.DestinationHasError);
        Assert.Contains(
            "new or empty",
            viewModel.DestinationValidationMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanGoNext);

        viewModel.DestinationPath = Path.Combine(
            temporaryDirectory.Path,
            "new-package");

        Assert.True(viewModel.DestinationIsValid);
        Assert.False(viewModel.DestinationHasError);
        Assert.True(viewModel.CanGoNext);
    }

    private static TemplateAuthoringRequest CreateRequest(
        string destination,
        string? existingContentPath = null,
        ImmutableArray<string> platforms = default,
        ImmutableArray<string> buildSystems = default,
        ImmutableArray<TemplateAuthoringSeedFile> seeds = default) =>
        new(
            destination,
            existingContentPath,
            "local",
            "cpp-starter",
            "C++ Starter",
            "A small C++ starter.",
            "0.1.0",
            "cpp",
            buildSystems.IsDefault ? ["cmake"] : buildSystems,
            platforms.IsDefault ? ["windows"] : platforms,
            "MIT",
            "Generated source: MIT",
            CreateReadme: true,
            seeds.IsDefault ? [] : seeds);

    private sealed class StubFolderPicker : ITemplateAuthoringFolderPicker
    {
        public Task<string?> PickAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
