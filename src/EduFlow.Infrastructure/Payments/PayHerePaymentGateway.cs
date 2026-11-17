using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EduFlow.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace EduFlow.Infrastructure.Payments;

public sealed class PayHerePaymentGateway(IDataProtectionProvider dataProtectionProvider) : IPaymentGateway
{
    private const string ProtectorPurpose = "EduFlow.PayHere.MerchantSecret.v1";
    private const string SandboxCheckoutUrl = "https://sandbox.payhere.lk/pay/checkout";
    private const string LiveCheckoutUrl = "https://www.payhere.lk/pay/checkout";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public string ProviderName => "PayHere";

    public string GetCheckoutUrl(bool isSandbox) =>
        isSandbox ? SandboxCheckoutUrl : LiveCheckoutUrl;

    public string FormatAmount(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    public string GenerateCheckoutHash(
        string merchantId,
        string merchantSecret,
        string orderId,
        decimal amount,
        string currency)
    {
        var hashedSecret = UpperMd5(merchantSecret?.Trim() ?? string.Empty);
        var formattedAmount = FormatAmount(amount);
        var payload = string.Concat(
            merchantId?.Trim() ?? string.Empty,
            orderId?.Trim() ?? string.Empty,
            formattedAmount,
            (currency?.Trim() ?? "LKR").ToUpperInvariant(),
            hashedSecret);

        return UpperMd5(payload);
    }

    public string GenerateWebhookSignature(
        string merchantId,
        string merchantSecret,
        string orderId,
        string payHereAmount,
        string payHereCurrency,
        int statusCode)
    {
        var hashedSecret = UpperMd5(merchantSecret?.Trim() ?? string.Empty);
        var payload = string.Concat(
            merchantId?.Trim() ?? string.Empty,
            orderId?.Trim() ?? string.Empty,
            payHereAmount?.Trim() ?? string.Empty,
            (payHereCurrency?.Trim() ?? "LKR").ToUpperInvariant(),
            statusCode.ToString(CultureInfo.InvariantCulture),
            hashedSecret);

        return UpperMd5(payload);
    }

    public bool VerifyWebhookSignature(
        string merchantId,
        string merchantSecret,
        string orderId,
        string payHereAmount,
        string payHereCurrency,
        int statusCode,
        string providedMd5Sig)
    {
        if (string.IsNullOrWhiteSpace(providedMd5Sig))
        {
            return false;
        }

        var expected = GenerateWebhookSignature(
            merchantId,
            merchantSecret,
            orderId,
            payHereAmount,
            payHereCurrency,
            statusCode);

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(providedMd5Sig.Trim().ToUpperInvariant());
        return expectedBytes.Length == providedBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    public string ProtectSecret(string plainSecret) =>
        _protector.Protect(plainSecret);

    public string UnprotectSecret(string encryptedSecret) =>
        _protector.Unprotect(encryptedSecret);

    public string MaskSecret(string plainSecret)
    {
        if (string.IsNullOrWhiteSpace(plainSecret))
        {
            return string.Empty;
        }

        var trimmed = plainSecret.Trim();
        if (trimmed.Length <= 4)
        {
            return "••••••••";
        }

        return $"••••••••{trimmed[^4..]}";
    }

    public static string UpperMd5(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
