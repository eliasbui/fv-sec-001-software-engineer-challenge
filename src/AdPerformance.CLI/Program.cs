using AdPerformance.CLI;
using Microsoft.Extensions.Logging;

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (CliHelpRequested)
{
    Console.WriteLine(CliOptions.HelpText);
    return ExitCodes.Success;
}
catch (CliParseException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.HelpText);
    return ExitCodes.UsageError;
}

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSimpleConsole(opts =>
    {
        opts.SingleLine = true;
        opts.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
});

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var logger = loggerFactory.CreateLogger("AdPerformance");
try
{
    var command = new AggregateCommand(loggerFactory);
    return await command.RunAsync(options, cts.Token);
}
catch (OperationCanceledException)
{
    logger.LogWarning("Cancelled by user.");
    return ExitCodes.Unhandled;
}
catch (Exception ex)
{
    logger.LogError(ex, "Unhandled exception");
    return ExitCodes.Unhandled;
}
