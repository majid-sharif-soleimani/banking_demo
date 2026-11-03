using System.Buffers.Text;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

public sealed record TableColumn(
    [property: Description("The column heading.")] string Label,
    [property: Description("text, money, date or number. Money and number cells are aligned right.")] string Type);

public sealed record CardField(
    [property: Description("The label, such as 'Balance'.")] string Label,
    [property: Description("The value as it should be shown, money with its currency, such as '1 200.00 SEK'.")] string Value);

public sealed record ChartSeries(
    [property: Description("The name in the legend, such as 'Spending'.")] string Name,
    [property: Description("One number per label, in label order.")] decimal[] Values);

/// <summary>A table or a card the model asked to show: the tool name the UI renders by, the stored kind, and its data.</summary>
public sealed record UiComponent(string Tool, string Kind, JsonElement Data);

/// <summary>
/// The UI tools of one run (phase 2 design plan, section 5, decision A8): show_table, show_card, show_chart,
/// show_diagram, show_form and request_document.
///
/// Generative UI with tools: the model cannot draw anything, it can only write text and call tools. So a table is a
/// tool call: the model calls show_table with the columns and rows, and the arguments are the table. This class checks
/// the arguments, keeps the component in a queue for the AG-UI endpoint to stream to the browser, and answers the model
/// so it can go on writing. The browser renders the tool call as a table, a chart, a form, and so on.
///
/// A form and a document request also wait for the person: they are kept in the session (<see cref="HostSession.Pending"/>)
/// until the person sends the form or the file. A new instance per run, so two runs never share what they show.
/// </summary>
public sealed class UiTools
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Queue<UiComponent> shown = new();
    private readonly HostSession session;

    public UiTools(HostSession session)
    {
        this.session = session;
        Tools =
        [
            AIFunctionFactory.Create(ShowTable, "show_table",
                "Shows a table to the person, for a list such as accounts, statement lines, installments or products. Every row has one cell per column, as display text."),
            AIFunctionFactory.Create(ShowCard, "show_card",
                "Shows one record to the person as a card with labelled fields, such as one account, one loan or a receipt."),
            AIFunctionFactory.Create(ShowChart, "show_chart",
                "Shows a line or bar chart, such as a balance over time or spending per month. One value per label in every series."),
            AIFunctionFactory.Create(ShowDiagram, "show_diagram",
                "Shows a diagram written in Mermaid, such as a timeline of installments or the steps of a process."),
            AIFunctionFactory.Create(ShowForm, "show_form",
                "Shows a form when you need exact values from the person, such as an amount, a date or a card number. The values come back in the next message; wait for them."),
            AIFunctionFactory.Create(RequestDocument, "request_document",
                "Asks the person to upload one document (PDF, PNG or JPEG, at most 5 MB) for one of their loan applications. The upload is confirmed in the next message."),
        ];
    }

    public IReadOnlyList<AITool> Tools { get; }

    /// <summary>A random, unguessable id for something the person answers (a form, a change, an upload).</summary>
    public static string NewId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Queues a component for the browser. Also used by <see cref="ConfirmFirst"/> for confirmation cards.</summary>
    public void Show(string tool, string kind, object data) =>
        shown.Enqueue(new UiComponent(tool, kind, JsonSerializer.SerializeToElement(data, Json)));

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

        Show("show_table", MessageKind.Table, new { title, columns, rows });
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

        Show("show_card", MessageKind.Card, new { title, fields });
        return "The card is shown to the person.";
    }

    private string ShowChart(
        string title,
        [Description("line or bar.")] string kind,
        [Description("The labels along the bottom, such as months.")] string[] labels,
        ChartSeries[] series,
        [Description("The unit of the values, such as SEK.")] string unit)
    {
        if (UiComponentCheck.Chart(kind, labels.Length, [.. series.Select(s => s.Values.Length)]) is { } problem)
        {
            return $"Not shown: {problem}";
        }

        Show("show_chart", MessageKind.Chart, new { title, kind, labels, series, unit });
        return "The chart is shown to the person.";
    }

    private string ShowDiagram(string title, [Description("The diagram in Mermaid syntax, such as 'flowchart LR' or 'timeline'.")] string mermaid)
    {
        if (UiComponentCheck.Diagram(mermaid) is { } problem)
        {
            return $"Not shown: {problem}";
        }

        Show("show_diagram", MessageKind.Diagram, new { title, mermaid });
        return "The diagram is shown to the person.";
    }

    private string ShowForm(
        string title,
        [Description("The fields. type is text, number, date or choice; a choice needs options; min and max limit a number.")] FormField[] fields,
        [Description("The text of the send button, such as 'Continue'.")] string submitLabel)
    {
        if (FormCheck.Definition(fields) is { } problem)
        {
            return $"Not shown: {problem}";
        }

        var form = new OpenForm(NewId(), title, fields);
        session.Pending[form.Id] = form;
        Show("show_form", MessageKind.Form, new { formId = form.Id, title, fields, submitLabel });
        return "The form is shown. Stop here and wait: the person's values come in the next message.";
    }

    private string RequestDocument(
        [Description("The id of the customer's loan application the document is for.")] Guid applicationId,
        [Description("What to upload, such as 'Your last three payslips'.")] string description)
    {
        var request = new DocumentRequest(NewId(), applicationId, description);
        session.Pending[request.Id] = request;
        Show("request_document", MessageKind.DocumentRequest, new { requestId = request.Id, applicationId, description });
        return "The upload box is shown. Stop here and wait: the upload is confirmed in the next message.";
    }
}
