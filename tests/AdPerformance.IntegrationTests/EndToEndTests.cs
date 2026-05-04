using AdPerformance.CLI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdPerformance.IntegrationTests;

public sealed class EndToEndTests : IDisposable
{
    private readonly string _outputDir;

    public EndToEndTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "adperf_it_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true);
    }

    private const string ExpectedCtr = """
        campaign_id,total_impressions,total_clicks,total_spend,total_conversions,CTR,CPA
        CMP003,10000,1000,200.00,50,0.1000,4.00
        CMP006,100,10,10.00,2,0.1000,5.00
        CMP011,300,30,30.00,10,0.1000,3.00
        CMP005,500,40,40.00,20,0.0800,2.00
        CMP007,1000,80,80.00,16,0.0800,5.00
        CMP001,1000,50,50.00,10,0.0500,5.00
        CMP009,5000,250,500.00,25,0.0500,20.00
        CMP012,6000,240,120.00,20,0.0400,6.00
        CMP008,2000,60,30.00,10,0.0300,3.00
        CMP010,800,20,200.00,0,0.0250,
        """;

    private const string ExpectedCpa = """
        campaign_id,total_impressions,total_clicks,total_spend,total_conversions,CTR,CPA
        CMP005,500,40,40.00,20,0.0800,2.00
        CMP008,2000,60,30.00,10,0.0300,3.00
        CMP011,300,30,30.00,10,0.1000,3.00
        CMP003,10000,1000,200.00,50,0.1000,4.00
        CMP001,1000,50,50.00,10,0.0500,5.00
        CMP002,2000,40,40.00,8,0.0200,5.00
        CMP006,100,10,10.00,2,0.1000,5.00
        CMP007,1000,80,80.00,16,0.0800,5.00
        CMP012,6000,240,120.00,20,0.0400,6.00
        CMP009,5000,250,500.00,25,0.0500,20.00
        """;

    [Theory]
    [InlineData(1)]  // sequential
    [InlineData(2)]  // parallel, 2 workers
    [InlineData(4)]  // parallel, 4 workers
    [InlineData(0)]  // parallel, auto
    public async Task ProducesExpectedTop10Outputs(int workers)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample_small.csv");
        File.Exists(fixturePath).Should().BeTrue($"fixture must be copied to {fixturePath}");

        var options = new CliOptions(
            InputPath: fixturePath,
            OutputDirectory: _outputDir,
            TopN: 10,
            Verbose: false,
            Workers: workers);

        var cmd = new AggregateCommand(NullLoggerFactory.Instance);
        var exit = await cmd.RunAsync(options, CancellationToken.None);

        exit.Should().Be(ExitCodes.Success);

        var ctr = (await File.ReadAllTextAsync(Path.Combine(_outputDir, "top10_ctr.csv")))
            .ReplaceLineEndings("\n").TrimEnd('\n');
        var cpa = (await File.ReadAllTextAsync(Path.Combine(_outputDir, "top10_cpa.csv")))
            .ReplaceLineEndings("\n").TrimEnd('\n');

        ctr.Should().Be(ExpectedCtr, $"workers={workers} must produce identical output");
        cpa.Should().Be(ExpectedCpa, $"workers={workers} must produce identical output");
    }

    [Fact]
    public async Task ReturnsInputMissingExitCode_WhenFileDoesNotExist()
    {
        var options = new CliOptions(
            InputPath: "/does/not/exist.csv",
            OutputDirectory: _outputDir,
            TopN: 10,
            Verbose: false,
            Workers: 1);

        var cmd = new AggregateCommand(NullLoggerFactory.Instance);
        var exit = await cmd.RunAsync(options);

        exit.Should().Be(ExitCodes.InputMissing);
    }

    [Fact]
    public async Task CpaOutput_ExcludesCampaignsWithZeroConversions()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample_small.csv");
        var options = new CliOptions(
            InputPath: fixturePath,
            OutputDirectory: _outputDir,
            TopN: 10,
            Verbose: false,
            Workers: 0);

        var cmd = new AggregateCommand(NullLoggerFactory.Instance);
        await cmd.RunAsync(options, CancellationToken.None);

        var cpa = await File.ReadAllTextAsync(Path.Combine(_outputDir, "top10_cpa.csv"));
        cpa.Should().NotContain("CMP004"); // 0 conversions
        cpa.Should().NotContain("CMP010"); // 0 conversions
    }
}
