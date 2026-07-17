// TODO: remove smoke test
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Mirrors every logged message into an in-memory list alongside the console
// sink, so VideoSmokeTest can assert on log content (e.g. the constructed
// ffmpeg command) without FfmpegProvider exposing test-only hooks.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = new();

    public IReadOnlyList<string> Messages
    {
        get { lock (_messages) return _messages.ToList(); }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose() { }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<string> _messages;

        public CapturingLogger(List<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }
    }
}
