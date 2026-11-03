using System.ComponentModel;
using System.Text.Json;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

public sealed record TableColumn(
    [property: Description("The column heading.")] string Label,
    [property: Description("text, money, date or number. Money and number cells are aligned right.")] string Type);

public sealed record CardField(
    [property: Description("The label, such as 'Balance'.")] string Label,
    [property: Description("The value as it should be shown, money with its currency, such as '1 200.00 SEK'.")] string Value);

public sealed record TableData(string Title, IReadOnlyList<TableColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record CardData(string Title, IReadOnlyList<CardField> Fields);

/// <summary>A table or a card the model asked to show: the tool name the UI renders by, and its data.</summary>
public sealed record UiComponent(string Tool, string Kind, JsonElement Data);

/// <summary>
/// The UI tools of one run: show_table and show_card (phase 2 design plan, section 5, decision A8).
///
/// Generative UI with tools: the model cannot draw anything, it can only write text and call tools. So a table is a
/// tool call: the model calls show_table with the columns and rows, and the arguments are the table. This class checks
/// the arguments, keeps the component in <see cref="Shown"/> for the AG-UI endpoint to stream to the browser, and
/// answers the model "shown" so it can go on writing. The browser renders the tool call as a table.
///
/// A new instance per run, so two runs never share what they show.
/// </summary>
public sealed class UiTools
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Queue<UiComponent> shown = new();

    public UiTools()
    {
        Tools =
        [
            AIFunctionFactory.Create(ShowTable, "show_table",
                "Shows a table to the person, for a list such as accounts, statement lines, installments or products. Every row has one cell per column, as display text."),
            AIFunctionFactory.Create(ShowCard, "show_card",
                "Shows one record to the person as a card with labelled fields, such as one account or one loan."),
        ];
    }

    public IReadOnlyList<AITool> Tools { get; }

    /// <summary>The components shown since the last call, oldest first.</summary>
    public IReadOnlyList<UiComponent> TakeShown()
    {
        var list = shown.ToList();
        shown.Clear();
        return list;
    }

    private string ShowTable(
        [Description("A short title, such as 'Your accounts'.")] string title,
        TableColumn[] columns,
        [Description("The rows; each row is a list of cell texts in column order.")] string[][] rows)
    {
        if (UiComponentCheck.Table([.. columns.Select(c => c.Type)], rows) is { } problem)
        {
            return $"Not shown: {problem}";
        }

        shown.Enqueue(new UiComponent("show_table", MessageKind.Table, JsonSerializer.SerializeToElement(new TableData(title, columns, rows), Json)));
        return "The table is shown to the person.";
    }

    private string ShowCard(
        [Description("A short title, such as 'Savings account 1234 5678 9'.")] string title,
        CardField[] fields)
    {
        if (UiComponentCheck.Card([.. fields.Select(f => f.Label)]) is { } problem)
        {
            return $"Not shown: {problem}";
        }

        shown.Enqueue(new UiComponent("show_card", MessageKind.Card, JsonSerializer.SerializeToElement(new CardData(title, fields), Json)));
        return "The card is shown to the person.";
    }
}
