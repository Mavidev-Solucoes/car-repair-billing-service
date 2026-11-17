using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Contracts;
using Application.Payments.Queries;
using Domain.Enums;
using Infrastructure.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

/// <summary>
/// Fluxo do Mercado Pago pela API, com Postgres e RabbitMQ reais e a API do Mercado Pago simulada:
/// cobrança vira checkout pendente, o webhook (ou a sincronização) aprova ou recusa, e o evento sai para a saga.
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class MercadoPagoFlowTests(BillingFixture fixture)
{
    private const string WebhookSecret = "segredo-de-teste";

    [Fact]
    public async Task Checkout_ThenWebhook_ShouldApprovePaymentAndPublishEvent()
    {
        var mercadoPago = new FakeMercadoPago();
        await using var factory = CreateFactory(mercadoPago, webhookSecret: null);
        using var client = factory.CreateClient();
        using var probe = new EventsProbe(fixture);
        var serviceOrderId = Guid.NewGuid();
        var (budgetId, paymentId) = await CreatePendingPaymentAsync(client, serviceOrderId);

        var pending = await client.GetFromJsonAsync<PaymentResponse>($"/api/payments/{paymentId}");
        Assert.Equal(PaymentStatus.Pending, pending!.Status);
        Assert.Equal($"https://sandbox.mercadopago.test/checkout/{paymentId:N}", pending.CheckoutUrl);
        var listed = await client.GetFromJsonAsync<List<PaymentSummaryResponse>>("/api/payments");
        Assert.Equal(pending.CheckoutUrl, Assert.Single(listed!, payment => payment.Id == paymentId).CheckoutUrl);

        mercadoPago.Payments["900001"] = ("approved", paymentId);
        var webhook = await client.PostAsJsonAsync("/api/payments/webhooks/mercadopago",
            new { type = "payment", action = "payment.updated", data = new { id = "900001" } });
        var repeated = await client.PostAsync("/api/payments/webhooks/mercadopago?type=payment&data.id=900001", null);
        var ignored = await client.PostAsJsonAsync("/api/payments/webhooks/mercadopago", new { type = "merchant_order", data = new { id = "1" } });

        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        Assert.Contains("Updated", await webhook.Content.ReadAsStringAsync());
        Assert.Contains("Unchanged", await repeated.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);

        var approved = await client.GetFromJsonAsync<PaymentResponse>($"/api/payments/{paymentId}");
        Assert.Equal(PaymentStatus.Approved, approved!.Status);
        Assert.Contains(approved.Transactions, transaction => transaction.ExternalReference == "900001" && transaction.IsSuccess);

        var paymentApproved = await probe.WaitForAsync("PaymentApproved", serviceOrderId.ToString("D"));
        Assert.Equal(budgetId, paymentApproved.Payload!["budgetId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task ManualSync_ShouldRejectPaymentWhenCheckoutWasDeclined()
    {
        var mercadoPago = new FakeMercadoPago();
        await using var factory = CreateFactory(mercadoPago, webhookSecret: null);
        using var client = factory.CreateClient();
        var (_, paymentId) = await CreatePendingPaymentAsync(client, Guid.NewGuid());

        var beforePaying = await client.PostAsync($"/api/payments/{paymentId}/sync", null);
        mercadoPago.Payments["900002"] = ("rejected", paymentId);
        var afterPaying = await client.PostAsync($"/api/payments/{paymentId}/sync", null);

        Assert.Equal(HttpStatusCode.NotFound, beforePaying.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterPaying.StatusCode);
        var rejected = await afterPaying.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.Equal(PaymentStatus.Rejected, rejected!.Status);
    }

    [Fact]
    public async Task Webhook_WithSecret_ShouldRequireValidSignature()
    {
        var mercadoPago = new FakeMercadoPago();
        await using var factory = CreateFactory(mercadoPago, WebhookSecret);
        using var client = factory.CreateClient();
        var (_, paymentId) = await CreatePendingPaymentAsync(client, Guid.NewGuid());
        mercadoPago.Payments["900003"] = ("approved", paymentId);

        using var unsigned = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/mercadopago?type=payment&data.id=900003");
        using var signed = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/mercadopago?type=payment&data.id=900003");
        var v1 = MercadoPagoWebhookSignature.Compute("id:900003;request-id:req-42;ts:1700000000;", WebhookSecret);
        signed.Headers.Add("x-signature", $"ts=1700000000,v1={v1}");
        signed.Headers.Add("x-request-id", "req-42");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(unsigned)).StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await client.GetFromJsonAsync<PaymentResponse>($"/api/payments/{paymentId}"))!.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(signed)).StatusCode);
        Assert.Equal(PaymentStatus.Approved, (await client.GetFromJsonAsync<PaymentResponse>($"/api/payments/{paymentId}"))!.Status);
    }

    private WebApplicationFactory<Program> CreateFactory(FakeMercadoPago mercadoPago, string? webhookSecret) =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PaymentGateway:Provider", "MercadoPago");
            builder.UseSetting("MercadoPago:AccessToken", "TEST-token");
            builder.UseSetting("MercadoPago:BaseUrl", "https://api.mercadopago.test");
            builder.UseSetting("MercadoPago:WebhookSecret", webhookSecret ?? string.Empty);
            builder.ConfigureServices(services =>
                services.AddHttpClient(MercadoPagoPaymentGateway.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => mercadoPago));
        });

    private static async Task<(Guid BudgetId, Guid PaymentId)> CreatePendingPaymentAsync(HttpClient client, Guid serviceOrderId)
    {
        var budget = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest(
            serviceOrderId, "Maria Silva", [new CreateBudgetItemRequest("Revisão", 300m, 1)]));
        var budgetId = await ReadIdAsync(budget);
        (await client.PostAsync($"/api/budgets/{budgetId}/approve", null)).EnsureSuccessStatusCode();

        var payment = await client.PostAsJsonAsync("/api/payments", new CreatePaymentRequest(budgetId, 300m));
        var paymentId = await ReadIdAsync(payment);
        var charge = await client.PostAsJsonAsync($"/api/payments/{paymentId}/approve", new ApprovePaymentRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, charge.StatusCode);
        return (budgetId, paymentId);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>API do Mercado Pago em memória: preferências, consulta e busca de pagamentos.</summary>
    private sealed class FakeMercadoPago : HttpMessageHandler
    {
        public ConcurrentDictionary<string, (string Status, Guid PaymentId)> Payments { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/checkout/preferences")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var reference = Guid.Parse(body.RootElement.GetProperty("external_reference").GetString()!);
                return Json(HttpStatusCode.Created, new
                {
                    id = $"pref-{reference:N}",
                    init_point = $"https://www.mercadopago.test/checkout/{reference:N}",
                    sandbox_init_point = $"https://sandbox.mercadopago.test/checkout/{reference:N}"
                });
            }

            if (request.Method == HttpMethod.Get && path == "/v1/payments/search")
            {
                var reference = Guid.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["external_reference"]!);
                var results = Payments.Where(item => item.Value.PaymentId == reference)
                    .Select(item => ToPayment(item.Key, item.Value)).ToArray();
                return Json(HttpStatusCode.OK, new { results });
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/v1/payments/", StringComparison.Ordinal))
            {
                var id = path["/v1/payments/".Length..];
                return Payments.TryGetValue(id, out var payment)
                    ? Json(HttpStatusCode.OK, ToPayment(id, payment))
                    : Json(HttpStatusCode.NotFound, new { message = "not found" });
            }

            return Json(HttpStatusCode.NotFound, new { message = $"rota não simulada: {request.Method} {path}" });
        }

        private static object ToPayment(string id, (string Status, Guid PaymentId) payment) => new
        {
            id = long.Parse(id),
            status = payment.Status,
            status_detail = payment.Status == "approved" ? "accredited" : "cc_rejected_other_reason",
            external_reference = payment.PaymentId.ToString("D")
        };

        private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }
}
