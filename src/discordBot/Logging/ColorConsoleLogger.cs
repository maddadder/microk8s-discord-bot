using Microsoft.Extensions.Logging;

namespace discordBot.Logging;
public sealed class ColorConsoleLogger : ILogger
{
    private readonly string _name;
    private readonly Func<ColorConsoleLoggerConfiguration> _getCurrentConfig;

    public ColorConsoleLogger(
        string name,
        Func<ColorConsoleLoggerConfiguration> getCurrentConfig) =>
        (_name, _getCurrentConfig) = (name, getCurrentConfig);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) =>
        _getCurrentConfig().LogLevelToColorMap.ContainsKey(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ColorConsoleLoggerConfiguration config = _getCurrentConfig();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); // Customize the timestamp format
        if (config.EventId == 0 || config.EventId == eventId.Id)
        {
            ConsoleColor originalColor = Console.ForegroundColor;

            Console.ForegroundColor = config.LogLevelToColorMap[logLevel];
            Console.WriteLine($"{timestamp} - [{eventId.Id,2}: {logLevel,-12}] {_name} - {formatter(state, exception)}");
            Console.ForegroundColor = originalColor;
        }
    }
}