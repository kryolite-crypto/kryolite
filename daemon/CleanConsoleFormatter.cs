using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Kryolite.Daemon;

public static class CleanConsoleFormatterExtension
{
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026", Justification = "Types are retained")]
    [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
    public static ILoggingBuilder AddCleanConsole(this ILoggingBuilder builder)
    {
        return builder.AddConsoleFormatter<CleanConsoleFormatter, SimpleConsoleFormatterOptions>(x =>
        {
            x.IncludeScopes = false;
            x.UseUtcTimestamp = false;
            x.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            x.SingleLine = true;
        });
    }
}

public class CleanConsoleFormatter : ConsoleFormatter, IDisposable
{
    private readonly IDisposable? _optionsReloadToken;
    private SimpleConsoleFormatterOptions FormatterOptions;
    private const string LoglevelPadding = ":";
    private static readonly string _messagePadding = new string(' ', GetLogLevelString(LogLevel.Information).Length + LoglevelPadding.Length);
    private static readonly string _newLineWithMessagePadding = Environment.NewLine + _messagePadding;

    public CleanConsoleFormatter(IOptionsMonitor<SimpleConsoleFormatterOptions> options)
        : base("clean") => (_optionsReloadToken, FormatterOptions) = (options.OnChange(ReloadLoggerOptions), options.CurrentValue);

    private void ReloadLoggerOptions(SimpleConsoleFormatterOptions options) =>
        FormatterOptions = options;

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        if (logEntry.Formatter is null) {
            throw new ArgumentNullException(nameof(logEntry.Formatter));
        }

        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);

        if (logEntry.Exception == null && message == null)
        {
            return;
        }
        LogLevel logLevel = logEntry.LogLevel;
        ConsoleColors logLevelColors = GetLogLevelConsoleColors(logLevel);
        string logLevelString = GetLogLevelString(logLevel);

        string? timestamp = null;
        string? timestampFormat = FormatterOptions.TimestampFormat;
        if (timestampFormat != null)
        {
            DateTimeOffset dateTimeOffset = GetCurrentDateTime();
            timestamp = dateTimeOffset.ToString(timestampFormat, CultureInfo.InvariantCulture);
        }
        if (timestamp != null)
        {
            textWriter.Write(timestamp);
        }
        if (logLevelString != null)
        {
            textWriter.WriteColoredMessage(logLevelString, logLevelColors.Background, logLevelColors.Foreground);
        }
        CreateDefaultLogMessage(textWriter, logEntry, message, scopeProvider);
    }

    private void CreateDefaultLogMessage<TState>(TextWriter textWriter, in LogEntry<TState> logEntry, string message, IExternalScopeProvider? scopeProvider)
    {
        bool singleLine = FormatterOptions.SingleLine;
        int eventId = logEntry.EventId.Id;
        Exception? exception = logEntry.Exception;

        // Example:
        // info: ConsoleApp.Program[10]
        //       Request received

        // category and event id
        textWriter.Write(LoglevelPadding);
        if (!singleLine)
        {
            textWriter.Write(Environment.NewLine);
        }

        // scope information
        WriteScopeInformation(textWriter, scopeProvider, singleLine);
        WriteMessage(textWriter, message, singleLine);

        // Example:
        // System.InvalidOperationException
        //    at Namespace.Class.Function() in File:line X
        if (exception != null)
        {
            // exception message
            WriteMessage(textWriter, exception.ToString(), singleLine);
        }
        if (singleLine)
        {
            textWriter.Write(Environment.NewLine);
        }
    }

    private static void WriteMessage(TextWriter textWriter, string message, bool singleLine)
    {
        if (!string.IsNullOrEmpty(message))
        {
            if (singleLine)
            {
                textWriter.Write(' ');
                WriteReplacing(textWriter, Environment.NewLine, " ", message);
            }
            else
            {
                textWriter.Write(_messagePadding);
                WriteReplacing(textWriter, Environment.NewLine, _newLineWithMessagePadding, message);
                textWriter.Write(Environment.NewLine);
            }
        }

        static void WriteReplacing(TextWriter writer, string oldValue, string newValue, string message)
        {
            string newMessage = message.Replace(oldValue, newValue);
            writer.Write(newMessage);
        }
    }

    private DateTimeOffset GetCurrentDateTime()
    {
        return FormatterOptions.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }

    private ConsoleColors GetLogLevelConsoleColors(LogLevel logLevel)
    {
        // We shouldn't be outputting color codes for Android/Apple mobile platforms,
        // they have no shell (adb shell is not meant for running apps) and all the output gets redirected to some log file.
        bool disableColors = (FormatterOptions.ColorBehavior == LoggerColorBehavior.Disabled);

        if (disableColors)
        {
            return new ConsoleColors(null, null);
        }
        // We must explicitly set the background color if we are setting the foreground color,
        // since just setting one can look bad on the users console.
        return logLevel switch
        {
            LogLevel.Trace => new ConsoleColors(ConsoleColor.Gray, ConsoleColor.Black),
            LogLevel.Debug => new ConsoleColors(ConsoleColor.Gray, ConsoleColor.Black),
            LogLevel.Information => new ConsoleColors(ConsoleColor.DarkGreen, ConsoleColor.Black),
            LogLevel.Warning => new ConsoleColors(ConsoleColor.Yellow, ConsoleColor.Black),
            LogLevel.Error => new ConsoleColors(ConsoleColor.Black, ConsoleColor.DarkRed),
            LogLevel.Critical => new ConsoleColors(ConsoleColor.White, ConsoleColor.DarkRed),
            _ => new ConsoleColors(null, null)
        };
    }

    private void WriteScopeInformation(TextWriter textWriter, IExternalScopeProvider? scopeProvider, bool singleLine)
    {
        if (FormatterOptions.IncludeScopes && scopeProvider != null)
        {
            bool paddingNeeded = !singleLine;
            scopeProvider.ForEachScope((scope, state) =>
            {
                if (paddingNeeded)
                {
                    paddingNeeded = false;
                    state.Write(_messagePadding);
                    state.Write("=> ");
                }
                else
                {
                    state.Write(" => ");
                }
                state.Write(scope);
            }, textWriter);

            if (!paddingNeeded && !singleLine)
            {
                textWriter.Write(Environment.NewLine);
            }
        }
    }

    private readonly struct ConsoleColors
    {
        public ConsoleColors(ConsoleColor? foreground, ConsoleColor? background)
        {
            Foreground = foreground;
            Background = background;
        }

        public ConsoleColor? Foreground { get; }

        public ConsoleColor? Background { get; }
    }

    public void Dispose() => _optionsReloadToken?.Dispose();
}