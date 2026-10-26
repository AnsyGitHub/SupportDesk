namespace SupportDesk.Core.Tickets;

public record TicketAnalysisResponse(
    TicketCategory Category,
    TicketPriority Priority,
    TicketSentiment Sentiment,
    string Summary,
    string SuggestedAction);
