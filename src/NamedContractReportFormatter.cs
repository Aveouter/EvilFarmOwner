namespace EvilFarmOwner;

internal sealed record NamedContractReportItem(string DisplayName, int Quality, int Stack);

internal static class NamedContractReportFormatter
{
    public static IReadOnlyList<NamedContractReportItem> SummarizeItems(
        IEnumerable<ContractCargoSnapshotMessage> items)
    {
        return items
            .GroupBy(item => new { item.DisplayName, item.Quality })
            .Select(group => new NamedContractReportItem(
                group.Key.DisplayName,
                group.Key.Quality,
                group.Sum(item => item.Stack)))
            .OrderBy(item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.Quality)
            .ToArray();
    }

    public static string FormatItems(
        IEnumerable<ContractCargoSnapshotMessage> items,
        string emptyText)
    {
        IReadOnlyList<NamedContractReportItem> summarized = SummarizeItems(items);
        return summarized.Count == 0
            ? emptyText
            : string.Join(", ", summarized.Select(item =>
                $"{item.DisplayName} q{item.Quality} x{item.Stack}"));
    }
}
