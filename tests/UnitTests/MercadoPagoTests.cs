using System.Net;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Payments;
using Application.Payments;
using Application.Payments.Commands;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests;

public sealed class MercadoPagoGatewayTests
{
    [Fact]
    public async Task Process_ShouldCreatePreferenceAndReturnPendingCheckout()
    {
        var handler = new StubHttpHandler(_ => Json(HttpStatusCode.Created,
            """{"id":"pref-123","init_point":"https://mp/checkout","sandbox_init_point":"https://sandbox.mp/checkout"}"""));
        var gateway = CreateGateway(handler, options => options.NotificationUrl = "https://kong/api/payments/webhooks/mercadopago");
        var paymentId = Guid.NewGuid();

        var result = await gateway.ProcessAsync(paymentId, 230.456m, simulateFailure: false, CancellationToken.None);

        Assert.Equal(PaymentGatewayStatus.Pending, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("pref-123", result.ExternalReference);
        Assert.Equal("https://sandbox.mp/checkout", result.CheckoutUrl);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.mercadopago.com/checkout/preferences", request.Uri);
        Assert.Equal("Bearer TEST-token", request.Authorization);
        Assert.Equal(paymentId.ToString("D"), request.IdempotencyKey);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(paymentId.ToString("D"), body.RootElement.GetProperty("external_reference").GetString());
        Assert.Equal("https://kong/api/payments/webhooks/mercadopago", body.RootElement.GetProperty("notification_url").GetString());
        var item = body.RootElement.GetProperty("items")[0];
        Assert.Equal(230.46m, item.GetProperty("unit_price").GetDecimal());
        Assert.Equal("BRL", item.GetProperty("currency_id").GetString());
    }

    [Fact]
    public async Task Process_WithoutSandbox_ShouldUseInitPointAndOmitNotificationUrl()
    {
        var handler = new StubHttpHandler(_ => Json(HttpStatusCode.Created,
            """{"id":"pref-1","init_point":"https://mp/checkout","sandbox_init_point":"https://sandbox.mp/checkout"}"""));
        var gateway = CreateGateway(handler, options => options.UseSandboxCheckout = false);

        var result = await gateway.ProcessAsync(Guid.NewGuid(), 10m, false, CancellationToken.None);

        Assert.Equal("https://mp/checkout", result.CheckoutUrl);
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.False(body.RootElement.TryGetProperty("notification_url", out _));
    }

    [Fact]
    public async Task Process_WithSimulatedFailure_ShouldRejectWithoutCallingMercadoPago()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("não deveria chamar"));
        var gateway = CreateGateway(handler);

        var result = await gateway.ProcessAsync(Guid.NewGuid(), 10m, simulateFailure: true, CancellationToken.None);

        Assert.Equal(PaymentGatewayStatus.Rejected, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Process_WhenMercadoPagoFails_ShouldThrowForRetry()
    {
        var gateway = CreateGateway(new StubHttpHandler(_ => Json(HttpStatusCode.BadRequest, """{"message":"invalid"}""")));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            gateway.ProcessAsync(Guid.NewGuid(), 10m, false, CancellationToken.None));
        Assert.Contains("400", exception.Message);
    }

