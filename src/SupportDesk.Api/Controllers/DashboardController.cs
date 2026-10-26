using Microsoft.AspNetCore.Mvc;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(TicketService tickets) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken) =>
        await tickets.GetDashboardSummaryAsync(cancellationToken);
}
