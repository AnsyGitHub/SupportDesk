namespace SupportDesk.Core.Tickets;

public interface IAiTicketService
{
    Task<AiTicketAnalysis?> AnalyzeAsync(string title, string description, CancellationToken cancellationToken = default);
}
