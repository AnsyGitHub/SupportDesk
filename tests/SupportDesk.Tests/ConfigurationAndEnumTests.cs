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

    [Fact]
    public void EnumContractsUseNamesAndRejectUnknownAndNumericValues()
    {
        AssertEnumContract(TicketStatus.Open);
        AssertEnumContract(TicketPriority.Medium);
        AssertEnumContract(TicketCategory.AccountAccess);
        AssertEnumContract(TicketSentiment.Negative);
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
