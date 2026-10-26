using Microsoft.EntityFrameworkCore;
using SupportDesk.Core.Data;
using SupportDesk.Core.Errors;

namespace SupportDesk.Core.Tickets;

public class TicketService(SupportDeskDbContext db)
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
