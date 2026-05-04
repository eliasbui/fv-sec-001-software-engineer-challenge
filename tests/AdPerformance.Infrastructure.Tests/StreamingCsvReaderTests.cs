using System.Text;
using AdPerformance.Infrastructure.Csv;
using FluentAssertions;

namespace AdPerformance.Infrastructure.Tests;

public sealed class StreamingCsvReaderTests
{
    private static StreamingCsvReader FromString(string csv) =>
        new(() => new StringReader(csv));

    private const string Header =
        "campaign_id,date,impressions,clicks,spend,conversions";

    [Fact]
    public async Task Reads_ValidRows()
    {
        var csv = $"""
            {Header}
            CMP001,2025-01-01,1000,50,10.50,5
            CMP002,2025-01-02,2000,100,20.00,10
            """;
        var reader = FromString(csv);

        var records = new List<Core.Models.AdRecord>();
        await foreach (var r in reader.ReadAsync()) records.Add(r);

        records.Should().HaveCount(2);
        records[0].CampaignId.Should().Be("CMP001");
        records[0].Impressions.Should().Be(1000);
        records[0].Spend.Should().Be(10.50m);
        records[0].Date.Should().Be(new DateOnly(2025, 1, 1));
        reader.BadRowCount.Should().Be(0);
    }

    [Fact]
    public async Task SkipsAndCounts_MalformedRows()
    {
        var csv = $"""
            {Header}
            CMP001,2025-01-01,1000,50,10.50,5
            CMP002,not-a-date,2000,100,20.00,10
            CMP003,2025-01-03,bad,100,20.00,10
            ,2025-01-04,1,1,1,1
            CMP005,2025-01-05,100,50,10.00,5
            """;
        var reader = FromString(csv);

        var count = 0;
        await foreach (var _ in reader.ReadAsync()) count++;

        count.Should().Be(2);
        reader.BadRowCount.Should().Be(3);
    }

    [Fact]
    public async Task SkipsRowsWhereClicksExceedImpressions()
    {
        var csv = $"""
            {Header}
            CMP001,2025-01-01,100,500,10.00,5
            CMP002,2025-01-02,200,50,10.00,5
            """;
        var reader = FromString(csv);

        var count = 0;
        await foreach (var _ in reader.ReadAsync()) count++;

        count.Should().Be(1);
        reader.BadRowCount.Should().Be(1);
    }

    [Fact]
    public async Task SkipsNegativeValues()
    {
        var csv = $"""
            {Header}
            CMP001,2025-01-01,-100,50,10.00,5
            CMP002,2025-01-02,100,-50,10.00,5
            CMP003,2025-01-03,100,50,-10.00,5
            CMP004,2025-01-04,100,50,10.00,-5
            CMP005,2025-01-05,100,50,10.00,5
            """;
        var reader = FromString(csv);

        var count = 0;
        await foreach (var _ in reader.ReadAsync()) count++;

        count.Should().Be(1);
        reader.BadRowCount.Should().Be(4);
    }

    [Fact]
    public async Task Handles_Utf8Bom()
    {
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Encoding.UTF8.GetBytes($"{Header}\nCMP001,2025-01-01,1,1,1.00,1\n"));
        var reader = new StreamingCsvReader(
            () => new StreamReader(new MemoryStream(bytes.ToArray()), detectEncodingFromByteOrderMarks: true));

        var count = 0;
        await foreach (var _ in reader.ReadAsync()) count++;
        count.Should().Be(1);
    }

    [Fact]
    public async Task Handles_CrlfLineEndings()
    {
        var csv = $"{Header}\r\nCMP001,2025-01-01,1,1,1.00,1\r\nCMP002,2025-01-02,2,2,2.00,2\r\n";
        var reader = FromString(csv);

        var count = 0;
        await foreach (var _ in reader.ReadAsync()) count++;
        count.Should().Be(2);
    }

    [Fact]
    public async Task Throws_WhenHeaderColumnsMissing()
    {
        var csv = "campaign_id,date\nCMP001,2025-01-01\n";
        var reader = FromString(csv);

        var act = async () =>
        {
            await foreach (var _ in reader.ReadAsync()) { }
        };
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task InternsCampaignIds()
    {
        var csv = $"""
            {Header}
            CMP001,2025-01-01,1,1,1.00,1
            CMP001,2025-01-02,2,2,2.00,2
            CMP001,2025-01-03,3,3,3.00,3
            """;
        var reader = FromString(csv);

        var records = new List<Core.Models.AdRecord>();
        await foreach (var r in reader.ReadAsync()) records.Add(r);

        records.Should().HaveCount(3);
        // All three rows must share the same reference for campaign_id.
        ReferenceEquals(records[0].CampaignId, records[1].CampaignId).Should().BeTrue();
        ReferenceEquals(records[1].CampaignId, records[2].CampaignId).Should().BeTrue();
    }

    [Fact]
    public async Task EmptyInput_YieldsNothing()
    {
        var reader = FromString(string.Empty);
        var count = 0;
        await foreach (var _ in reader.ReadAsync()) count++;
        count.Should().Be(0);
    }
}