    [Fact]
    public async Task AnyCall_WithoutAccessToken_ShouldFail()
    {
        var gateway = CreateGateway(new StubHttpHandler(_ => Json(HttpStatusCode.OK, "{}")), options => options.AccessToken = "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.FindAsync("1", CancellationToken.None));
    }

    [Theory]
    [InlineData("approved", PaymentGatewayStatus.Approved)]
    [InlineData("rejected", PaymentGatewayStatus.Rejected)]
    [InlineData("cancelled", PaymentGatewayStatus.Rejected)]
    [InlineData("pending", PaymentGatewayStatus.Pending)]
    [InlineData("in_process", PaymentGatewayStatus.Pending)]
    [InlineData(null, PaymentGatewayStatus.Pending)]
    public void MapStatus_ShouldTranslateMercadoPagoStatuses(string? status, PaymentGatewayStatus expected)
    {
        Assert.Equal(expected, MercadoPagoPaymentGateway.MapStatus(status));
    }

    [Fact]
    public async Task Find_ShouldReadPaymentAndExternalReference()
    {
        var paymentId = Guid.NewGuid();
        var handler = new StubHttpHandler(_ => Json(HttpStatusCode.OK,
            $$"""{"id":987654,"status":"approved","status_detail":"accredited","external_reference":"{{paymentId}}"}"""));
        var gateway = CreateGateway(handler);

        var status = await gateway.FindAsync("987654", CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal("987654", status.GatewayPaymentId);
        Assert.Equal(paymentId, status.PaymentId);
        Assert.Equal(PaymentGatewayStatus.Approved, status.Status);
        Assert.Contains("accredited", status.Message);
        Assert.Equal("https://api.mercadopago.com/v1/payments/987654", handler.Requests[0].Uri);
    }

    [Fact]
    public async Task Find_ShouldReturnNullForUnknownOrForeignPayments()
    {
        var notFound = CreateGateway(new StubHttpHandler(_ => Json(HttpStatusCode.NotFound, "{}")));
        var foreign = CreateGateway(new StubHttpHandler(_ => Json(HttpStatusCode.OK,
            """{"id":1,"status":"approved","external_reference":"pedido-de-outro-sistema"}""")));

        Assert.Null(await notFound.FindAsync("1", CancellationToken.None));
        Assert.Null(await foreign.FindAsync("1", CancellationToken.None));
    }

    [Fact]
    public async Task FindByPaymentId_ShouldSearchByExternalReference()
    {
        var paymentId = Guid.NewGuid();
        var handler = new StubHttpHandler(_ => Json(HttpStatusCode.OK,
            $$"""{"results":[{"id":2,"status":"rejected","status_detail":"cc_rejected_other_reason","external_reference":"{{paymentId}}"}]}"""));
        var gateway = CreateGateway(handler);

        var status = await gateway.FindByPaymentIdAsync(paymentId, CancellationToken.None);
        var empty = await CreateGateway(new StubHttpHandler(_ => Json(HttpStatusCode.OK, """{"results":[]}""")))
            .FindByPaymentIdAsync(paymentId, CancellationToken.None);

        Assert.Equal(PaymentGatewayStatus.Rejected, status!.Status);
        Assert.Contains($"external_reference={paymentId}", handler.Requests[0].Uri);
        Assert.Null(empty);
    }

    [Fact]
    public async Task Refund_ShouldCallRefundApiWithIdempotencyKey()
    {
        var paymentId = Guid.NewGuid();
        var handler = new StubHttpHandler(_ => Json(HttpStatusCode.Created, """{"id":5555,"payment_id":987654,"status":"approved"}"""));
        var gateway = CreateGateway(handler);

        var result = await gateway.RefundAsync(paymentId, "987654", 230m, CancellationToken.None);
        var failed = await CreateGateway(new StubHttpHandler(_ => Json(HttpStatusCode.BadRequest, "{}")))
            .RefundAsync(paymentId, "987654", 230m, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("5555", result.ExternalReference);
        Assert.Equal("https://api.mercadopago.com/v1/payments/987654/refunds", handler.Requests[0].Uri);
        Assert.Equal($"{paymentId:D}-refund", handler.Requests[0].IdempotencyKey);
        Assert.Equal(PaymentGatewayStatus.Rejected, failed.Status);
    }

    private static MercadoPagoPaymentGateway CreateGateway(StubHttpHandler handler, Action<MercadoPagoOptions>? configure = null)
    {
        var options = new MercadoPagoOptions { AccessToken = "TEST-token" };
        configure?.Invoke(options);
        return new MercadoPagoPaymentGateway(new HttpClient(handler), Options.Create(options), NullLogger<MercadoPagoPaymentGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

public sealed class MercadoPagoWebhookSignatureTests
{
    private const string Secret = "segredo-do-webhook";

    [Fact]
    public void IsValid_ShouldAcceptSignatureBuiltFromManifest()
    {
        var v1 = MercadoPagoWebhookSignature.Compute("id:abc123;request-id:req-1;ts:1700000000;", Secret);

        Assert.True(MercadoPagoWebhookSignature.IsValid($"ts=1700000000,v1={v1}", "req-1", "ABC123", Secret));
    }

    [Fact]
    public void IsValid_ShouldWorkWithoutRequestIdAndDataId()
    {
        var v1 = MercadoPagoWebhookSignature.Compute("ts:1700000000;", Secret);

        Assert.True(MercadoPagoWebhookSignature.IsValid($"ts=1700000000, v1={v1}", null, null, Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ts=1700000000")]
    [InlineData("v1=abc")]
    [InlineData("lixo,ts,=")]
    [InlineData("ts=1700000000,v1=0000")]
    public void IsValid_ShouldRejectMissingOrWrongSignatures(string? header)
    {
        Assert.False(MercadoPagoWebhookSignature.IsValid(header, "req-1", "123", Secret));
    }

    [Fact]
    public void IsValid_ShouldRejectWhenSecretIsMissing()
    {
        Assert.False(MercadoPagoWebhookSignature.IsValid("ts=1,v1=abc", "req", "1", ""));
    }
}

public sealed class PaymentCheckoutTests
{
    [Fact]
    public void AwaitCheckout_ShouldKeepPaymentPendingWithCheckoutLink()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 100m);

        PaymentGatewayOutcome.Apply(payment, PaymentGatewayResult.Pending(" pref-1 ", " https://checkout ", "aguardando"));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("pref-1", payment.CheckoutReference);
        Assert.Equal("https://checkout", payment.CheckoutUrl);
        Assert.Empty(payment.Transactions);
    }

    [Fact]
    public void AwaitCheckout_ShouldValidateInput()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 100m);

        Assert.Throws<ArgumentException>(() => payment.AwaitCheckout("", "https://checkout"));
        Assert.Throws<ArgumentException>(() => payment.AwaitCheckout("pref", " "));
        Assert.Throws<InvalidOperationException>(() =>
            PaymentGatewayOutcome.Apply(payment, new PaymentGatewayResult(PaymentGatewayStatus.Pending, "pref", "sem link")));

        payment.Reject("recusado");
        Assert.Throws<InvalidOperationException>(() => payment.AwaitCheckout("pref", "https://checkout"));
    }

    [Fact]
    public void Confirm_ShouldOnlyDecidePendingPaymentsOnce()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 100m);
        var pending = new GatewayPaymentStatus("1", payment.Id, PaymentGatewayStatus.Pending, "ainda não pagou");
        var approved = new GatewayPaymentStatus("1", payment.Id, PaymentGatewayStatus.Approved, "pago");
        var rejected = new GatewayPaymentStatus("1", payment.Id, PaymentGatewayStatus.Rejected, "recusado");

        Assert.False(PaymentGatewayOutcome.Confirm(payment, pending));
        Assert.True(PaymentGatewayOutcome.Confirm(payment, approved));
        Assert.False(PaymentGatewayOutcome.Confirm(payment, rejected));

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        var transaction = Assert.Single(payment.Transactions);
        Assert.Equal("1", transaction.ExternalReference);
    }
}

public sealed class ConfirmGatewayPaymentCommandTests
{
    [Fact]
    public async Task Handle_ShouldApprovePaymentFromWebhook()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 100m);
        var gateway = new StubPaymentGateway(PaymentGatewayResult.Approved("x", "x"))
        {
            GatewayStatus = new GatewayPaymentStatus("777", payment.Id, PaymentGatewayStatus.Approved, "pago")
        };
        var unitOfWork = new NoOpUnitOfWork();
        var handler = CreateHandler(gateway, new InMemoryPaymentRepository(payment), unitOfWork);

        var first = await handler.Handle(new ConfirmGatewayPaymentCommand("777", null), CancellationToken.None);
        var repeated = await handler.Handle(new ConfirmGatewayPaymentCommand("777", null), CancellationToken.None);

        Assert.Equal(ConfirmGatewayPaymentResult.Updated, first);
        Assert.Equal(ConfirmGatewayPaymentResult.Unchanged, repeated);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
        Assert.Equal(["777", "777"], gateway.Lookups);
    }

    [Fact]
    public async Task Handle_ShouldRejectPaymentOnManualSync()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 100m);
        var gateway = new StubPaymentGateway(PaymentGatewayResult.Approved("x", "x"))
        {
            GatewayStatus = new GatewayPaymentStatus("778", payment.Id, PaymentGatewayStatus.Rejected, "cartão recusado")
        };
        var handler = CreateHandler(gateway, new InMemoryPaymentRepository(payment), new NoOpUnitOfWork());

