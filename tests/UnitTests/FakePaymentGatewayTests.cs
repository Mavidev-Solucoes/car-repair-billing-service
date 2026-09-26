using Infrastructure.Payments;

namespace UnitTests;

public sealed class FakePaymentGatewayTests
{
    [Fact]
    public async Task ProcessAsync_ShouldReturnSuccess_WhenFailureIsNotSimulated()
    {
        var gateway = new FakePaymentGateway();

        var result = await gateway.ProcessAsync(Guid.NewGuid(), 120m, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("fake-success-", result.ExternalReference);
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnFailure_WhenFailureIsSimulated()
    {
        var gateway = new FakePaymentGateway();

        var result = await gateway.ProcessAsync(Guid.NewGuid(), 120m, true, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("fake-failure-", result.ExternalReference);
    }
}
