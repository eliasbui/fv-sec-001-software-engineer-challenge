using AdPerformance.Core.Models;

namespace AdPerformance.Core.Ranking;

/// <summary>
/// Top-N ranking utilities for <see cref="CampaignResult"/>. Uses a bounded
/// <see cref="PriorityQueue{TElement, TPriority}"/> so runtime is O(N log K)
/// and memory is O(K), which matters when the aggregate set is larger than K.
/// </summary>
public static class TopNSelector
{
    /// <summary>
    /// Top <paramref name="n"/> campaigns by highest CTR (descending). Ties
    /// are broken by <c>campaign_id</c> ascending for deterministic output.
    /// </summary>
    public static IReadOnlyList<CampaignResult> TopByCtrDescending(
        IEnumerable<CampaignResult> source,
        int n)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);

        return SelectTop(
            source,
            n,
            keySelector: r => r.Ctr,
            descending: true);
    }

    /// <summary>
    /// Top <paramref name="n"/> campaigns by lowest CPA (ascending). Campaigns
    /// with zero conversions are excluded. Ties are broken by
    /// <c>campaign_id</c> ascending.
    /// </summary>
    public static IReadOnlyList<CampaignResult> TopByCpaAscending(
        IEnumerable<CampaignResult> source,
        int n)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);

        var filtered = source.Where(r => r.TotalConversions > 0 && r.Cpa.HasValue);
        return SelectTop(
            filtered,
            n,
            keySelector: r => r.Cpa!.Value,
            descending: false);
    }

    private static IReadOnlyList<CampaignResult> SelectTop(
        IEnumerable<CampaignResult> source,
        int n,
        Func<CampaignResult, double> keySelector,
        bool descending)
    {
        // We keep a min-heap of size N. When descending=true we push (value),
        // and the smallest at the root is evicted when a larger one arrives.
        // When descending=false we push (-value) so the heap still evicts the
        // worst candidate.
        var heap = new PriorityQueue<CampaignResult, double>(initialCapacity: n);
        foreach (var item in source)
        {
            var rawKey = keySelector(item);
            var key = descending ? rawKey : -rawKey;

            if (heap.Count < n)
            {
                heap.Enqueue(item, key);
            }
            else if (heap.TryPeek(out _, out var worst) && key > worst)
            {
                heap.DequeueEnqueue(item, key);
            }
        }

        // Heap is in ascending key order; reverse for best-first output.
        var buffer = new CampaignResult[heap.Count];
        for (var i = heap.Count - 1; i >= 0; i--)
        {
            buffer[i] = heap.Dequeue();
        }

        // Secondary sort: campaign_id ascending to break ties deterministically
        // within an equal-key run. The primary order is preserved by a stable
        // sort on the original key.
        Array.Sort(buffer, CompareResults);

        static int CompareResults(CampaignResult a, CampaignResult b)
            => StringComparer.Ordinal.Compare(a.CampaignId, b.CampaignId);

        // Re-sort fully by (key desc/asc, campaign_id asc).
        var sorted = descending
            ? buffer.OrderByDescending(r => keySelector(r)).ThenBy(r => r.CampaignId, StringComparer.Ordinal)
            : buffer.OrderBy(r => keySelector(r)).ThenBy(r => r.CampaignId, StringComparer.Ordinal);
        return sorted.ToArray();
    }
}
