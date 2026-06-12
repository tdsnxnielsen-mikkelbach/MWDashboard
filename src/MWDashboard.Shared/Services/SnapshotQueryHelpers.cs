namespace MWDashboard.Shared.Services;

/// <summary>
/// In-memory LINQ helpers that reduce an already-materialized snapshot result set to the latest
/// collection per grouping key. These run as LINQ-to-objects (after the query has executed), so
/// they involve no EF Core translation and are safe to use on any snapshot shape.
/// </summary>
internal static class SnapshotQueryHelpers
{
    /// <summary>Returns the single most-recent row per group key (by descending date).</summary>
    public static List<T> LatestPerKey<T, TKey>(
        this IEnumerable<T> rows,
        Func<T, TKey> keySelector,
        Func<T, DateTime> dateSelector)
        => rows
            .GroupBy(keySelector)
            .Select(g => g.OrderByDescending(dateSelector).First())
            .ToList();

    /// <summary>Returns every row that shares the latest date within each group key.</summary>
    public static IEnumerable<T> LatestDateRowsPerKey<T, TKey>(
        this IEnumerable<T> rows,
        Func<T, TKey> keySelector,
        Func<T, DateTime> dateSelector)
        => rows
            .GroupBy(keySelector)
            .SelectMany(g =>
            {
                var latest = g.Max(dateSelector);
                return g.Where(r => dateSelector(r) == latest);
            });
}
