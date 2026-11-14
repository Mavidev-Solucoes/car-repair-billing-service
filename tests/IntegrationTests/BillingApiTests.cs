using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Contracts;
using Application.Budgets.Queries;
using Application.Payments.Queries;
using Domain.Enums;

namespace IntegrationTests;

[Collection(BillingCollection.Name)]
public sealed class BillingApiTests(BillingFixture fixture)
{
    [Fact]
    public async Task BudgetLifecycle_ShouldPersistAndPublishEvents()
    {
        using var probe = new EventsProbe(fixture);
        using var client = fixture.Factory.CreateClient();
        var serviceOrderId = Guid.NewGuid();

        var budgetId = await CreateBudgetAsync(client, serviceOrderId);

        var budget = await client.GetFromJsonAsync<BudgetResponse>($"/api/budgets/{budgetId}");
        Assert.NotNull(budget);
        Assert.Equal(serviceOrderId, budget.ServiceOrderId);
        Assert.Equal(BudgetStatus.Pending, budget.Status);
        Assert.Equal(230m, budget.TotalAmount);
        Assert.Equal(2, budget.Items.Count);
        Assert.Contains(budget.Items, item => item.Description == "Filtro" && item.Total == 80m);

        var list = await client.GetFromJsonAsync<List<BudgetSummaryResponse>>("/budgets");
        Assert.Contains(list!, item => item.Id == budgetId);

        var approve = await client.PostAsJsonAsync($"/api/budgets/{budgetId}/approve", new ApproveBudgetRequest(SimulatePaymentFailure: true));
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        var approvedAgain = await client.PostAsync($"/api/budgets/{budgetId}/approve", null);
        Assert.Equal(HttpStatusCode.BadRequest, approvedAgain.StatusCode);

        var created = await probe.WaitForAsync("BudgetCreated", serviceOrderId.ToString("D"));
        var approved = await probe.WaitForAsync("BudgetApproved", serviceOrderId.ToString("D"));
        Assert.Equal(budgetId, created.Payload!["budgetId"]!.GetValue<Guid>());
        Assert.True(approved.Payload!["simulatePaymentFailure"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RejectBudget_ShouldStoreReason()
    {
        using var client = fixture.Factory.CreateClient();
        var budgetId = await CreateBudgetAsync(client, Guid.NewGuid());

        var reject = await client.PostAsJsonAsync($"/budgets/{budgetId}/reject", new RejectBudgetRequest("Muito caro"));
        var budget = await client.GetFromJsonAsync<BudgetResponse>($"/budgets/{budgetId}");

        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);
        Assert.Equal(BudgetStatus.Rejected, budget!.Status);
        Assert.Equal("Muito caro", budget.RejectionReason);
        Assert.NotNull(budget.RejectedAtUtc);
    }

    [Fact]
    public async Task InvalidRequests_ShouldReturnProblemResponses()
    {
        using var client = fixture.Factory.CreateClient();

        var noItems = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest(Guid.NewGuid(), "Maria", []));
        var unknownBudget = await client.GetAsync($"/api/budgets/{Guid.NewGuid()}");
        var unknownPayment = await client.GetAsync($"/api/payments/{Guid.NewGuid()}");
        var emptyReason = await client.PostAsJsonAsync($"/api/budgets/{Guid.NewGuid()}/reject", new RejectBudgetRequest(""));
        var invalidAmount = await client.PostAsJsonAsync("/api/payments", new CreatePaymentRequest(Guid.NewGuid(), 0));

        Assert.Equal(HttpStatusCode.BadRequest, noItems.StatusCode);
        Assert.Contains("Validation failed.", await noItems.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknownBudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownPayment.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyReason.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidAmount.StatusCode);
    }

    [Fact]
    public async Task Payments_ShouldRespectBudgetBalanceAndGatewayResult()
    {
        using var probe = new EventsProbe(fixture);
        using var client = fixture.Factory.CreateClient();
        var serviceOrderId = Guid.NewGuid();
        var budgetId = await CreateBudgetAsync(client, serviceOrderId);

        var beforeApproval = await client.PostAsJsonAsync("/api/payments", new CreatePaymentRequest(budgetId, 100m));
        Assert.Equal(HttpStatusCode.BadRequest, beforeApproval.StatusCode);

        await client.PostAsync($"/api/budgets/{budgetId}/approve", null);

        var first = await CreatePaymentAsync(client, budgetId, 100m);
        var second = await CreatePaymentAsync(client, budgetId, 80m);
        var third = await CreatePaymentAsync(client, budgetId, 50m);
        var exceeding = await client.PostAsJsonAsync("/api/payments", new CreatePaymentRequest(budgetId, 1m));
        Assert.Equal(HttpStatusCode.BadRequest, exceeding.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/payments/{first}/approve", new ApprovePaymentRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/payments/{second}/approve", new ApprovePaymentRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/payments/{third}/reject", new RejectPaymentRequest("Cartão bloqueado"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/payments/{first}/approve", new ApprovePaymentRequest(false))).StatusCode);

        var approved = await client.GetFromJsonAsync<PaymentResponse>($"/api/payments/{first}");
        var failed = await client.GetFromJsonAsync<PaymentResponse>($"/api/payments/{second}");
        var rejected = await client.GetFromJsonAsync<PaymentResponse>($"/api/payments/{third}");
        var list = await client.GetFromJsonAsync<List<PaymentSummaryResponse>>("/payments");

        Assert.Equal(PaymentStatus.Approved, approved!.Status);
        Assert.Single(approved.Transactions, transaction => transaction.IsSuccess);
        Assert.Equal(PaymentStatus.Rejected, failed!.Status);
        Assert.Equal(PaymentStatus.Rejected, rejected!.Status);
        Assert.Equal("Cartão bloqueado", rejected.RejectionReason);
        Assert.Equal(3, list!.Count(item => item.BudgetId == budgetId));

        var approvedEvent = await probe.WaitForAsync("PaymentApproved", serviceOrderId.ToString("D"));
        Assert.Equal(100m, approvedEvent.Payload!["amount"]!.GetValue<decimal>());
        await probe.WaitForAsync("PaymentRejected", serviceOrderId.ToString("D"));
    }

    private static async Task<Guid> CreateBudgetAsync(HttpClient client, Guid serviceOrderId)
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest(
            serviceOrderId,
            "Maria Silva",
            [new CreateBudgetItemRequest("Troca de óleo", 150m, 1), new CreateBudgetItemRequest("Filtro", 40m, 2)]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadIdAsync(response);
    }

    private static async Task<Guid> CreatePaymentAsync(HttpClient client, Guid budgetId, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/payments", new CreatePaymentRequest(budgetId, amount));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadIdAsync(response);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
