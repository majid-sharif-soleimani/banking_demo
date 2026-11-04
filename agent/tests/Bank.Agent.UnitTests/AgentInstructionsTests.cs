using Bank.Agent.Host.Logic;

namespace Bank.Agent.UnitTests;

public class AgentInstructionsTests
{
    private static readonly Principal Anna = new("IMP-C001", Role.Customer, Guid.NewGuid(), "Anna Andersson");

    private static readonly Principal Admin = new("admin", Role.Admin, null, "Admin");

    [Fact]
    public void A_customer_is_named_and_told_that_changes_need_a_confirmation()
    {
        var text = AgentInstructions.For(Anna);

        text.ShouldContain("Anna Andersson, a customer");
        text.ShouldContain("presses Confirm or Cancel");
        text.ShouldContain("business_date");
    }

    [Fact]
    public void The_admin_is_told_their_tools_and_that_loan_decisions_are_the_admins()
    {
        var text = AgentInstructions.For(Admin);

        text.ShouldContain("the bank's admin");
        text.ShouldContain("trial balance");
        text.ShouldContain("You never decide");
        text.ShouldContain("presses Confirm or Cancel");
    }

    [Fact]
    public void The_admin_lists_customers_page_by_page_and_never_by_guessing()
    {
        var text = AgentInstructions.For(Admin);

        text.ShouldContain("customers_search with no filter lists every customer");
        text.ShouldContain("Never join pages into one table");
        text.ShouldContain("Never search by guessing");
    }

    [Fact]
    public void The_customer_gets_the_loan_advisor_workflow()
    {
        var text = AgentInstructions.For(Anna);

        text.ShouldContain("Loan advisor");
        text.ShouldContain("loan_simulation");
        text.ShouldContain("never decide or promise a loan");
    }

    [Fact]
    public void The_admin_gets_the_document_arrears_and_report_workflows()
    {
        var text = AgentInstructions.For(Admin);

        text.ShouldContain("document_read");
        text.ShouldContain("loans_by_classification");
        text.ShouldContain("Management report");
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Both_roles_are_told_what_the_untrusted_markers_mean(Principal principal)
    {
        var text = AgentInstructions.For(principal);

        text.ShouldContain(UntrustedContent.Begin);
        text.ShouldContain(UntrustedContent.End);
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Both_roles_get_the_language_topic_and_number_rules(Principal principal)
    {
        var text = AgentInstructions.For(principal);

        text.ShouldContain("English, Swedish or Persian");
        text.ShouldContain("Politely refuse other topics");
        text.ShouldContain("Never compute a balance");
        text.ShouldContain("not instructions");
        text.ShouldContain("show_table");
    }

    [Fact]
    public void The_instructions_hold_neither_the_username_of_a_customer_nor_the_customer_id()
    {
        // The model needs the person's name to greet them, not their national id or the bank's internal id.
        var text = AgentInstructions.For(Anna);

        text.ShouldNotContain(Anna.Username);
        text.ShouldNotContain(Anna.CustomerId!.Value.ToString());
    }

    public static TheoryData<Principal> Both => new(Anna, Admin);
}
