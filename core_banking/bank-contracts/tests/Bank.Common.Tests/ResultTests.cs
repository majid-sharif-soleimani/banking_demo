namespace Bank.Common.Tests;

public class ResultTests
{
    [Fact]
    public void Success_carries_the_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Failure_carries_the_error_and_has_no_value()
    {
        Result<int> result = new Error("insufficient_funds", "Balance is too low.");

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("insufficient_funds");
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Success_has_no_error()
    {
        Result<int> result = 1;

        Should.Throw<InvalidOperationException>(() => result.Error);
    }
}
