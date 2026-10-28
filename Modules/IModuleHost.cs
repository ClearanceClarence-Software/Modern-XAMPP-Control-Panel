using XamppControl.Core;

namespace XamppControl.Modules;

public enum LogLevel
{
    Info,
    Ok,
    Warn,
    Error,
    Debug,
}

/// <summary>What a module driver can ask of the panel.</summary>
public interface IModuleHost
{
    string Root { get; }
    AppSettings Settings { get; }
    void Write(string source, string message, LogLevel level = LogLevel.Info);

    /// <summary>Only shown when "Show debug information" is on.</summary>
    void Debug(string source, string message);

    void TailLog(string source, string path, int lines);
}
