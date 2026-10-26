namespace SupportDesk.Core.Tickets;

public record TicketResponse(
    int Id,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    TicketCategory? Category,
    TicketSentiment? Sentiment,
    string? AiSummary,
    string? SuggestedAction,
    DateTime CreatedAt,
    DateTime UpdatedAt);
