using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests;

public sealed class BillingServiceApiSkeletonTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BillingServiceApiSkeletonTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact(Skip = "Integration skeleton only. Configure test container dependencies before enabling.")]
    public async Task HealthEndpoint_ShouldReturnSuccess_WhenApplicationIsRunning()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }
}
