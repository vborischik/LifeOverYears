using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Mirrors every logged message into a file alongside the console sink, so each
// run folder carries a complete run.log. The run folder doesn't exist yet when
// the first messages are logged (config load, DI wiring, vision call all happen
// before RunService creates it), so lines are buffered in memory until Attach
// gives this a path, then flushed and written straight through from then on.
//
// One log per process — this is a static singleton wired directly into the
// logger factory in Program.cs, not through Autofac, so it survives across
// every component the container constructs.
public sealed class RunLogProvider : ILoggerProvider
{
    public static readonly RunLogProvider Instance = new();

    private readonly object _lock = new();
    private readonly List<string> _buffer = new();
    private string? _path;
    private bool _fallbackFlushed;

    private RunLogProvider() { }

    // Called once the run folder exists. Writes out everything buffered so
    // far, then switches to appending each subsequent line straight to disk.
    public static void Attach(string runRoot)
    {
        var path = Path.Combine(runRoot, "run.log");
        lock (Instance._lock)
        {
            if (Instance._path is not null) return; // already attached this process
            File.WriteAllLines(path, Instance._buffer);
            Instance._buffer.Clear();
            Instance._path = path;
        }
    }

    // Fallback for a run that dies before Attach ever fires (bad photo path,
    // vision failure, config error) — otherwise the whole buffered log would
    // simply vanish with the process. No-op if Attach already ran (run.log
    // already has it) or if this has already written a fallback file once.
    public static void FlushIfUnattached()
    {
        lock (Instance._lock)
        {
            if (Instance._path is not null) return;
            if (Instance._fallbackFlushed) return;
            Instance._fallbackFlushed = true;

            if (Instance._buffer.Count == 0) return; // nothing to write, no file created

            var dir = Path.Combine("output", "errors");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllLines(path, Instance._buffer);
        }
    }

    private void WriteLine(string line)
    {
        lock (_lock)
        {
            if (_path is null)
                _buffer.Add(line);
            else
                File.AppendAllText(_path, line + Environment.NewLine);
        }
    }

    public ILogger CreateLogger(string categoryName) => new RunLogger(this, categoryName);

    public void Dispose() { }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace       => "TRC",
        LogLevel.Debug       => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning     => "WRN",
        LogLevel.Error       => "ERR",
        LogLevel.Critical    => "CRT",
        _                    => "???"
    };

    private sealed class RunLogger : ILogger
    {
        private readonly RunLogProvider _provider;
        private readonly string _category;

        public RunLogger(RunLogProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = $"{DateTime.Now:HH:mm:ss} {Abbreviate(logLevel)} {_category}  {formatter(state, exception)}";
            if (exception is not null)
                line += Environment.NewLine + exception;
            _provider.WriteLine(line);
        }
    }
}
