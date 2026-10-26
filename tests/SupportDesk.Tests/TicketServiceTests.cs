using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Core.Data;
using SupportDesk.Core.Errors;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Tests;

public class TicketServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private DbContextOptions<SupportDeskDbContext> options = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        options = new DbContextOptionsBuilder<SupportDeskDbContext>().UseSqlite(connection).Options;
        await using var db = new SupportDeskDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task CreateTrimsTextAndPersistsDefaultsAcrossContextsAsync()
    {
        var before = DateTime.UtcNow;
        TicketResponse created;
        await using (var db = new SupportDeskDbContext(options))
        {
            created = await new TicketService(db).CreateAsync(new("  Account access  ", "  Cannot sign in.\n"));
        }

        Assert.True(created.Id > 0);
        Assert.Equal("Account access", created.Title);
        Assert.Equal("Cannot sign in.", created.Description);
        Assert.Equal(TicketStatus.Open, created.Status);
        Assert.Equal(TicketPriority.Medium, created.Priority);
        Assert.Null(created.Category);
        Assert.Null(created.Sentiment);
        Assert.Null(created.AiSummary);
        Assert.Null(created.SuggestedAction);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
        Assert.InRange(created.CreatedAt, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);

        await using var freshDb = new SupportDeskDbContext(options);
        var retrieved = await new TicketService(freshDb).GetAsync(created.Id);
        Assert.Equal(created, retrieved);
        Assert.Equal(DateTimeKind.Utc, retrieved.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, retrieved.UpdatedAt.Kind);
        Assert.Equal(1, await freshDb.Tickets.CountAsync());
    }

    [Theory]
    [InlineData(null, "Description", "Title")]
    [InlineData("", "Description", "Title")]
    [InlineData(" \t\n", "Description", "Title")]
    [InlineData("Title", null, "Description")]
    [InlineData("Title", "", "Description")]
    [InlineData("Title", " \t\n", "Description")]
    public async Task RejectsMissingTextWithoutSavingAsync(string? title, string? description, string field)
    {
        await using var db = new SupportDeskDbContext(options);
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new TicketService(db).CreateAsync(new(title, description)));
        Assert.Contains(field, exception.Message);
        Assert.Equal(0, await db.Tickets.CountAsync());
    }

    [Theory]
    [InlineData(201, 10, "Title")]
    [InlineData(10, 5001, "Description")]
    public async Task RejectsOversizedTextWithoutSavingAsync(int titleLength, int descriptionLength, string field)
    {
        await using var db = new SupportDeskDbContext(options);
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new TicketService(db).CreateAsync(new(new string('t', titleLength), new string('d', descriptionLength))));
        Assert.Contains(field, exception.Message);
        Assert.Equal(0, await db.Tickets.CountAsync());
    }

    [Fact]
    public async Task AcceptsExactLimitsAfterTrimmingAsync()
    {
        await using var db = new SupportDeskDbContext(options);
        var created = await new TicketService(db).CreateAsync(new(
            " " + new string('t', 200) + " ", "\n" + new string('d', 5000) + "\t"));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(created, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(12345)]
    public async Task UnknownIdThrowsNotFoundWithoutChangingSavedTicketAsync(int id)
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        var created = await service.CreateAsync(new("Title", "Description"));
        var exception = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(id));
        Assert.Equal($"Ticket {id} was not found.", exception.Message);
        Assert.Equal(created, await service.GetAsync(created.Id));
        Assert.Equal(1, await db.Tickets.CountAsync());
    }
}
