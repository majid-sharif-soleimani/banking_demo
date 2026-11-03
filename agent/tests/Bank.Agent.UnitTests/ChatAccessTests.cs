using Bank.Agent.Host.Logic;

namespace Bank.Agent.UnitTests;

public class ChatAccessTests
{
    private static readonly Principal Anna = new("IMP-C001", Role.Customer, Guid.NewGuid(), "Anna");
    private static readonly Principal Bo = new("IMP-C002", Role.Customer, Guid.NewGuid(), "Bo");
    private static readonly Principal Admin = new("admin", Role.Admin, null, "Admin");

    [Fact]
    public void A_customer_reads_their_own_chat() => ChatAccess.CanRead(Anna, new ChatFacts("IMP-C001", IsOpen: false)).ShouldBeTrue();

    [Fact]
    public void A_customer_never_reads_another_customers_chat() =>
        ChatAccess.CanRead(Bo, new ChatFacts("IMP-C001", IsOpen: true)).ShouldBeFalse();

    [Fact]
    public void A_customer_never_reads_an_admin_chat() => ChatAccess.CanRead(Anna, new ChatFacts("admin", IsOpen: true)).ShouldBeFalse();

    [Theory]
    [InlineData("IMP-C001", true)]
    [InlineData("IMP-C002", false)]
    [InlineData("admin", false)]
    public void The_admin_reads_every_chat(string owner, bool isOpen) => ChatAccess.CanRead(Admin, new ChatFacts(owner, isOpen)).ShouldBeTrue();

    [Fact]
    public void The_owner_writes_their_open_chat() => ChatAccess.CanWrite(Anna, new ChatFacts("IMP-C001", IsOpen: true)).ShouldBeTrue();

    [Fact]
    public void Nobody_writes_a_closed_chat()
    {
        ChatAccess.CanWrite(Anna, new ChatFacts("IMP-C001", IsOpen: false)).ShouldBeFalse();
        ChatAccess.CanWrite(Admin, new ChatFacts("admin", IsOpen: false)).ShouldBeFalse();
    }

    [Fact]
    public void The_admin_reads_but_never_writes_a_customers_chat() =>
        ChatAccess.CanWrite(Admin, new ChatFacts("IMP-C001", IsOpen: true)).ShouldBeFalse();

    [Fact]
    public void Usernames_are_compared_exactly() => ChatAccess.CanRead(Anna, new ChatFacts("imp-c001", IsOpen: true)).ShouldBeFalse();
}
