using System.Net;
using System.Text;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Notifications;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduFlow.UnitTests;

public sealed class SmsGatewayUnitTests
{
    [Theory]
    [InlineData("0771234567", true, "94771234567")]
    [InlineData("+94771234567", true, "94771234567")]
    [InlineData("94771234567", true, "94771234567")]
    [InlineData("071-234-5678", true, "94712345678")]
    [InlineData("+94 76 123 4567", true, "94761234567")]
    [InlineData("(070) 111 2233", true, "94701112233")]
    [InlineData("0729998877", true, "94729998877")]
    [InlineData("0749998877", true, "94749998877")]
    [InlineData("0759998877", true, "94759998877")]
    [InlineData("0789998877", true, "94789998877")]
    [InlineData("0799998877", true, "94799998877")]
    [InlineData("0112345678", false, null)]
    [InlineData("+94112345678", false, null)]
    [InlineData("94812345678", false, null)]
    [InlineData("0212223344", false, null)]
    [InlineData("07712345", false, null)]
    [InlineData("077123456789", false, null)]
    [InlineData("771234567", false, null)]
    [InlineData("077123456a", false, null)]
    [InlineData("", false, null)]
    [InlineData("   ", false, null)]
    public void Phone_normalizer_handles_Sri_Lankan_mobiles_and_rejects_landlines_and_invalid_numbers(
        string input,
        bool expectedValid,
        string? expectedCanonical)
    {
        var result = SriLankanPhoneNormalizer.NormalizeForGateway(input);

        result.IsValid.Should().Be(expectedValid);
        result.Canonical947.Should().Be(expectedCanonical);
        if (!expectedValid)
        {
            result.ErrorCode.Should().Be("INVALID_NUMBER");
            result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void ParseSendResponse_parses_success_and_error_cases_and_redacts_secrets()
    {
        const string apiKey = "secret-notify-key-98765";

        var ok = NotifyLkSmsProvider.ParseSendResponse(
            HttpStatusCode.OK,
            """{"status":"success","data":"Sent"}""",
            "Notify.lk",
            apiKey);
        ok.Succeeded.Should().BeTrue();
        ok.Provider.Should().Be("Notify.lk");
        ok.ProviderMessageId.Should().NotBeNullOrWhiteSpace();

        var okWithMetrics = NotifyLkSmsProvider.ParseSendResponse(
            HttpStatusCode.OK,
            """{"status":"success","data":{"id":"nlk-42","segments":2,"cost":3.25}}""",
            "Notify.lk",
            apiKey);
        okWithMetrics.Succeeded.Should().BeTrue();
        okWithMetrics.ProviderMessageId.Should().Be("nlk-42");
        okWithMetrics.GatewaySegments.Should().Be(2);
        okWithMetrics.GatewayCost.Should().Be(3.25m);

        var lowBalance = NotifyLkSmsProvider.ParseSendResponse(
            HttpStatusCode.BadRequest,
            """{"status":"failed","message":"Insufficient balance to send SMS"}""",
            "Notify.lk",
            apiKey);
        lowBalance.Succeeded.Should().BeFalse();
        lowBalance.ErrorCode.Should().Be("INSUFFICIENT_BALANCE");

        var invalidSender = NotifyLkSmsProvider.ParseSendResponse(
            HttpStatusCode.BadRequest,
            """{"status":"failed","message":"Invalid sender_id"}""",
            "Notify.lk",
            apiKey);
        invalidSender.Succeeded.Should().BeFalse();
        invalidSender.ErrorCode.Should().Be("INVALID_SENDER");

        var invalidNumber = NotifyLkSmsProvider.ParseSendResponse(
            HttpStatusCode.UnprocessableEntity,
            """{"status":"failed","message":"Invalid recipient mobile number"}""",
            "Notify.lk",
            apiKey);
        invalidNumber.Succeeded.Should().BeFalse();
        invalidNumber.ErrorCode.Should().Be("INVALID_NUMBER");

        var leakedSecretErr = NotifyLkSmsProvider.ParseSendResponse(
            HttpStatusCode.Unauthorized,
            $$"""{"status":"failed","message":"Invalid api_key {{apiKey}}"}""",
            "Notify.lk",
            apiKey);
        leakedSecretErr.Succeeded.Should().BeFalse();
        leakedSecretErr.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        leakedSecretErr.ErrorMessage.Should().NotContain(apiKey);
    }

    [Fact]
    public void ParseBalanceResponse_parses_NotifyLk_status_payload()
    {
        var ok = NotifyLkSmsProvider.ParseBalanceResponse(
            HttpStatusCode.OK,
            """{"status":"success","data":{"active":true,"acc_balance":3500.75}}""",
            "Notify.lk");

        ok.Succeeded.Should().BeTrue();
        ok.IsActive.Should().BeTrue();
        ok.Balance.Should().Be(3500.75m);
        ok.Currency.Should().Be("LKR");
    }

    [Fact]
    public async Task NotifyLkSmsProvider_enforces_TestMode_allowlist_and_sets_unicode_type_for_Tamil_and_Sinhala()
    {
        var apiKey = "test-secret-api-key-112233";
        var handler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"success","data":"Sent"}""", Encoding.UTF8, "application/json")
        });
        var configProvider = new StubConfigProvider(new EffectiveSmsGatewayConfig(
            Provider: "NotifyLk",
            UserId: "user-1",
            ApiKey: apiKey,
            SenderId: "Admino",
            PromotionalSenderId: "EduPromo",
            BaseUrl: "https://app.notify.lk/api/v1",
            TestMode: true,
            TestAllowList: ["94771112233"],
            LowBalanceThreshold: 500m));

        var provider = new NotifyLkSmsProvider(
            new HttpClient(handler),
            configProvider,
            new SmsSegmentCalculator(),
            NullLogger<NotifyLkSmsProvider>.Instance);

        // 1. Not in allow-list -> skipped without HTTP call
        var skipped = await provider.SendAsync(new SmsSendRequest("0779998888", "Hello"));
        skipped.IsSkipped.Should().BeTrue();
        skipped.SkipReason.Should().Be("Skipped:TestMode");
        handler.Requests.Should().BeEmpty();

        // 2. Landline -> rejected before HTTP call
        var landline = await provider.SendAsync(new SmsSendRequest("0112345678", "Hello"));
        landline.Succeeded.Should().BeFalse();
        landline.ErrorCode.Should().Be("INVALID_NUMBER");
        handler.Requests.Should().BeEmpty();

        // 3. Allowed number + English GSM-7 -> HTTP POST without type=unicode
        var sentEn = await provider.SendAsync(new SmsSendRequest("0771112233", "Present at 4:00 PM"));
        sentEn.Succeeded.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
        handler.Bodies[0].Should().Contain("to=94771112233");
        handler.Bodies[0].Should().Contain("sender_id=Admino");
        handler.Bodies[0].Should().NotContain("type=unicode");

        // 4. Allowed number + Sinhala Unicode + Promotional -> HTTP POST with type=unicode & PromotionalSenderId
        var sentSi = await provider.SendAsync(new SmsSendRequest("+94771112233", "නිමල් පැමිණ ඇත", IsPromotional: true));
        sentSi.Succeeded.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        handler.Bodies[1].Should().Contain("to=94771112233");
        handler.Bodies[1].Should().Contain("sender_id=EduPromo");
        handler.Bodies[1].Should().Contain("type=unicode");
    }

    [Fact]
    public void PiiRedactor_redacts_api_key_in_query_strings_and_raw_secrets()
    {
        const string key = "super-secret-api-key-999";
        var query = PiiRedactor.RedactQueryString("https://app.notify.lk/api/v1/status?user_id=12&api_key=abc12345");
        query.Should().NotContain("abc12345");
        query.Should().Contain("api_key=[REDACTED]");

        var scrubbed = PiiRedactor.RedactSecretValues($"Gateway failed for api_key={key}", key);
        scrubbed.Should().NotContain(key);
        scrubbed.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void NotificationCatalog_marks_Broadcast_as_Promotional_and_all_others_Transactional()
    {
        NotificationCatalog.GetDefinition(NotificationMessageType.Broadcast).SmsCategory
            .Should().Be(SmsMessageCategory.Promotional);

        foreach (var def in NotificationCatalog.All.Where(d => d.MessageType != NotificationMessageType.Broadcast))
        {
            def.SmsCategory.Should().Be(SmsMessageCategory.Transactional);
        }
    }

    private sealed class StubConfigProvider(EffectiveSmsGatewayConfig config) : ISmsGatewayConfigProvider
    {
        public Task<EffectiveSmsGatewayConfig> GetEffectiveConfigAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(config);

        public Task<string?> ResolveTenantSenderIdAsync(
            Guid? tenantId,
            bool isPromotional,
            string? explicitSenderId,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(explicitSenderId))
            {
                return Task.FromResult<string?>(explicitSenderId);
            }

            if (isPromotional && !string.IsNullOrWhiteSpace(config.PromotionalSenderId))
            {
                return Task.FromResult<string?>(config.PromotionalSenderId);
            }

            return Task.FromResult<string?>(config.SenderId);
        }

        public Task RecordErrorAsync(
            string provider,
            string operation,
            string? recipient,
            string errorCode,
            string errorMessage,
            Guid? tenantId = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return responder(request);
        }
    }
}
