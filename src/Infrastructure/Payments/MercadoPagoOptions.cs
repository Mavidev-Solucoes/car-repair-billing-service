namespace Infrastructure.Payments;

public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    /// <summary>Access token da aplicação no Mercado Pago (credencial de teste no sandbox). Vem de Secret, nunca do git.</summary>
    public string AccessToken { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.mercadopago.com";

    /// <summary>URL pública do webhook (POST /api/payments/webhooks/mercadopago pelo Kong). Opcional.</summary>
    public string? NotificationUrl { get; set; }

    /// <summary>Assinatura secreta do webhook (painel do Mercado Pago). Com ela, notificações sem assinatura válida são recusadas.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Usa o sandbox_init_point da preferência (checkout de teste) em vez do init_point.</summary>
    public bool UseSandboxCheckout { get; set; } = true;

    public string ItemTitle { get; set; } = "Serviços da oficina Car Repair";
}

public sealed class PaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway";

    /// <summary>Fake (padrão) ou MercadoPago.</summary>
    public string Provider { get; set; } = "Fake";

    public bool UsesMercadoPago => string.Equals(Provider, "MercadoPago", StringComparison.OrdinalIgnoreCase);
}
