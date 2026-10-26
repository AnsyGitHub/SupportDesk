using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Core.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSupportDesk(this IServiceCollection services, string? connectionString, string? aiProvider = null,
        string? aiModel = null, string? aiApiKey = null)
    {
        const string message = "Set ConnectionStrings__SupportDesk to a SQLite connection string with an absolute database file path.";
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(message);
        }

        SqliteConnectionStringBuilder connection;
        try
        {
            connection = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(message);
        }

        if (!Path.IsPathFullyQualified(connection.DataSource) || connection.Mode == SqliteOpenMode.Memory)
        {
            throw new InvalidOperationException(message);
        }

        services.AddDbContext<SupportDeskDbContext>(options => options.UseSqlite(connection.ConnectionString));
        if (string.Equals(aiProvider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAiTicketService, MockAiTicketService>();
        }

        if (string.Equals(aiProvider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(30)
            });
            services.AddSingleton<IAiTicketService>(provider =>
                new GeminiAiTicketService(provider.GetRequiredService<HttpClient>(), aiModel, aiApiKey));
        }

        services.AddScoped<TicketService>();
        return services;
    }
}
