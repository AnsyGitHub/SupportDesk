using System.Net;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using SupportDesk.Api.Errors;
using SupportDesk.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Core.Data;
using SupportDesk.Core.Errors;
using System.Text.Json;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Tests;

public class GeminiAiTicketServiceTests
{
    [Fact]
    public async Task ParsesStructuredAnalysisAsync()
    {
        using var client = new HttpClient(new ProviderHandler((_, _) => Task.FromResult(JsonResponse(
            """{"category":"AccountAccess","priority":"High","sentiment":"Negative","summary":"Cannot sign in.","suggestedAction":"Check reset flow."}"""))));
        IAiTicketService service = new GeminiAiTicketService(client, "test-model", "synthetic-key");

        var result = await service.AnalyzeAsync("Synthetic sign-in issue", "Password reset did not help.");

        Assert.Equal(new AiTicketAnalysis(TicketCategory.AccountAccess, TicketPriority.High,
            TicketSentiment.Negative, "Cannot sign in.", "Check reset flow."), result);
    }

    [Fact]
    public async Task DraftUsesStructuredTextAndDoesNotSaveAsync()
    {
        using var client = new HttpClient(new ProviderHandler(async (request, token) =>
        {
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/test-model:generateContent", request.RequestUri!.ToString());
            Assert.Equal("synthetic-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
            Assert.Equal("low", body.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
            Assert.Contains("Synthetic title", body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
            return JsonResponse("""{"text":"  Please try resetting your password.  "}""");
        }));
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SupportDeskDbContext>().UseSqlite(connection).Options;
        await using var db = new SupportDeskDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new TicketService(db, new GeminiAiTicketService(client, "test-model", "synthetic-key", thinkingLevel: "low"));
        var before = await service.CreateAsync(new("Synthetic title", "Synthetic description"));

        Assert.Equal(new TicketDraftReplyResponse("Please try resetting your password."), await service.DraftReplyAsync(before.Id));
        await using var fresh = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(fresh).GetAsync(before.Id));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("invalid-json")]
    [InlineData("empty-candidates")]
    [InlineData("wrong-shape")]
    [InlineData("truncated")]
    [InlineData("invalid-output")]
    [InlineData("numeric-enum")]
    [InlineData("invalid-values")]
    [InlineData("missing-config")]
    public async Task FailuresAreControlledAndLeaveStoredAnalysisUnchangedAsync(string scenario)
    {
        using var client = new HttpClient(new ProviderHandler(async (_, token) =>
        {
            if (scenario == "timeout") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (scenario == "network") throw new HttpRequestException("sensitive provider details");
            return scenario switch
            {
                "http" => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("sensitive provider details") },
                "invalid-json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not JSON") },
                "empty-candidates" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"candidates\":[]}") },
                "wrong-shape" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"candidates\":null}") },
                "truncated" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}") },
                "numeric-enum" => JsonResponse("""{"category":0,"priority":"High","sentiment":"Negative","summary":"Summary","suggestedAction":"Action"}"""),
                "invalid-values" => JsonResponse("""{"category":"Billing","priority":"High","sentiment":"Negative","summary":" ","suggestedAction":"Action","text":" "}"""),
                _ => JsonResponse("{}")
            };
        })) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SupportDeskDbContext>().UseSqlite(connection).Options;
        await using var db = new SupportDeskDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var seed = new TicketService(db, new MockAiTicketService());
        var created = await seed.CreateAsync(new("Synthetic title", "Synthetic description"));
        await seed.AnalyzeAsync(created.Id);
        var before = await seed.GetAsync(created.Id);
        var service = new TicketService(db, new GeminiAiTicketService(client, "test-model",
            scenario == "missing-config" ? null : "synthetic-key"));

        var analysisError = await Assert.ThrowsAsync<AiProviderException>(() => service.AnalyzeAsync(created.Id));
        var draftError = await Assert.ThrowsAsync<AiProviderException>(() => service.DraftReplyAsync(created.Id));

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new ApiExceptionHandler().TryHandleAsync(context, analysisError, CancellationToken.None);
        Assert.Equal(scenario == "timeout" ? 504 : 502, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(analysisError.Message, problem.RootElement.GetProperty("detail").GetString());
        var tools = new Tools(service);
        var mcpAnalysisError = await Assert.ThrowsAsync<McpException>(() => tools.AnalyzeTicketAsync(created.Id, CancellationToken.None));
        var mcpDraftError = await Assert.ThrowsAsync<McpException>(() => tools.DraftReplyAsync(created.Id, CancellationToken.None));
        Assert.Equal(analysisError.Message, mcpAnalysisError.Message);
        Assert.Equal(draftError.Message, mcpDraftError.Message);

        Assert.Equal(scenario == "timeout", analysisError.IsTimeout);
        Assert.Equal(scenario == "timeout", draftError.IsTimeout);
        Assert.DoesNotContain("sensitive", analysisError.Message);
        await using var fresh = new SupportDeskDbContext(options);
        Assert.Equal(before, await new TicketService(fresh).GetAsync(created.Id));
        Assert.Single(await service.ListAsync());
    }

    [Fact]
    public async Task CallerCancellationIsNotAProviderTimeoutAsync()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new ProviderHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse("{}");
        }));
        IAiTicketService service = new GeminiAiTicketService(client, "test-model", "synthetic-key");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync("Title", "Description", cancellation.Token));
    }

    [Fact]
    public async Task ConfiguredProviderDeadlineBoundsResponseBodyReadAsync()
    {
        using var client = new HttpClient(new ProviderHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) })))
        { Timeout = Timeout.InfiniteTimeSpan };
        IAiTicketService service = new GeminiAiTicketService(client, "test-model", "synthetic-key",
            requestTimeout: TimeSpan.FromMilliseconds(50));
        var error = await Assert.ThrowsAsync<AiProviderException>(() =>
            service.DraftReplyAsync("Title", "Description").WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(error.IsTimeout);
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage JsonResponse(string output) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = output } } } } }
        }))
    };

    private sealed class ProviderHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
