namespace PassDpi.Core;

public enum LogLevel { Info, Warn, Error }

/// <summary>Minimal logging abstraction so lower layers don't depend on the service's logger.</summary>
public interface ILog
{
    void Write(LogLevel level, string message);
}

public static class LogExtensions
{
    public static void Info(this ILog log, string m) => log.Write(LogLevel.Info, m);
    public static void Warn(this ILog log, string m) => log.Write(LogLevel.Warn, m);
    public static void Error(this ILog log, string m) => log.Write(LogLevel.Error, m);
}

public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Write(LogLevel level, string message) { }
}
