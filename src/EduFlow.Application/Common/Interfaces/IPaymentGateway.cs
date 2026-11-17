namespace EduFlow.Application.Common.Interfaces;

public interface IPaymentGateway
{
    string ProviderName { get; }
    string GetCheckoutUrl(bool isSandbox);
    string FormatAmount(decimal amount);
    string GenerateCheckoutHash(string merchantId, string merchantSecret, string orderId, decimal amount, string currency);
    string GenerateWebhookSignature(string merchantId, string merchantSecret, string orderId, string payHereAmount, string payHereCurrency, int statusCode);
    bool VerifyWebhookSignature(string merchantId, string merchantSecret, string orderId, string payHereAmount, string payHereCurrency, int statusCode, string providedMd5Sig);
    string ProtectSecret(string plainSecret);
    string UnprotectSecret(string encryptedSecret);
    string MaskSecret(string plainSecret);
}
