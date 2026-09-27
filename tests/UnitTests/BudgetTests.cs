using Domain.Entities;
using Domain.Enums;

namespace UnitTests;

public sealed class BudgetTests
{
    [Fact]
    public void Approve_ShouldSetStatusAndApprovedDate()
    {
        var budget = new Budget("Customer", [new BudgetItem("Part", 100m, 1)]);

        budget.Approve();

        Assert.Equal(BudgetStatus.Approved, budget.Status);
        Assert.NotNull(budget.ApprovedAtUtc);
    }

    [Fact]
    public void Reject_AfterApprove_ShouldThrowInvalidOperationException()
    {
        var budget = new Budget("Customer", [new BudgetItem("Part", 100m, 1)]);
        budget.Approve();

        Assert.Throws<InvalidOperationException>(() => budget.Reject("not valid"));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenItemsAreEmpty()
    {
        Assert.Throws<ArgumentException>(() => new Budget("Customer", []));
    }

    [Fact]
    public void Reject_ShouldThrow_WhenReasonIsBlank()
    {
        var budget = new Budget("Customer", [new BudgetItem("Part", 100m, 1)]);

        Assert.Throws<ArgumentException>(() => budget.Reject("   "));
    }
}
