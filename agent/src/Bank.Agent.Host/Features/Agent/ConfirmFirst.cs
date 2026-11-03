using System.Text.Json;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// Stands in front of a bank tool that changes data (decision A11: a person confirms every change, enforced by the agent
/// host, not by the model).
///
/// The model sees the real tool: same name, description and parameters. But when it calls it, nothing reaches the bank.
/// Instead this function keeps the call (tool and exact arguments) in the session, shows the person a confirmation card
/// with those values and Confirm / Cancel buttons, and tells the model to wait. Only the person's click on Confirm runs
/// the tool (<c>ActionEndpoints</c>), with the stored arguments. The model cannot press the button.
/// </summary>
public sealed class ConfirmFirst(AIFunction inner, HostSession session, UiTools ui) : DelegatingAIFunction(inner)
{
    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var values = JsonSerializer.SerializeToElement(arguments.ToDictionary(a => a.Key, a => a.Value), UiTools.Json);
        var change = new PendingChange(UiTools.NewId(), Name, ChangeSummary.Title(Name), values);
        session.Pending[change.Id] = change;

        var fields = ChangeSummary.Fields(values.EnumerateObject().Select(p => new KeyValuePair<string, string?>(p.Name, Display(p.Value))));
        ui.Show("show_confirmation", MessageKind.Confirmation, new { changeId = change.Id, title = change.Title, fields });
        return ValueTask.FromResult<object?>(
            "Not done yet. The person now sees a confirmation card with these values and the buttons Confirm and Cancel. " +
            "Tell them in one sentence to check the values and press Confirm. Do not call this tool again for the same change.");
    }

    private static string? Display(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        _ => value.GetRawText(),
    };
}
