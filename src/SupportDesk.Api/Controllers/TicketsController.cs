using Microsoft.AspNetCore.Mvc;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketsController(TicketService tickets) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketResponse>> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var ticket = await tickets.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetAsync), new { id = ticket.Id }, ticket);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TicketResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<TicketResponse>> ListAsync(CancellationToken cancellationToken) =>
        await tickets.ListAsync(cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> GetAsync(int id, CancellationToken cancellationToken) =>
        await tickets.GetAsync(id, cancellationToken);
}
