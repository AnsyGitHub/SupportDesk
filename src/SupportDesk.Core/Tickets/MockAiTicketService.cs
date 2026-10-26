namespace SupportDesk.Core.Tickets;

public class MockAiTicketService : IAiTicketService
{
    public Task<AiTicketAnalysis?> AnalyzeAsync(string title, string description, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<AiTicketAnalysis?>(new AiTicketAnalysis(
            TicketCategory.Other, TicketPriority.Medium, TicketSentiment.Neutral,
            "Mock Analysis: synthetic demonstration summary.",
            "Mock suggestion: review the Ticket and contact the customer manually."));
    }
}
