namespace SupportDesk.Core.Tickets;

public record AiTicketAnalysis(
    TicketCategory? Category,
    TicketPriority? Priority,
    TicketSentiment? Sentiment,
    string? Summary,
    string? SuggestedAction);
