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

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task CompletedTicketsRemainEditableAndIdenticalUpdatesRefreshTimestampAsync(TicketStatus status)
    {
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = new SupportDeskDbContext(options))
        {
            db.Tickets.Add(new Ticket { Id = 1, Title = "t", Description = "d", Status = status,
                Priority = TicketPriority.Low, Category = TicketCategory.Billing, Sentiment = TicketSentiment.Negative,
                AiSummary = "Summary", SuggestedAction = "Action", CreatedAt = timestamp, UpdatedAt = timestamp });
            await db.SaveChangesAsync();
        }

        TicketResponse updated;
        await using (var db = new SupportDeskDbContext(options))
        {
            var service = new TicketService(db);
            var before = await service.GetAsync(1);
            updated = await service.UpdateAsync(1, new(" changed ", " changed description ", TicketPriority.High));
            Assert.Equal(before with { Title = "changed", Description = "changed description",
                Priority = TicketPriority.High, Category = null, Sentiment = null, AiSummary = null,
                SuggestedAction = null, UpdatedAt = updated.UpdatedAt }, updated);
            Assert.True(updated.UpdatedAt > timestamp);
            var repeated = await service.UpdateAsync(1, new(updated.Title, updated.Description, updated.Priority));
            Assert.Equal(updated with { UpdatedAt = repeated.UpdatedAt }, repeated);
            Assert.True(repeated.UpdatedAt > updated.UpdatedAt);
            updated = repeated;
        }
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(updated, await new TicketService(freshDb).GetAsync(1));
    }

    [Theory]
    [InlineData(null, "d", TicketPriority.High)]
    [InlineData(" ", "d", TicketPriority.High)]
    [InlineData("t", null, TicketPriority.High)]
    [InlineData("t", " ", TicketPriority.High)]
    [InlineData("t", "d", null)]
    [InlineData("t", "d", (TicketPriority)99)]
    public async Task InvalidUpdatesLeaveTicketAndTimestampUnchangedAsync(string? title, string? description, TicketPriority? priority)
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        var created = await service.CreateAsync(new("Original", "Original description"));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(created.Id, new(title, description, priority)));
        // A subsequent valid write must not accidentally save any part of the failed edit.
        await service.CreateAsync(new("Other", "Other description"));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(created, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(201, 10)]
    [InlineData(10, 5001)]
    public async Task OversizedUpdatesLeaveTicketUnchangedAsync(int titleLength, int descriptionLength)
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        var created = await service.CreateAsync(new("Original", "Description"));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(created.Id,
            new(new string('t', titleLength), new string('d', descriptionLength), TicketPriority.High)));
        Assert.Equal(created, await service.GetAsync(created.Id));
    }

    [Fact]
    public async Task UpdateAcceptsExactTextLimitsAfterTrimmingAsync()
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        var created = await service.CreateAsync(new("t", "d"));
        var updated = await service.UpdateAsync(created.Id,
            new(" " + new string('t', 200) + " ", " " + new string('d', 5000) + " ", TicketPriority.Low));
        Assert.Equal(new string('t', 200), updated.Title);
        Assert.Equal(new string('d', 5000), updated.Description);
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(updated, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData((TicketStatus)99)]
    public async Task InvalidStatusLeavesTicketAndTimestampUnchangedAsync(TicketStatus? status)
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        var created = await service.CreateAsync(new("t", "d"));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateStatusAsync(created.Id, new(status)));
        await service.CreateAsync(new("Other", "Description"));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(created, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(12345)]
    public async Task UpdatingMissingTicketsReturnsNotFoundAndLeavesDataUnchangedAsync(int id)
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        var created = await service.CreateAsync(new("t", "d"));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(id, new("Changed", "Changed", TicketPriority.High)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateStatusAsync(id, new(TicketStatus.Closed)));
        Assert.Equal(created, await service.GetAsync(created.Id));
        Assert.Single(await service.ListAsync());
    }

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task StatusPermitsEveryTransitionAndRepeatedStatusAsync(TicketStatus initial)
    {
        await using (var db = new SupportDeskDbContext(options))
        {
            db.Tickets.Add(new Ticket { Id = 1, Title = "t", Description = "d", Status = initial,
                CreatedAt = DateTime.UtcNow.AddDays(-1), UpdatedAt = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        foreach (var next in Enum.GetValues<TicketStatus>())
        {
            await using (var db = new SupportDeskDbContext(options))
            {
                var service = new TicketService(db);
                await service.UpdateStatusAsync(1, new(initial));
                var before = await service.GetAsync(1);
                var changed = await service.UpdateStatusAsync(1, new(next));
                Assert.Equal(before with { Status = next, UpdatedAt = changed.UpdatedAt }, changed);
                Assert.True(changed.UpdatedAt > before.UpdatedAt);
                var repeated = await service.UpdateStatusAsync(1, new(next));
                Assert.Equal(changed with { UpdatedAt = repeated.UpdatedAt }, repeated);
                Assert.True(repeated.UpdatedAt > changed.UpdatedAt);
            }
            await using var freshDb = new SupportDeskDbContext(options);
            Assert.Equal(next, (await new TicketService(freshDb).GetAsync(1)).Status);
        }
    }

    [Fact]
    public async Task UpdateTrimsTextPersistsPriorityAndRefreshesTimestampAsync()
    {
        TicketResponse created;
        await using (var db = new SupportDeskDbContext(options))
        {
            created = await new TicketService(db).CreateAsync(new("Before", "Before description"));
        }

        TicketResponse updated;
        var before = DateTime.UtcNow;
        await using (var db = new SupportDeskDbContext(options))
        {
            updated = await new TicketService(db).UpdateAsync(created.Id,
                new UpdateTicketRequest("  After  ", "  After description  ", TicketPriority.Critical));
        }

        Assert.Equal("After", updated.Title);
        Assert.Equal("After description", updated.Description);
        Assert.Equal(TicketPriority.Critical, updated.Priority);
        Assert.Equal(created.Status, updated.Status);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
        Assert.InRange(updated.UpdatedAt, before, DateTime.UtcNow);
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(updated, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Fact]
    public async Task DashboardCountsOverlappingPrioritiesAcrossAllStatusesWithoutWritingAsync()
    {
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = new SupportDeskDbContext(options))
        {
            foreach (var (status, priority) in new[]
            {
                (TicketStatus.Open, TicketPriority.High),
                (TicketStatus.Open, TicketPriority.Low),
                (TicketStatus.InProgress, TicketPriority.Critical),
                (TicketStatus.Resolved, TicketPriority.High),
                (TicketStatus.Resolved, TicketPriority.Medium),
                (TicketStatus.Closed, TicketPriority.Critical)
            })
            {
                db.Tickets.Add(new Ticket { Title = "t", Description = "d", Status = status,
                    Priority = priority, CreatedAt = timestamp, UpdatedAt = timestamp });
            }
            await db.SaveChangesAsync();
        }

        await using var freshDb = new SupportDeskDbContext(options);
        var service = new TicketService(freshDb);
        Assert.Equal(new DashboardSummaryResponse(6, 2, 4, 2), await service.GetDashboardSummaryAsync());
        Assert.All(await service.ListAsync(), ticket => Assert.Equal(timestamp, ticket.UpdatedAt));
    }

    [Fact]
    public async Task ListOrdersNewestFirstThenHighestIdAndLeavesTimestampsUnchangedAsync()
    {
        var older = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = older.AddDays(1);
        await using (var db = new SupportDeskDbContext(options))
        {
            db.Tickets.AddRange(
                new Ticket { Id = 10, Title = "Old", Description = "d", CreatedAt = older, UpdatedAt = older },
                new Ticket { Id = 2, Title = "New low ID", Description = "d", CreatedAt = newer, UpdatedAt = older },
                new Ticket { Id = 3, Title = "New high ID", Description = "d", CreatedAt = newer, UpdatedAt = older });
            await db.SaveChangesAsync();
        }

        await using var freshDb = new SupportDeskDbContext(options);
        var service = new TicketService(freshDb);
        var listed = await service.ListAsync();
        Assert.Equal(new[] { 3, 2, 10 }, listed.Select(ticket => ticket.Id));
        foreach (var ticket in listed)
        {
            Assert.Equal(older, ticket.UpdatedAt);
            Assert.Equal(ticket, await service.GetAsync(ticket.Id));
        }
    }

    [Fact]
    public async Task DashboardReturnsZeroCountsForEmptyDatabaseAsync()
    {
        await using var db = new SupportDeskDbContext(options);
        var summary = await new TicketService(db).GetDashboardSummaryAsync();
        Assert.Equal(new DashboardSummaryResponse(0, 0, 0, 0), summary);
    }

    [Fact]
    public async Task ListReturnsEmptyWhenNoTicketsExistAsync()
    {
        await using var db = new SupportDeskDbContext(options);
        Assert.Empty(await new TicketService(db).ListAsync());
    }

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
