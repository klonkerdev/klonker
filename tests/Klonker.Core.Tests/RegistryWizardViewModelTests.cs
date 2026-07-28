using Klonker.Core.Tests.TestSupport;
using Klonker.Desktop.Services;
using Klonker.Desktop.ViewModels;

namespace Klonker.Core.Tests;

public sealed class RegistryWizardViewModelTests
{
    [Fact]
    public void ConfigureFields_ReevaluateContinueCommand()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var viewModel = new RegistryWizardViewModel(
            new RegistryConfigurationStore(temporaryDirectory.Path),
            new StubRegistryWorkspacePicker());
        viewModel.ChooseDevelopmentCommand.Execute(null);

        Assert.False(viewModel.NextCommand.CanExecute(null));
        var canExecuteChanged = 0;
        viewModel.NextCommand.CanExecuteChanged +=
            (_, _) => canExecuteChanged++;

        viewModel.WorkspacePath = Path.Combine(
            temporaryDirectory.Path,
            "registry");

        Assert.True(viewModel.CanGoNext);
        Assert.True(viewModel.NextCommand.CanExecute(null));
        Assert.True(canExecuteChanged > 0);
    }

    private sealed class StubRegistryWorkspacePicker
        : IRegistryWorkspacePicker
    {
        public Task<string?> PickFolderAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickPrivateKeyDestinationAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickExistingPrivateKeyAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
