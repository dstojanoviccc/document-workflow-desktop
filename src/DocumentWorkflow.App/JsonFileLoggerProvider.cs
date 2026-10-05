using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.App;

// One JSON event per line; daily files keep troubleshooting local and readable.
public sealed class JsonFileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string logDirectory = directory;
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    private sealed class FileLogger(JsonFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try
            {
                lock (provider.gate)
                {
                    Directory.CreateDirectory(provider.logDirectory);
                    var json = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, level = level.ToString(), category,
                        eventId = eventId.Id, message = formatter(state, exception),
                        properties = state is IEnumerable<KeyValuePair<string, object?>> values ? values.ToDictionary(x => x.Key, x => x.Value) : null,
                        exception = exception?.ToString() });
                    File.AppendAllText(Path.Combine(provider.logDirectory, $"workflow-{DateTime.UtcNow:yyyy-MM-dd}.jsonl"), json + Environment.NewLine);
                }
            }
            catch (IOException) { System.Diagnostics.Debug.WriteLine("Unable to write application log."); }
            catch (UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine("Unable to write application log."); }
        }
    }
}