        var result = await handler.Handle(new ConfirmGatewayPaymentCommand(null, payment.Id), CancellationToken.None);

        Assert.Equal(ConfirmGatewayPaymentResult.Updated, result);
        Assert.Equal(PaymentStatus.Rejected, payment.Status);
        Assert.Equal("cartão recusado", payment.RejectionReason);
        Assert.Equal([payment.Id.ToString("D")], gateway.Lookups);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundForUnknownOrMismatchedPayments()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 100m);
        var unknownAtGateway = CreateHandler(new StubPaymentGateway(PaymentGatewayResult.Approved("x", "x")),
            new InMemoryPaymentRepository(payment), new NoOpUnitOfWork());
        var otherPayment = new StubPaymentGateway(PaymentGatewayResult.Approved("x", "x"))
        {
            GatewayStatus = new GatewayPaymentStatus("9", Guid.NewGuid(), PaymentGatewayStatus.Approved, "pago")
        };

        Assert.Equal(ConfirmGatewayPaymentResult.NotFound,
            await unknownAtGateway.Handle(new ConfirmGatewayPaymentCommand("1", null), CancellationToken.None));
        Assert.Equal(ConfirmGatewayPaymentResult.NotFound,
            await CreateHandler(otherPayment, new InMemoryPaymentRepository(payment), new NoOpUnitOfWork())
                .Handle(new ConfirmGatewayPaymentCommand(null, payment.Id), CancellationToken.None));
        Assert.Equal(ConfirmGatewayPaymentResult.NotFound,
            await CreateHandler(otherPayment, new InMemoryPaymentRepository(), new NoOpUnitOfWork())
                .Handle(new ConfirmGatewayPaymentCommand("9", null), CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldRequireOneIdentifier()
    {
        var validator = new ConfirmGatewayPaymentCommandValidator();

        Assert.False(validator.Validate(new ConfirmGatewayPaymentCommand(" ", null)).IsValid);
        Assert.False(validator.Validate(new ConfirmGatewayPaymentCommand(null, Guid.Empty)).IsValid);
        Assert.True(validator.Validate(new ConfirmGatewayPaymentCommand("1", null)).IsValid);
        Assert.True(validator.Validate(new ConfirmGatewayPaymentCommand(null, Guid.NewGuid())).IsValid);
    }

    private static ConfirmGatewayPaymentCommandHandler CreateHandler(
        StubPaymentGateway gateway,
        InMemoryPaymentRepository repository,
        NoOpUnitOfWork unitOfWork) =>
        new(gateway, repository, unitOfWork, NullLogger<ConfirmGatewayPaymentCommandHandler>.Instance);
}

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var response = respond(request);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!.ToString(),
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-Idempotency-Key", out var keys) ? keys.Single() : null,
            body));
        return response;
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string? IdempotencyKey, string? Body);
