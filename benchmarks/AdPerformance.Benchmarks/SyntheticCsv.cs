using System.Globalization;
using System.Text;

namespace AdPerformance.Benchmarks;

/// <summary>
/// Generates synthetic CSV input on demand. Used as the seed data for
/// BenchmarkDotNet runs so results are stable across machines.
/// </summary>
internal static class SyntheticCsv
{
    public const string Header =
        "campaign_id,date,impressions,clicks,spend,conversions";

    /// <summary>
    /// Writes <paramref name="rowCount"/> synthetic rows to <paramref name="path"/>
    /// using a deterministic RNG. Campaigns are drawn from a pool of
    /// <paramref name="campaignCount"/> unique IDs to mimic the shape of the
    /// production input.
    /// </summary>
    public static void Write(string path, long rowCount, int campaignCount = 50)
    {
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8, bufferSize: 1 << 16);
        writer.WriteLine(Header);

        var rng = new Random(Seed: 42);
        var start = new DateOnly(2025, 1, 1);

        for (long i = 0; i < rowCount; i++)
        {
            var campaignId = $"CMP{rng.Next(campaignCount):000}";
            var date = start.AddDays(rng.Next(365));
            var impressions = rng.Next(100, 100_000);
            var clicks = rng.Next(0, impressions / 20 + 1);
            var spend = Math.Round(rng.NextDouble() * 1000, 2);
            var conversions = clicks == 0 ? 0 : rng.Next(0, Math.Max(1, clicks / 5));

            writer.Write(campaignId);
            writer.Write(',');
            writer.Write(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(impressions);
            writer.Write(',');
            writer.Write(clicks);
            writer.Write(',');
            writer.Write(spend.ToString("F2", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(conversions);
            writer.Write('\n');
        }
    }
}
