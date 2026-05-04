using AdPerformance.Core.Models;

namespace AdPerformance.Core.Abstractions;

/// <summary>
/// Streaming producer of <see cref="AdRecord"/> values. Implementations must
/// yield rows lazily so that consumers can aggregate without loading the
/// entire input into memory.
/// </summary>
public interface IAdRecordSource
{
    IAsyncEnumerable<AdRecord> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Count of rows that failed to parse or validate while reading. Populated
    /// as the enumeration progresses.
    /// </summary>
    long BadRowCount { get; }
}
