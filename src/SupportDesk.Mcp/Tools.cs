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

    [McpServerTool(Name = "update_ticket", UseStructuredContent = true)]
    [Description("Edit a Ticket's title, description, and priority, including Resolved or Closed Tickets. Status uses update_ticket_status.")]
    public async Task<TicketResponse> UpdateTicketAsync(
        int id,
        [Description("Required title, at most 200 characters after trimming.")] string title,
        [Description("Required description, at most 5000 characters after trimming.")] string description,
        [Description("Required priority: Low, Medium, High, or Critical.")] TicketPriority? priority,
        CancellationToken cancellationToken)
    {
        try
        {
            return await tickets.UpdateAsync(id, new UpdateTicketRequest(title, description, priority), cancellationToken);
        }
        catch (Exception exception) when (exception is ValidationException or NotFoundException)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "update_ticket_status", UseStructuredContent = true)]
    [Description("Set a Ticket's status to Open, InProgress, Resolved, or Closed. Reopening and repeated statuses succeed.")]
    public async Task<TicketResponse> UpdateTicketStatusAsync(
        int id,
        [Description("Required status: Open, InProgress, Resolved, or Closed.")] TicketStatus? status,
        CancellationToken cancellationToken)
    {
        try
        {
            return await tickets.UpdateStatusAsync(id, new UpdateTicketStatusRequest(status), cancellationToken);
        }
        catch (Exception exception) when (exception is ValidationException or NotFoundException)
        {
            throw new McpException(exception.Message);
        }
    }

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
