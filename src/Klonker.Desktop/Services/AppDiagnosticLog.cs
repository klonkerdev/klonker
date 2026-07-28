using System.Text;

namespace Klonker.Desktop.Services;

public sealed class AppDiagnosticLog
{
    private readonly Lock syncRoot = new();
    private readonly AppSettingsStore settingsStore;
    private readonly string logDirectory;

    public AppDiagnosticLog(AppSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ??
            throw new ArgumentNullException(nameof(settingsStore));
        logDirectory = Path.Combine(settingsStore.ApplicationDataRoot, "logs");
    }

    public string LogPath => Path.Combine(logDirectory, "klonker.log");

    public void Write(
        DiagnosticLogLevel level,
        string eventName,
        string message,
        Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var settings = settingsStore.Load();
        if (!settings.IsSuccess ||
            !settings.Value!.DiagnosticLoggingEnabled ||
            level > settings.Value.DiagnosticLogLevel)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("O"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(eventName)
            .Append(": ")
            .Append(message.ReplaceLineEndings(" "))
            .ToString();
        if (exception is not null)
        {
            line += $" ({exception.GetType().Name}: " +
                $"{exception.Message.ReplaceLineEndings(" ")})";
        }

        lock (syncRoot)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(
                    LogPath,
                    line + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception logException) when (
                logException is IOException or UnauthorizedAccessException)
            {
                // Diagnostics must never make an application operation fail.
            }
        }
    }

    public bool Clear()
    {
        lock (syncRoot)
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    File.Delete(LogPath);
                }

                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
