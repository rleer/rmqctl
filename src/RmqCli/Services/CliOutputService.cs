using RmqCli.Configuration;
using RmqCli.Models;
using Spectre.Console;

namespace RmqCli.Services;

public interface ICliOutputService
{
    // void ShowProgress(string message, int current, int total);
    void ShowStatus(string message);
    void ShowSuccess(string message);
    void ShowWarning(string message, bool addNewLine = false);
    void ShowError(string message, string? exception = null);

    void WritePublishResult(Destination dest, List<PublishResult> results, OutputFormat format = OutputFormat.Plain);
    Task<T> ExecuteWithProgress<T>(string description, int maxValue, Func<IProgress<int>, Task<T>> workload);
    bool IsInteractive { get; }
}

// ILogger -> stderr (technical diagnostics, enable with --verbose)
// AnsiConsole -> stderr (user-friendly output)
// (Json)Console -> stdout for results(structured output for automation)
public class CliOutputService : ICliOutputService
{
    private const string SuccessSymbol = "\u2714"; // ✓
    private const string WarningSymbol = "\u26A0"; // ⚠
    private const string ErrorSymbol = "\u2717"; // ✗
    private const string StatusSymbol = "\u26EF"; // ⛯

    private readonly CliConfig _cliConfig;

    public CliOutputService(CliConfig cliConfig)
    {
        _cliConfig = cliConfig;
    }

    public bool IsInteractive => !_cliConfig.JsonOutput && !Console.IsOutputRedirected;

    public void ShowStatus(string message)
    {
        if (!IsInteractive)
            return;
        AnsiConsole.MarkupLine($"{StatusSymbol} {message}");
    }

    public void ShowSuccess(string message)
    {
        if (!IsInteractive)
            return;
        AnsiConsole.MarkupLine($"{SuccessSymbol} {message}");
    }

    public void WritePublishResult(Destination dest, List<PublishResult> results, OutputFormat format = OutputFormat.Plain)
    {
        if (_cliConfig.JsonOutput)
            return;

        if (dest.Queue is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"  Queue:       {dest.Queue}");
        }
        else if (dest is { Exchange: not null, RoutingKey: not null })
        {
            AnsiConsole.MarkupLineInterpolated($"  Exchange:    {dest.Exchange}");
            AnsiConsole.MarkupLineInterpolated($"  Routing Key: {dest.RoutingKey}");
        }

        if (results.Count > 1)
        {
            var avgSize = Math.Round(results.Sum(m => m.MessageLength) / (double)results.Count, 2);
            var avgSizeString = ToSizeString(avgSize);
            var totalSizeString = ToSizeString(results.Sum(m => m.MessageLength));
            var timeString = $"{results[0].Timestamp:yyyy-MM-dd HH:mm:ss} UTC → {results[^1].Timestamp:yyyy-MM-dd HH:mm:ss} UTC";

            AnsiConsole.MarkupLineInterpolated($"  Message IDs: {results[0].MessageId} → {results[^1].MessageId}");
            AnsiConsole.MarkupLineInterpolated($"  Size:        {avgSizeString} avg. ({totalSizeString} total)");
            AnsiConsole.MarkupLineInterpolated($"  Time:        {timeString}");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"  Message ID:  {results[0].MessageId}");
            AnsiConsole.MarkupLineInterpolated($"  Size:        {results[0].MessageSize}");
            AnsiConsole.MarkupLineInterpolated($"  Timestamp:   {results[0].Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        }
    }

    public void ShowError(string message, string? exception = null)
    {
        if (_cliConfig.JsonOutput)
            return;

        var outputMessage = $"{ErrorSymbol} {message}";
        if (exception is not null)
        {
            outputMessage += $": {EscapeMarkup(exception)}";
        }

        AnsiConsole.MarkupLine(outputMessage);
    }

    public void ShowWarning(string message, bool addNewLine = false)
    {
        if (_cliConfig.JsonOutput)
            return;

        var outputMessage = $"{WarningSymbol} {message}";

        if (addNewLine)
            AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine(outputMessage);
    }

    public async Task<T> ExecuteWithProgress<T>(string description, int maxValue, Func<IProgress<int>, Task<T>> workload)
    {
        // TODO: Make progress bar threshold for interactive mode configurable
        if (!IsInteractive || (IsInteractive && maxValue < 3000))
        {
            // For non-interactive mode, provide a no-op progress reporter
            return await workload(new Progress<int>());
        }

        return await AnsiConsole.Progress()
            .AutoClear(true)
            .HideCompleted(true)
            .Columns(
                new SpinnerColumn(Spinner.Known.Dots), 
                new TaskDescriptionColumn(), 
                new ProgressBarColumn(), 
                new PercentageColumn())
            .StartAsync(async ctx =>
            {
                var progressTask = ctx.AddTask(description, maxValue: maxValue);

                var progress = new Progress<int>(value =>
                {
                    progressTask.Value = value;
                });

                return await workload(progress);
            });
    }

    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }

    private static string ToSizeString(double l)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;
        double size = l;
        switch (l)
        {
            case >= gb:
                size = Math.Round(l / gb, 2);
                return $"{size} GB";
            case >= mb:
                size = Math.Round(l / mb, 2);
                return $"{size} MB";
            case >= kb:
                size = Math.Round(l / kb, 2);
                return $"{size} KB";
            default:
                return $"{size} bytes";
        }
    }
}