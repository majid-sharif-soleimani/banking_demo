namespace Bank.Agent.Host.Logic;

/// <summary>
/// Checks the data of a table or a card before the UI gets it (phase 2 design plan, section 5). The model fills these in,
/// and a model can make mistakes, such as a row with a missing cell. A refused component is not shown; the model is told
/// why and can try again.
/// </summary>
public static class UiComponentCheck
{
    public static readonly string[] ColumnTypes = ["text", "money", "date", "number"];

    /// <returns>Null when the table can be shown, otherwise what is wrong, written for the model.</returns>
    public static string? Table(IReadOnlyList<string> columnTypes, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (columnTypes.Count == 0)
        {
            return "A table needs at least one column.";
        }

        if (columnTypes.FirstOrDefault(t => !ColumnTypes.Contains(t)) is { } unknown)
        {
            return $"Unknown column type '{unknown}'. Use one of: {string.Join(", ", ColumnTypes)}.";
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count != columnTypes.Count)
            {
                return $"Row {i + 1} has {rows[i].Count} cells, but the table has {columnTypes.Count} columns. Give every row one cell per column.";
            }
        }

        return null;
    }

    /// <returns>Null when the card can be shown, otherwise what is wrong, written for the model.</returns>
    public static string? Card(IReadOnlyList<string> labels) =>
        labels.Count == 0 ? "A card needs at least one field." : null;

    public static readonly string[] ChartKinds = ["line", "bar"];

    public const int MaxDiagramLength = 5000;

    public const int MaxSeries = 4;

    /// <returns>Null when the chart can be shown, otherwise what is wrong, written for the model.</returns>
    public static string? Chart(string kind, int labelCount, IReadOnlyList<int> seriesLengths)
    {
        if (!ChartKinds.Contains(kind))
        {
            return $"Unknown chart kind '{kind}'. Use line or bar.";
        }

        if (labelCount == 0 || seriesLengths.Count == 0)
        {
            return "A chart needs labels and at least one series.";
        }

        // Four colours stay apart for colour-blind readers too; more series need a table or several charts.
        if (seriesLengths.Count > MaxSeries)
        {
            return $"A chart can show at most {MaxSeries} series. Use a table, or several charts.";
        }

        return seriesLengths.Any(n => n != labelCount)
            ? $"Every series needs one value per label ({labelCount})."
            : null;
    }

    /// <returns>Null when the diagram can be shown, otherwise what is wrong, written for the model.</returns>
    public static string? Diagram(string mermaid) =>
        string.IsNullOrWhiteSpace(mermaid) ? "A diagram needs Mermaid text."
        : mermaid.Length > MaxDiagramLength ? $"A diagram can have at most {MaxDiagramLength} characters."
        : null;
}
