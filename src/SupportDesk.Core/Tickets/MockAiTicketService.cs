namespace SupportDesk.Core.Tickets;

public class MockAiTicketService : IAiTicketService
{
    public Task<string?> DraftReplyAsync(string title, string description, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(
            $"Mock Draft Reply: Thank you for contacting support about \"{title}\". " +
            "We have received your request and will review the details. " +
            "Please review and edit this draft before sending it manually.");
    }

    public Task<AiTicketAnalysis?> AnalyzeAsync(string title, string description, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<AiTicketAnalysis?>(new AiTicketAnalysis(
            TicketCategory.Other, TicketPriority.Medium, TicketSentiment.Neutral,
            "Mock Analysis: synthetic demonstration summary.",
            "Mock suggestion: review the Ticket and contact the customer manually."));
    }
}
