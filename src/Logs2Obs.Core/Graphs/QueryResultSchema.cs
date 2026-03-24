namespace Logs2Obs.Core.Graphs;

public class QueryResultSchema
{
    public List<ColumnInfo> Columns { get; init; } = [];
    public int RowCount { get; init; }

    public bool HasTimeColumn() =>
        Columns.Any(c => c.IsTimestamp ||
            c.Name.Contains("time", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("date", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("bucket", StringComparison.OrdinalIgnoreCase));

    public bool HasCategoricalColumn() =>
        Columns.Any(c => c.IsCategorical);

    public bool HasSingleNumericColumn() =>
        Columns.Count(c => c.IsNumeric) == 1;

    public bool HasColumn(string name) =>
        Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool HasColumns(params string[] names) =>
        names.All(HasColumn);
}
