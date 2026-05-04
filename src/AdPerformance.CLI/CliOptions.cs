namespace AdPerformance.CLI;

/// <summary>Parsed CLI arguments. Validated at construction time.</summary>
public sealed record CliOptions(
    string InputPath,
    string OutputDirectory,
    int TopN,
    bool Verbose,
    int Workers)
{
    public const string DefaultOutputDirectory = "./output";
    public const int DefaultTopN = 10;
    /// <summary>0 means "auto: Environment.ProcessorCount - 1".</summary>
    public const int DefaultWorkers = 0;

    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? input = null;
        string outputDir = DefaultOutputDirectory;
        int topN = DefaultTopN;
        int workers = DefaultWorkers;
        bool verbose = false;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--help" or "-h":
                    throw new CliHelpRequested();
                case "--input" or "-i":
                    input = RequireNext(args, ref i, a);
                    break;
                case "--output-dir" or "-o":
                    outputDir = RequireNext(args, ref i, a);
                    break;
                case "--top-n" or "-n":
                    var v = RequireNext(args, ref i, a);
                    if (!int.TryParse(v, out topN) || topN <= 0)
                        throw new CliParseException($"--top-n must be a positive integer (got '{v}')");
                    break;
                case "--workers" or "-w":
                    var w = RequireNext(args, ref i, a);
                    if (!int.TryParse(w, out workers) || workers < 0)
                        throw new CliParseException($"--workers must be a non-negative integer (got '{w}')");
                    break;
                case "--verbose" or "-v":
                    verbose = true;
                    break;
                default:
                    if (a.StartsWith("--input="))
                        input = a["--input=".Length..];
                    else if (a.StartsWith("--output-dir="))
                        outputDir = a["--output-dir=".Length..];
                    else if (a.StartsWith("--top-n="))
                    {
                        var vv = a["--top-n=".Length..];
                        if (!int.TryParse(vv, out topN) || topN <= 0)
                            throw new CliParseException($"--top-n must be a positive integer (got '{vv}')");
                    }
                    else if (a.StartsWith("--workers="))
                    {
                        var ww = a["--workers=".Length..];
                        if (!int.TryParse(ww, out workers) || workers < 0)
                            throw new CliParseException($"--workers must be a non-negative integer (got '{ww}')");
                    }
                    else
                        throw new CliParseException($"Unknown argument: {a}");
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(input))
            throw new CliParseException("--input is required");

        try
        {
            input = PathSanitizer.NormalizePath(input!, "--input");
            outputDir = PathSanitizer.NormalizePath(outputDir, "--output-dir");
        }
        catch (ArgumentException ex)
        {
            throw new CliParseException(ex.Message);
        }

        return new CliOptions(input, outputDir, topN, verbose, workers);
    }

    private static string RequireNext(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new CliParseException($"{flag} requires a value");
        return args[++i];
    }

    public static string HelpText => """
        AdPerformance — aggregate ad campaign CSV data.

        Usage:
          AdPerformance --input <csv> [--output-dir <dir>] [--top-n <n>]
                        [--workers <n>] [--verbose]

        Options:
          -i, --input <path>        Input CSV file (required).
          -o, --output-dir <dir>    Directory for result CSVs. Default: ./output
          -n, --top-n <int>         Ranking size for each output file. Default: 10
          -w, --workers <int>       Parallel worker count. Default: 0 (auto: N-1 cores).
                                    1 disables parallelism and uses the single-threaded
                                    aggregator.
          -v, --verbose             Log progress as rows are processed.
          -h, --help                Show this help and exit.

        Outputs:
          <output-dir>/top10_ctr.csv  — top N campaigns by highest CTR
          <output-dir>/top10_cpa.csv  — top N campaigns by lowest CPA (excludes zero-conversion)
        """;
}

public sealed class CliParseException(string message) : Exception(message);

public sealed class CliHelpRequested : Exception;
