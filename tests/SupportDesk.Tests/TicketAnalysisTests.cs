using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SupportDesk.Core.Errors;
using SupportDesk.Core.Data;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Tests;

public class TicketAnalysisTests : IAsyncLifetime
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
    public async Task AnalysisTrimsAndPersistsSuggestionsReplacingManualPriorityAsync()
    {
        ai.AnalyzeAsync("Title", "Description", Arg.Any<CancellationToken>()).Returns(
            new AiTicketAnalysis(TicketCategory.Billing, TicketPriority.High, TicketSentiment.Negative,
                " Summary ", " Action "));
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("Title", "Description"));
        var edited = await service.UpdateAsync(created.Id, new("Title", "Description", TicketPriority.Critical));
        var before = DateTime.UtcNow;
        var analysis = await service.AnalyzeAsync(created.Id);
        Assert.Equal(new TicketAnalysisResponse(TicketCategory.Billing, TicketPriority.High,
            TicketSentiment.Negative, "Summary", "Action"), analysis);
        await using var freshDb = new SupportDeskDbContext(options);
        var saved = await new TicketService(freshDb).GetAsync(created.Id);
        Assert.Equal(edited with { Category = TicketCategory.Billing, Priority = TicketPriority.High,
            Sentiment = TicketSentiment.Negative, AiSummary = "Summary", SuggestedAction = "Action",
            UpdatedAt = saved.UpdatedAt }, saved);
        Assert.True(saved.UpdatedAt > edited.UpdatedAt);
        Assert.InRange(saved.UpdatedAt, before, DateTime.UtcNow);
    }
    private static AiTicketAnalysis ValidAnalysis => new(TicketCategory.Technical, TicketPriority.High,
        TicketSentiment.Negative, "Summary", "Action");

    public static IEnumerable<object?[]> InvalidResults()
    {
        yield return new object?[] { null };
        yield return new object?[] { ValidAnalysis with { Category = null } };
        yield return new object?[] { ValidAnalysis with { Priority = null } };
        yield return new object?[] { ValidAnalysis with { Sentiment = null } };
        yield return new object?[] { ValidAnalysis with { Category = (TicketCategory)99 } };
        yield return new object?[] { ValidAnalysis with { Priority = (TicketPriority)99 } };
        yield return new object?[] { ValidAnalysis with { Sentiment = (TicketSentiment)99 } };
        foreach (var text in new string?[] { null, "", " \t\n" })
        {
            yield return new object?[] { ValidAnalysis with { Summary = text } };
            yield return new object?[] { ValidAnalysis with { SuggestedAction = text } };
        }
        yield return new object?[] { ValidAnalysis with { Summary = new string('s', 1001) } };
        yield return new object?[] { ValidAnalysis with { SuggestedAction = new string('a', 2001) } };
    }

    [Theory]
    [MemberData(nameof(InvalidResults))]
    public async Task InvalidAnalysisPreservesPreviousAnalysisAndTimestampAsync(AiTicketAnalysis? invalid)
    {
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValidAnalysis);
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("t", "d"));
        await service.AnalyzeAsync(created.Id);
        var before = await service.GetAsync(created.Id);
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(invalid);
        var error = await Assert.ThrowsAsync<AiProviderException>(() => service.AnalyzeAsync(created.Id));
        Assert.False(error.IsTimeout);
        Assert.Equal(before, await service.GetAsync(created.Id));
        await service.CreateAsync(new("Other", "Description"));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderFailuresLeaveSavedAnalysisAndTimestampsUnchangedAsync(bool timeout)
    {
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValidAnalysis);
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("t", "d"));
        await service.AnalyzeAsync(created.Id);
        var before = await service.GetAsync(created.Id);
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AiTicketAnalysis?>(new AiProviderException(timeout)));
        var error = await Assert.ThrowsAsync<AiProviderException>(() => service.AnalyzeAsync(created.Id));
        Assert.Equal(timeout, error.IsTimeout);
        await service.CreateAsync(new("Other", "Description"));
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task CompletedTicketsCanBeAnalyzedRepeatedlyReplacingLatestAnalysisAsync(TicketStatus status)
    {
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValidAnalysis);
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("t", "d"));
        await service.UpdateStatusAsync(created.Id, new(status));
        await service.AnalyzeAsync(created.Id);
        var first = await service.GetAsync(created.Id);
        await service.AnalyzeAsync(created.Id);
        var repeated = await service.GetAsync(created.Id);
        Assert.Equal(first with { UpdatedAt = repeated.UpdatedAt }, repeated);
        Assert.True(repeated.UpdatedAt > first.UpdatedAt);
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new AiTicketAnalysis(TicketCategory.AccountAccess, TicketPriority.Low, TicketSentiment.Positive, "New", "Next"));
        await service.AnalyzeAsync(created.Id);
        await using var freshDb = new SupportDeskDbContext(options);
        var saved = await new TicketService(freshDb).GetAsync(created.Id);
        Assert.Equal(repeated with { Category = TicketCategory.AccountAccess, Priority = TicketPriority.Low,
            Sentiment = TicketSentiment.Positive, AiSummary = "New", SuggestedAction = "Next", UpdatedAt = saved.UpdatedAt }, saved);
        Assert.True(saved.UpdatedAt > repeated.UpdatedAt);
    }

    [Theory]
    [InlineData("title", "d", true)]
    [InlineData("t", "description", true)]
    [InlineData("t", "d", false)]
    [InlineData("  t  ", "  d  ", false)]
    public async Task OnlyTrimmedTextChangesClearAnalysisAndPreserveSubmittedPriorityAsync(string title, string description, bool clears)
    {
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValidAnalysis);
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("t", "d"));
        await service.AnalyzeAsync(created.Id);
        var before = await service.GetAsync(created.Id);
        var updated = await service.UpdateAsync(created.Id, new(title, description, TicketPriority.Critical));
        var expected = before with { Title = title.Trim(), Description = description.Trim(),
            Priority = TicketPriority.Critical, UpdatedAt = updated.UpdatedAt };
        if (clears)
        {
            expected = expected with { Category = null, Sentiment = null, AiSummary = null, SuggestedAction = null };
        }
        Assert.Equal(expected, updated);
        await using var freshDb = new SupportDeskDbContext(options);
        Assert.Equal(expected, await new TicketService(freshDb).GetAsync(created.Id));
    }

    [Fact]
    public async Task AnalysisAcceptsExactLimitsAfterTrimmingAsync()
    {
        ai.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            ValidAnalysis with { Summary = " " + new string('s', 1000) + " ", SuggestedAction = " " + new string('a', 2000) + " " });
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db, ai);
        var created = await service.CreateAsync(new("t", "d"));
        var result = await service.AnalyzeAsync(created.Id);
        Assert.Equal(new string('s', 1000), result.Summary);
        Assert.Equal(new string('a', 2000), result.SuggestedAction);
        await using var freshDb = new SupportDeskDbContext(options);
        var saved = await new TicketService(freshDb).GetAsync(created.Id);
        Assert.Equal(result.Summary, saved.AiSummary);
        Assert.Equal(result.SuggestedAction, saved.SuggestedAction);
    }

    [Fact]
    public async Task MissingProviderLeavesOrdinaryOperationsAvailableAndAnalysisUnchangedAsync()
    {
        await using var db = new SupportDeskDbContext(options);
        var service = new TicketService(db);
        var created = await service.CreateAsync(new("t", "d"));
        await Assert.ThrowsAsync<AiProviderException>(() => service.AnalyzeAsync(created.Id));
        Assert.Equal(created, await service.GetAsync(created.Id));
        Assert.Single(await service.ListAsync());
        await service.UpdateAsync(created.Id, new("Edited", "d", TicketPriority.Low));
        await service.UpdateStatusAsync(created.Id, new(TicketStatus.Closed));
    }

    [Fact]
    public async Task MissingTicketReturnsNotFoundEvenWithoutProviderAsync()
    {
        await using var db = new SupportDeskDbContext(options);
        await Assert.ThrowsAsync<NotFoundException>(() => new TicketService(db).AnalyzeAsync(12345));
        Assert.Empty(await new TicketService(db).ListAsync());
    }
}
