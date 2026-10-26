using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Core.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSupportDesk(this IServiceCollection services, string? connectionString)
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
        services.AddScoped<TicketService>();
        return services;
    }
}
