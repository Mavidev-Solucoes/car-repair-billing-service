using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Payments;

/// <summary>
/// Valida o cabeçalho x-signature das notificações do Mercado Pago ("ts=...,v1=..."):
/// v1 = HMAC-SHA256 (hex) de "id:{data.id};request-id:{x-request-id};ts:{ts};" com a assinatura secreta do webhook.
/// </summary>
public static class MercadoPagoWebhookSignature
{
    public static bool IsValid(string? signatureHeader, string? requestId, string? dataId, string secret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        string? ts = null;
        string? v1 = null;
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0] == "ts")
            {
                ts = pair[1];
            }
            else if (pair[0] == "v1")
            {
                v1 = pair[1];
            }
        }

        if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(v1))
        {
            return false;
        }

        var manifest = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(dataId))
        {
            // Ids alfanuméricos entram em minúsculas no manifesto.
            manifest.Append("id:").Append(dataId.ToLowerInvariant()).Append(';');
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            manifest.Append("request-id:").Append(requestId).Append(';');
        }

        manifest.Append("ts:").Append(ts).Append(';');

        var expected = Compute(manifest.ToString(), secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(v1.ToLowerInvariant()));
    }

    public static string Compute(string manifest, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
