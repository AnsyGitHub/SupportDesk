namespace SupportDesk.Core.Tickets;

public record UpdateTicketRequest(string? Title, string? Description, TicketPriority? Priority);
