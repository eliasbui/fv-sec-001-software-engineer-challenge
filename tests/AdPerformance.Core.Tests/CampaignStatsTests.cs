using AdPerformance.Core.Models;
using FluentAssertions;

namespace AdPerformance.Core.Tests;

public sealed class CampaignStatsTests
{
    [Fact]
    public void Add_SumsAllFields()
    {
        var stats = new CampaignStats("CMP001");
        var r1 = new AdRecord("CMP001", new DateOnly(2025, 1, 1), 1000, 50, 10m, 5);
        var r2 = new AdRecord("CMP001", new DateOnly(2025, 1, 2), 2000, 100, 20.5m, 10);

        stats.Add(in r1);
        stats.Add(in r2);

        stats.TotalImpressions.Should().Be(3000);
        stats.TotalClicks.Should().Be(150);
        stats.TotalSpend.Should().Be(30.5m);
        stats.TotalConversions.Should().Be(15);
    }

    [Fact]
    public void Ctr_IsClicksOverImpressions()
    {
        var stats = new CampaignStats("CMP001");
        var r = new AdRecord("CMP001", default, 200, 10, 0m, 0);
        stats.Add(in r);
        stats.Ctr.Should().BeApproximately(0.05d, precision: 1e-9);
    }

    [Fact]
    public void Ctr_WithZeroImpressions_IsZero()
    {
        var stats = new CampaignStats("CMP001");
        var r = new AdRecord("CMP001", default, 0, 0, 1m, 1);
        stats.Add(in r);
        stats.Ctr.Should().Be(0d);
    }

    [Fact]
    public void Cpa_IsSpendOverConversions()
    {
        var stats = new CampaignStats("CMP001");
        var r = new AdRecord("CMP001", default, 0, 0, 100m, 10);
        stats.Add(in r);
        stats.Cpa.Should().NotBeNull();
        stats.Cpa!.Value.Should().BeApproximately(10d, precision: 1e-9);
    }

    [Fact]
    public void Cpa_WithZeroConversions_IsNull()
    {
        var stats = new CampaignStats("CMP001");
        var r = new AdRecord("CMP001", default, 1000, 50, 42m, 0);
        stats.Add(in r);
        stats.Cpa.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ctor_RejectsInvalidCampaignId(string? id)
    {
        var act = () => new CampaignStats(id!);
        act.Should().Throw<ArgumentException>();
    }
}
