namespace SupportDesk.Core.Tickets;

public class Ticket
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public TicketStatus Status { get; set; }
    public TicketPriority Priority { get; set; }
    public TicketCategory? Category { get; set; }
    public TicketSentiment? Sentiment { get; set; }
    public string? AiSummary { get; set; }
    public string? SuggestedAction { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
