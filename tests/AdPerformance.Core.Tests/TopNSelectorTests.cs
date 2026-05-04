using AdPerformance.Core.Models;
using AdPerformance.Core.Ranking;
using FluentAssertions;

namespace AdPerformance.Core.Tests;

public sealed class TopNSelectorTests
{
    private static CampaignResult Build(string id, double ctr, double? cpa, long conversions = 1) =>
        new(
            CampaignId: id,
            TotalImpressions: 1000,
            TotalClicks: (long)(1000 * ctr),
            TotalSpend: 0m,
            TotalConversions: conversions,
            Ctr: ctr,
            Cpa: cpa);

    [Fact]
    public void TopByCtr_ReturnsHighestNDescending()
    {
        var input = new[]
        {
            Build("A", 0.10, 5),
            Build("B", 0.50, 5),
            Build("C", 0.30, 5),
            Build("D", 0.05, 5),
            Build("E", 0.40, 5),
        };

        var result = TopNSelector.TopByCtrDescending(input, 3);

        result.Select(r => r.CampaignId).Should().Equal("B", "E", "C");
    }

    [Fact]
    public void TopByCtr_BreaksTiesByCampaignIdAscending()
    {
        var input = new[]
        {
            Build("Z", 0.50, 5),
            Build("A", 0.50, 5),
            Build("M", 0.50, 5),
            Build("B", 0.10, 5),
        };

        var result = TopNSelector.TopByCtrDescending(input, 3);

        result.Select(r => r.CampaignId).Should().Equal("A", "M", "Z");
    }

    [Fact]
    public void TopByCtr_FewerThanN_ReturnsAllSorted()
    {
        var input = new[]
        {
            Build("A", 0.10, 5),
            Build("B", 0.20, 5),
        };

        var result = TopNSelector.TopByCtrDescending(input, 10);

        result.Should().HaveCount(2);
        result[0].CampaignId.Should().Be("B");
    }

    [Fact]
    public void TopByCpa_ExcludesZeroConversions()
    {
        var input = new[]
        {
            Build("A", 0.1, cpa: 10, conversions: 5),
            Build("B", 0.1, cpa: null, conversions: 0),
            Build("C", 0.1, cpa: 15, conversions: 3),
        };

        var result = TopNSelector.TopByCpaAscending(input, 10);

        result.Should().HaveCount(2);
        result.Should().NotContain(r => r.CampaignId == "B");
    }

    [Fact]
    public void TopByCpa_ReturnsLowestNAscending()
    {
        var input = new[]
        {
            Build("A", 0.1, 30, 1),
            Build("B", 0.1, 10, 1),
            Build("C", 0.1, 20, 1),
            Build("D", 0.1, 40, 1),
        };

        var result = TopNSelector.TopByCpaAscending(input, 3);

        result.Select(r => r.CampaignId).Should().Equal("B", "C", "A");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TopByCtr_RejectsInvalidN(int n)
    {
        var act = () => TopNSelector.TopByCtrDescending(Array.Empty<CampaignResult>(), n);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
