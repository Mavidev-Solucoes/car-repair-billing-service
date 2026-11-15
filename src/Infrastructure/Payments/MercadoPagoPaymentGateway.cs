using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Payments;

/// <summary>
/// Integração com o Mercado Pago via Checkout Pro:
/// 1. ProcessAsync cria uma preferência com external_reference = id do Payment e devolve Pending com o link de checkout;
/// 2. o cliente paga no checkout (no sandbox, com cartão de teste);
/// 3. o Mercado Pago chama o webhook, e FindAsync consulta o pagamento para aprovar ou recusar o Payment.
/// O estorno da compensação usa a API de refunds sobre o id do pagamento aprovado.
/// </summary>
public sealed class MercadoPagoPaymentGateway(
    HttpClient httpClient,
    IOptions<MercadoPagoOptions> options,
    ILogger<MercadoPagoPaymentGateway> logger) : IPaymentGateway
{
    public const string HttpClientName = "MercadoPago";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MercadoPagoOptions _options = options.Value;

    public async Task<PaymentGatewayResult> ProcessAsync(
        Guid paymentId,
        decimal amount,
        bool simulateFailure,
        CancellationToken cancellationToken)
    {
        if (simulateFailure)
        {
            // Demonstração da compensação sem depender do checkout: recusa sem chamar o Mercado Pago.
            return PaymentGatewayResult.Rejected($"simulated-{paymentId:N}", "Simulated payment failure (Mercado Pago not called).");
        }

        var preference = new PreferenceRequest(
            [new PreferenceItem(_options.ItemTitle, 1, "BRL", decimal.Round(amount, 2))],
            paymentId.ToString("D"),
            string.IsNullOrWhiteSpace(_options.NotificationUrl) ? null : _options.NotificationUrl);

        using var request = CreateRequest(HttpMethod.Post, "/checkout/preferences");
        request.Headers.Add("X-Idempotency-Key", paymentId.ToString("D"));
        request.Content = JsonContent.Create(preference, options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            // Falha ao criar o checkout é transitória para a saga: a mensagem volta para retry.
            throw new HttpRequestException(
                $"Mercado Pago rejected the preference for payment {paymentId}: {(int)response.StatusCode} {error}");
        }

        var created = await response.Content.ReadFromJsonAsync<PreferenceResponse>(JsonOptions, cancellationToken)
            ?? throw new HttpRequestException("Mercado Pago returned an empty preference.");
        var checkoutUrl = _options.UseSandboxCheckout && !string.IsNullOrWhiteSpace(created.SandboxInitPoint)
            ? created.SandboxInitPoint
            : created.InitPoint;

        logger.LogInformation("Mercado Pago preference {PreferenceId} created for payment {PaymentId}.", created.Id, paymentId);
        return PaymentGatewayResult.Pending(created.Id, checkoutUrl!, "Waiting for the customer to pay at Mercado Pago checkout.");
    }

    public async Task<PaymentGatewayResult> RefundAsync(
        Guid paymentId,
        string externalReference,
        decimal amount,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/v1/payments/{Uri.EscapeDataString(externalReference)}/refunds");
        request.Headers.Add("X-Idempotency-Key", $"{paymentId:D}-refund");
        request.Content = JsonContent.Create(new { }, options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return PaymentGatewayResult.Rejected(externalReference, $"Mercado Pago refund failed: {(int)response.StatusCode} {error}");
        }

        var refund = await response.Content.ReadFromJsonAsync<RefundResponse>(JsonOptions, cancellationToken);
        return PaymentGatewayResult.Approved(
            refund?.Id.ToString(CultureInfo.InvariantCulture) ?? externalReference,
            FormattableString.Invariant($"Refunded {amount:0.00} at Mercado Pago (payment {externalReference})."));
    }

    public async Task<GatewayPaymentStatus?> FindAsync(string gatewayPaymentId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/v1/payments/{Uri.EscapeDataString(gatewayPaymentId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payment = await response.Content.ReadFromJsonAsync<MercadoPagoPayment>(JsonOptions, cancellationToken);
        return payment is null ? null : ToStatus(payment);
    }

    public async Task<GatewayPaymentStatus?> FindByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var query = $"/v1/payments/search?external_reference={paymentId:D}&sort=date_created&criteria=desc";
        using var request = CreateRequest(HttpMethod.Get, query);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var search = await response.Content.ReadFromJsonAsync<PaymentSearchResponse>(JsonOptions, cancellationToken);
        var latest = search?.Results?.FirstOrDefault();
        return latest is null ? null : ToStatus(latest);
    }

    public static PaymentGatewayStatus MapStatus(string? status) => status switch
    {
        "approved" => PaymentGatewayStatus.Approved,
        "rejected" or "cancelled" => PaymentGatewayStatus.Rejected,
        _ => PaymentGatewayStatus.Pending
    };

    private static GatewayPaymentStatus? ToStatus(MercadoPagoPayment payment)
    {
        if (!Guid.TryParse(payment.ExternalReference, out var paymentId))
        {
            return null;
        }

        var message = string.IsNullOrWhiteSpace(payment.StatusDetail)
            ? $"Mercado Pago payment {payment.Status}."
            : $"Mercado Pago payment {payment.Status} ({payment.StatusDetail}).";

        return new GatewayPaymentStatus(
            payment.Id.ToString(CultureInfo.InvariantCulture),
            paymentId,
            MapStatus(payment.Status),
            message);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            throw new InvalidOperationException("MercadoPago:AccessToken is not configured.");
        }

        var request = new HttpRequestMessage(method, new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        return request;
    }

    private sealed record PreferenceRequest(
        IReadOnlyCollection<PreferenceItem> Items,
        string ExternalReference,
        string? NotificationUrl);

    private sealed record PreferenceItem(string Title, int Quantity, string CurrencyId, decimal UnitPrice);

    private sealed record PreferenceResponse(string Id, string? InitPoint, string? SandboxInitPoint);

    private sealed record RefundResponse(long Id);

    private sealed record PaymentSearchResponse(IReadOnlyCollection<MercadoPagoPayment>? Results);

    private sealed record MercadoPagoPayment(long Id, string? Status, string? StatusDetail, string? ExternalReference);
}
