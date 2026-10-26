using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SupportDesk.Core.Data;
using SupportDesk.Core.Errors;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Tests;

public class TicketDraftReplyTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private DbContextOptions<SupportDeskDbContext> options = null!;
    private readonly IAiTicketService ai = Substitute.For<IAiTicketService>();

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        options = new DbContextOptionsBuilder<SupportDeskDbContext>().UseSqlite(connection).Options;
        await using var db = new SupportDeskDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task DraftUsesCurrentTextWithoutAnalysisAndLeavesTicketUnchangedAsync()
    {
        ai.DraftReplyAsync("Edited title", "Edited description", Arg.Any<CancellationToken>())
            .Returns("  Please try signing in again. \n");
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("Original title", "Original description"));
        var before = await service.UpdateAsync(created.Id, new("Edited title", "Edited description", TicketPriority.Critical));

        var draft = await service.DraftReplyAsync(created.Id);

        Assert.Equal(new TicketDraftReplyResponse("Please try signing in again."), draft);
        Assert.Equal(before, await service.GetAsync(created.Id));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n")]
    [InlineData("oversized")]
    public async Task MalformedDraftLeavesExistingAnalysisAndTimestampUnchangedAsync(string? text)
    {
        ai.DraftReplyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(text == "oversized" ? new string('x', 4001) : text);
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new AiTicketAnalysis(TicketCategory.Billing, TicketPriority.High, TicketSentiment.Negative, "Summary", "Action"));
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("Title", "Description"));
        await service.AnalyzeAsync(created.Id);
        var before = await service.GetAsync(created.Id);

        var error = await Assert.ThrowsAsync<AiProviderException>(() => service.DraftReplyAsync(created.Id));

        Assert.False(error.IsTimeout);
        Assert.Equal(before, await service.GetAsync(created.Id));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task CompletedTicketsAcceptExactLimitAfterTrimmingWithoutChangingAnalysisAsync(TicketStatus status)
    {
        var text = new string('x', 4000);
        ai.DraftReplyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(" \n" + text + "\t ");
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new AiTicketAnalysis(TicketCategory.Technical, TicketPriority.High, TicketSentiment.Negative, "Summary", "Action"));
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("Title", "Description"));
        await service.AnalyzeAsync(created.Id);
        var before = await service.UpdateStatusAsync(created.Id, new(status));

        Assert.Equal(new TicketDraftReplyResponse(text), await service.DraftReplyAsync(created.Id));
        Assert.Equal(new TicketDraftReplyResponse(text), await service.DraftReplyAsync(created.Id));

        Assert.Equal(before, await service.GetAsync(created.Id));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderFailuresAndTimeoutsLeaveTicketUnchangedAsync(bool isTimeout)
    {
        ai.DraftReplyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string?>(new AiProviderException(isTimeout)));
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var before = await service.CreateAsync(new("Title", "Description"));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => service.DraftReplyAsync(before.Id));

        Assert.Equal(isTimeout, error.IsTimeout);
        Assert.Equal(before, await service.GetAsync(before.Id));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(freshDb).GetAsync(before.Id));
    }

    [Fact]
    public async Task MissingTicketReturnsNotFoundBeforeProviderErrorsAsync()
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        await Assert.ThrowsAsync<NotFoundException>(() => service.DraftReplyAsync(12345));
        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task MissingProviderReturnsControlledErrorAndLeavesTicketUnchangedAsync()
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        var before = await service.CreateAsync(new("Title", "Description"));
        await Assert.ThrowsAsync<AiProviderException>(() => service.DraftReplyAsync(before.Id));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(freshDb).GetAsync(before.Id));
    }
}
