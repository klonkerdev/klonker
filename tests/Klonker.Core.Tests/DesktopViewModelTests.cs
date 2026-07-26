using System.Collections.Immutable;
using Klonker.Core.Diagnostics;
using Klonker.Core.Templates;
using Klonker.Core.Tests.TestSupport;
using Klonker.Desktop.Services;
using Klonker.Desktop.ViewModels;

namespace Klonker.Core.Tests;

public sealed class DesktopViewModelTests
{
    [Fact]
    public void Load_CatalogFailure_CreatesVisibleErrorState()
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

        Assert.True(viewModel.HasError);
        Assert.Contains("Sample catalog failed.", viewModel.ErrorMessage);
        Assert.Empty(viewModel.Templates);
    }

    [Fact]
    public async Task Preview_ValidConfiguration_CreatesSelectablePreview()
    {
        var viewModel = CreateLoadedViewModel();

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasError);
        Assert.NotNull(viewModel.Preview);
        Assert.Equal(5, viewModel.Preview!.Files.Count);
        Assert.NotNull(viewModel.Preview.SelectedFile);
        Assert.Contains("CMake", viewModel.Preview.DirectoryTree);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Preview.SelectedContent));
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

    private static MainViewModel CreateLoadedViewModel()
    {
        var packageResult = TemplatePackageLoader.Load(RepositoryPaths.SamplePackage);
        Assert.True(packageResult.IsSuccess);
        var catalog = new StubCatalog(
            new OperationResult<TemplateCatalogSnapshot>(
                new TemplateCatalogSnapshot([packageResult.Value!]),
                []));
        var viewModel = new MainViewModel(catalog);
        viewModel.Load();
        return viewModel;
    }

    private sealed class StubCatalog : ITemplateCatalog
    {
        private readonly OperationResult<TemplateCatalogSnapshot> result;

        public StubCatalog(OperationResult<TemplateCatalogSnapshot> result)
        {
            this.result = result;
        }

        public OperationResult<TemplateCatalogSnapshot> Load() => result;
    }
}
