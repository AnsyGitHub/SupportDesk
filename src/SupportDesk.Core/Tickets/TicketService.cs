using Microsoft.EntityFrameworkCore;
using SupportDesk.Core.Data;
using SupportDesk.Core.Errors;

namespace SupportDesk.Core.Tickets;

public class TicketService(SupportDeskDbContext db, IAiTicketService? ai = null)
{
    public async Task<TicketResponse> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        var title = ValidateText(request.Title, "Title", 200);
        var description = ValidateText(request.Description, "Description", 5000);
        var now = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Title = title,
            Description = description,
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(ticket);
    }

    public async Task<TicketResponse> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Ticket {id} was not found.");
        return ToResponse(ticket);
    }

    public async Task<IReadOnlyList<TicketResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tickets = await db.Tickets.AsNoTracking()
            .OrderByDescending(ticket => ticket.CreatedAt)
            .ThenByDescending(ticket => ticket.Id)
            .ToListAsync(cancellationToken);
        return tickets.Select(ToResponse).ToList();
    }

    public async Task<DashboardSummaryResponse> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        var summary = await db.Tickets.AsNoTracking().GroupBy(ticket => 1)
            .Select(group => new DashboardSummaryResponse(
                group.Count(),
                group.Count(ticket => ticket.Status == TicketStatus.Open),
                group.Count(ticket => ticket.Priority == TicketPriority.High || ticket.Priority == TicketPriority.Critical),
                group.Count(ticket => ticket.Status == TicketStatus.Resolved)))
            .SingleOrDefaultAsync(cancellationToken);
        return summary ?? new DashboardSummaryResponse(0, 0, 0, 0);
    }

    public async Task<TicketResponse> UpdateAsync(int id, UpdateTicketRequest request, CancellationToken cancellationToken = default)
    {
        var title = ValidateText(request.Title, "Title", 200);
        var description = ValidateText(request.Description, "Description", 5000);
        if (request.Priority is not { } priority || !Enum.IsDefined(priority))
        {
            throw new ValidationException("Priority must be Low, Medium, High, or Critical.");
        }

        var ticket = await db.Tickets.SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Ticket {id} was not found.");
        if (ticket.Title != title || ticket.Description != description)
        {
            ticket.Category = null;
            ticket.Sentiment = null;
            ticket.AiSummary = null;
            ticket.SuggestedAction = null;
        }

        ticket.Title = title;
        ticket.Description = description;
        ticket.Priority = priority;
        ticket.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(ticket);
    }

    public async Task<TicketResponse> UpdateStatusAsync(int id, UpdateTicketStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Status is not { } status || !Enum.IsDefined(status))
        {
            throw new ValidationException("Status must be Open, InProgress, Resolved, or Closed.");
        }

        var ticket = await db.Tickets.SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Ticket {id} was not found.");
        ticket.Status = status;
        ticket.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(ticket);
    }

    public async Task<TicketAnalysisResponse> AnalyzeAsync(int id, CancellationToken cancellationToken = default)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Ticket {id} was not found.");
        if (ai is null)
        {
            throw new AiProviderException();
        }

        var analysis = await ai.AnalyzeAsync(ticket.Title, ticket.Description, cancellationToken);
        var summary = analysis?.Summary?.Trim();
        var suggestedAction = analysis?.SuggestedAction?.Trim();
        if (analysis?.Category is not { } category || !Enum.IsDefined(category)
            || analysis.Priority is not { } priority || !Enum.IsDefined(priority)
            || analysis.Sentiment is not { } sentiment || !Enum.IsDefined(sentiment)
            || string.IsNullOrEmpty(summary) || summary.Length > 1000
            || string.IsNullOrEmpty(suggestedAction) || suggestedAction.Length > 2000)
        {
            throw new AiProviderException();
        }

        ticket.Category = category;
        ticket.Priority = priority;
        ticket.Sentiment = sentiment;
        ticket.AiSummary = summary;
        ticket.SuggestedAction = suggestedAction;
        ticket.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new TicketAnalysisResponse(category, priority, sentiment, summary, suggestedAction);
    }

    private static string ValidateText(string? value, string field, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new ValidationException($"{field} is required.");
        }

        if (text.Length > maxLength)
        {
            throw new ValidationException($"{field} must be {maxLength} characters or fewer.");
        }

        return text;
    }

    private static TicketResponse ToResponse(Ticket ticket) => new(
        ticket.Id, ticket.Title, ticket.Description, ticket.Status, ticket.Priority,
        ticket.Category, ticket.Sentiment, ticket.AiSummary, ticket.SuggestedAction,
        ticket.CreatedAt, ticket.UpdatedAt);
}
