using AdPerformance.Core.Models;

namespace AdPerformance.Core.Abstractions;

/// <summary>
/// Writes ranked results to persistent storage. Implementations decide on the
/// exact serialisation format (CSV, JSON, parquet, etc.).
/// </summary>
public interface IResultSink
{
    Task WriteAsync(
        string fileName,
        IReadOnlyList<CampaignResult> rows,
        CancellationToken cancellationToken = default);
}
