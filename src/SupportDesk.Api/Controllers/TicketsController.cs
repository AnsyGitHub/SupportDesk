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

    [HttpPost("{id:int}/analyze")]
    [ProducesResponseType<TicketAnalysisResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout)]
    public async Task<TicketAnalysisResponse> AnalyzeAsync(int id, CancellationToken cancellationToken) =>
        await tickets.AnalyzeAsync(id, cancellationToken);

    [HttpPut("{id:int}")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<TicketResponse> UpdateAsync(int id, UpdateTicketRequest request, CancellationToken cancellationToken) =>
        await tickets.UpdateAsync(id, request, cancellationToken);

    [HttpPatch("{id:int}/status")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<TicketResponse> UpdateStatusAsync(int id, UpdateTicketStatusRequest request, CancellationToken cancellationToken) =>
        await tickets.UpdateStatusAsync(id, request, cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> GetAsync(int id, CancellationToken cancellationToken) =>
        await tickets.GetAsync(id, cancellationToken);
}
