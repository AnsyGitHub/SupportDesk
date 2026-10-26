namespace SupportDesk.Core.Tickets;

public record DashboardSummaryResponse(int Total, int Open, int HighPriority, int Resolved);
