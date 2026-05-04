using AdPerformance.Core.Models;
using FluentAssertions;

namespace AdPerformance.Core.Tests;

public sealed class CampaignStatsMergeTests
{
    [Fact]
    public void Merge_AddsTotals()
    {
        var a = new CampaignStats("CMP001");
        var b = new CampaignStats("CMP001");

        a.Add(new AdRecord("CMP001", default, 100, 10, 5.00m, 2));
        a.Add(new AdRecord("CMP001", default, 200, 20, 10.00m, 4));
        b.Add(new AdRecord("CMP001", default, 50, 5, 2.50m, 1));

        a.Merge(b);

        a.TotalImpressions.Should().Be(350);
        a.TotalClicks.Should().Be(35);
        a.TotalSpend.Should().Be(17.50m);
        a.TotalConversions.Should().Be(7);
    }

    [Fact]
    public void Merge_EmptyShard_IsIdentity()
    {
        var a = new CampaignStats("CMP001");
        a.Add(new AdRecord("CMP001", default, 100, 10, 5m, 1));

        var before = (a.TotalImpressions, a.TotalClicks, a.TotalSpend, a.TotalConversions);
        a.Merge(new CampaignStats("CMP001"));
        var after = (a.TotalImpressions, a.TotalClicks, a.TotalSpend, a.TotalConversions);

        after.Should().Be(before);
    }

    [Fact]
    public void Merge_MismatchedCampaignId_Throws()
    {
        var a = new CampaignStats("CMP001");
        var b = new CampaignStats("CMP002");

        var act = () => a.Merge(b);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Merge_NullOther_Throws()
    {
        var a = new CampaignStats("CMP001");
        var act = () => a.Merge(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
