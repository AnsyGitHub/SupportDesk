using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SupportDesk.Core.Errors;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Mcp;

[McpServerToolType]
public class Tools(TicketService tickets)
{
    [McpServerTool(Name = "create_ticket", UseStructuredContent = true)]
    [Description("Create a persisted Ticket from a title and description. Starts Open with Medium priority.")]
    public async Task<TicketResponse> CreateTicketAsync(
        [Description("Required title, at most 200 characters after trimming.")] string title,
        [Description("Required description, at most 5000 characters after trimming.")] string description,
        CancellationToken cancellationToken)
    {
        try
        {
            return await tickets.CreateAsync(new CreateTicketRequest(title, description), cancellationToken);
        }
        catch (ValidationException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "list_tickets", UseStructuredContent = true, ReadOnly = true)]
    [Description("List all Tickets newest first, then highest ID for tied creation times. No filters or pagination.")]
    public async Task<IReadOnlyList<TicketResponse>> ListTicketsAsync(CancellationToken cancellationToken) =>
        await tickets.ListAsync(cancellationToken);

    [McpServerTool(Name = "get_dashboard_summary", UseStructuredContent = true, ReadOnly = true)]
    [Description("Count all Tickets, Open Tickets, High or Critical Tickets in any status, and Resolved Tickets. Counts overlap.")]
    public async Task<DashboardSummaryResponse> GetDashboardSummaryAsync(CancellationToken cancellationToken) =>
        await tickets.GetDashboardSummaryAsync(cancellationToken);

    [McpServerTool(Name = "get_ticket", UseStructuredContent = true, ReadOnly = true)]
    [Description("Retrieve a Ticket by its integer ID. Returns an error if it does not exist.")]
    public async Task<TicketResponse> GetTicketAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await tickets.GetAsync(id, cancellationToken);
        }
        catch (NotFoundException exception)
        {
            throw new McpException(exception.Message);
        }
    }
}
