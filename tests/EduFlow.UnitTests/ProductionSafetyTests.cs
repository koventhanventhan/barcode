using EduFlow.Infrastructure.Notifications;
using EduFlow.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EduFlow.UnitTests;

public sealed class ProductionSafetyTests
{
    [Fact]
    public void JwtOptions_has_empty_default_signing_key()
    {
        var opts = new JwtOptions();
        opts.SigningKey.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short-key-under-32-chars")]
    [InlineData(JwtOptions.KnownDevSigningKey)]
    public void ValidateJwtSigningKey_rejects_missing_short_or_dev_key_in_production(string? key)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = key
        });
        var env = new TestHostEnvironment("Production");

        var act = () => StartupSecurityValidator.ValidateJwtSigningKey(config, env);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateJwtSigningKey_allows_dev_key_in_development_and_testing_and_strong_key_in_production()
    {
        var devConfig = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = JwtOptions.KnownDevSigningKey
        });

        StartupSecurityValidator.ValidateJwtSigningKey(devConfig, new TestHostEnvironment("Development"));
        StartupSecurityValidator.ValidateJwtSigningKey(devConfig, new TestHostEnvironment("Testing"));

        var prodConfig = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "Prod_Only_Secret_Signing_Key_At_Least_32_Bytes_Long_987654321!"
        });
        StartupSecurityValidator.ValidateJwtSigningKey(prodConfig, new TestHostEnvironment("Production"));
    }

    [Theory]
    [InlineData(null, "StrongProdPass!123")]
    [InlineData("", "StrongProdPass!123")]
    [InlineData("ops@admino.lk", null)]
    [InlineData("ops@admino.lk", "")]
    [InlineData("ops@admino.lk", "Short1!")]
    [InlineData("ops@admino.lk", StartupSecurityValidator.KnownDefaultSuperAdminPassword)]
    public void SuperAdmin_seed_credentials_fail_in_production_when_missing_short_or_default(
        string? email,
        string? password)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SUPERADMIN_EMAIL"] = email,
            ["SUPERADMIN_PASSWORD"] = password,
            ["Seed:SuperAdmin:Email"] = "superadmin@admino.lk"
        });

        var act = () => StartupSecurityValidator.ResolveAndValidateSuperAdminCredentials(
            config,
            new TestHostEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SuperAdmin_seed_credentials_succeed_in_production_with_explicit_strong_values()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SUPERADMIN_EMAIL"] = "Admin@SchoolPlatform.lk",
            ["SUPERADMIN_PASSWORD"] = "VeryStrongProdPassword#2026"
        });

        var (email, password, _) = StartupSecurityValidator.ResolveAndValidateSuperAdminCredentials(
            config,
            new TestHostEnvironment("Production"));

        email.Should().Be("admin@schoolplatform.lk");
        password.Should().Be("VeryStrongProdPassword#2026");
    }

    [Fact]
    public void ShouldSeedDemoData_is_true_only_in_development_or_when_demo_enabled()
    {
        var defaultConfig = BuildConfig(new Dictionary<string, string?>());
        var enabledConfig = BuildConfig(new Dictionary<string, string?> { ["Demo:Enabled"] = "true" });
        var disabledConfig = BuildConfig(new Dictionary<string, string?> { ["Demo:Enabled"] = "false" });

        StartupSecurityValidator.ShouldSeedDemoData(defaultConfig, new TestHostEnvironment("Development")).Should().BeTrue();
        StartupSecurityValidator.ShouldSeedDemoData(defaultConfig, new TestHostEnvironment("Production")).Should().BeFalse();
        StartupSecurityValidator.ShouldSeedDemoData(disabledConfig, new TestHostEnvironment("Production")).Should().BeFalse();
        StartupSecurityValidator.ShouldSeedDemoData(enabledConfig, new TestHostEnvironment("Production")).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("admino.lk;*")]
    public void ValidateAllowedHosts_rejects_wildcard_or_empty_outside_development_and_testing(string? allowedHosts)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = allowedHosts
        });

        var act = () => StartupSecurityValidator.ValidateAllowedHosts(config, new TestHostEnvironment("Production"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateAllowedHosts_accepts_explicit_hosts_in_production()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "admino.lk;*.admino.lk"
        });

        StartupSecurityValidator.ValidateAllowedHosts(config, new TestHostEnvironment("Production"));
    }

    [Fact]
    public void SmsConfigurationLoader_prefers_Sms_keys_over_legacy_keys_and_reports_deprecations()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Sms:Provider"] = "NotifyLk",
            ["Sms:UserId"] = "new-user",
            ["Sms:ApiKey"] = "new-secret-key",
            ["Sms:SenderId"] = "NewSender",
            ["Sms:BaseUrl"] = "https://new.example.test/api/v1",
            ["Sms:TestMode"] = "true",
            ["Notifications:Sms:Provider"] = "Fake",
            ["Notifications:Sms:NotifyLk:UserId"] = "old-user",
            ["Notifications:Sms:NotifyLk:ApiKey"] = "old-secret-key",
            ["Notifications:Sms:NotifyLk:SenderId"] = "OldSender",
            ["Notifications:Sms:NotifyLk:Endpoint"] = "https://old.example.test/api/v1/send"
        });

        var opts = new SmsProviderOptions();
        SmsConfigurationLoader.ApplyTo(opts, config);
        var deprecated = SmsConfigurationLoader.DetectDeprecatedKeys(config);

        opts.Provider.Should().Be("NotifyLk");
        opts.UserId.Should().Be("new-user");
        opts.ApiKey.Should().Be("new-secret-key");
        opts.SenderId.Should().Be("NewSender");
        opts.BaseUrl.Should().Be("https://new.example.test/api/v1");
        opts.TestMode.Should().BeTrue();
        deprecated.Should().Contain("Notifications:Sms:Provider");
        deprecated.Should().Contain("Notifications:Sms:NotifyLk:ApiKey");
    }

    [Fact]
    public void SmsConfigurationLoader_falls_back_to_legacy_NotifyLk_keys_when_Sms_keys_absent()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:Sms:Provider"] = "NotifyLk",
            ["Notifications:Sms:NotifyLk:UserId"] = "legacy-user",
            ["Notifications:Sms:NotifyLk:ApiKey"] = "legacy-key",
            ["Notifications:Sms:NotifyLk:SenderId"] = "LegacyId",
            ["Notifications:Sms:NotifyLk:Endpoint"] = "https://app.notify.lk/api/v1/send"
        });

        var opts = new SmsProviderOptions();
        SmsConfigurationLoader.ApplyTo(opts, config);

        opts.Provider.Should().Be("NotifyLk");
        opts.UserId.Should().Be("legacy-user");
        opts.ApiKey.Should().Be("legacy-key");
        opts.SenderId.Should().Be("LegacyId");
        opts.BaseUrl.Should().Be("https://app.notify.lk/api/v1");
    }

    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "EduFlow.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
