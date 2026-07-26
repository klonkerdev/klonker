namespace Klonker.Desktop.Services;

public interface IDestinationPicker
{
    Task<string?> PickAsync(CancellationToken cancellationToken = default);
}
