using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Core.Data;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Tests;

public class ConfigurationAndEnumTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Data Source=relative.db")]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=/tmp/tickets.db;Mode=Memory")]
    [InlineData("invalid connection string")]
    public void RejectsMissingOrNonDurableDatabasePaths(string? connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddSupportDesk(connectionString));
        Assert.Contains("absolute database file path", exception.Message);
        if (!string.IsNullOrEmpty(connectionString))
        {
            Assert.DoesNotContain(connectionString, exception.Message);
        }
    }

    [Theory]
    [InlineData("Gemini", typeof(GeminiAiTicketService))]
    [InlineData("Mock", typeof(MockAiTicketService))]
    public void ExplicitProviderSelectionResolvesRequestedService(string provider, Type expected)
    {
        using var services = new ServiceCollection()
            .AddSupportDesk("Data Source=/tmp/tickets.db", provider, "test-model", "synthetic-key")
            .BuildServiceProvider();
        Assert.IsType(expected, services.GetRequiredService<IAiTicketService>());
    }

    [Fact]
    public void EnumContractsUseNamesAndRejectUnknownAndNumericValues()
    {
        AssertEnumContract(TicketStatus.Open);
        AssertEnumContract(TicketPriority.Medium);
        AssertEnumContract(TicketCategory.AccountAccess);
        AssertEnumContract(TicketSentiment.Negative);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void RejectsUnboundedGeminiTimeouts(int seconds)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddSupportDesk("Data Source=/tmp/tickets.db", "Gemini", aiTimeoutSeconds: seconds));
        Assert.Equal("AI:TimeoutSeconds must be between 1 and 300.", error.Message);
    }

    private static void AssertEnumContract<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var json = $"\"{value}\"";
        Assert.Equal(json, JsonSerializer.Serialize(value));
        Assert.Equal(value, JsonSerializer.Deserialize<TEnum>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TEnum>("0"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TEnum>("\"0\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TEnum>("\"Unknown\""));
    }
}
