using AdPerformance.Core.Models;
using AdPerformance.Infrastructure.Csv;
using FluentAssertions;

namespace AdPerformance.Infrastructure.Tests;

public sealed class CsvResultWriterTests : IDisposable
{
    private readonly string _tempDir;

    public CsvResultWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adperf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task WritesHeaderAndRows_WithFixedPrecisionFormatting()
    {
        var rows = new List<CampaignResult>
        {
            new("CMP042", 125000, 6250, 12500.50m, 625, 0.05, 20.00),
            new("CMP015", 340000, 15300, 30600.25m, 1530, 0.045, 20.0016339869),
        };

        var writer = new CsvResultWriter(_tempDir);
        await writer.WriteAsync("top10_ctr.csv", rows);

        var path = Path.Combine(_tempDir, "top10_ctr.csv");
        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(3);
        lines[0].Should().Be("campaign_id,total_impressions,total_clicks,total_spend,total_conversions,CTR,CPA");
        lines[1].Should().Be("CMP042,125000,6250,12500.50,625,0.0500,20.00");
        lines[2].Should().Be("CMP015,340000,15300,30600.25,1530,0.0450,20.00");
    }

    [Fact]
    public async Task BlankCpa_WhenResultCpaIsNull()
    {
        var rows = new List<CampaignResult>
        {
            new("CMP001", 1000, 50, 10.00m, 0, 0.05, null),
        };

        var writer = new CsvResultWriter(_tempDir);
        await writer.WriteAsync("top10_ctr.csv", rows);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "top10_ctr.csv"));
        lines[1].Should().Be("CMP001,1000,50,10.00,0,0.0500,");
    }

    [Fact]
    public async Task CreatesOutputDirectory_IfMissing()
    {
        var nested = Path.Combine(_tempDir, "nested", "out");
        var writer = new CsvResultWriter(nested);
        await writer.WriteAsync("top10_cpa.csv", Array.Empty<CampaignResult>());

        File.Exists(Path.Combine(nested, "top10_cpa.csv")).Should().BeTrue();
    }

    [Fact]
    public async Task EmptyRows_WritesOnlyHeader()
    {
        var writer = new CsvResultWriter(_tempDir);
        await writer.WriteAsync("top10_cpa.csv", Array.Empty<CampaignResult>());

        var lines = await File.ReadAllLinesAsync(Path.Combine(_tempDir, "top10_cpa.csv"));
        lines.Should().ContainSingle().Which.Should()
            .Be("campaign_id,total_impressions,total_clicks,total_spend,total_conversions,CTR,CPA");
    }
}
