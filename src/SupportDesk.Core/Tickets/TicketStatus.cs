using System.Text.Json.Serialization;

namespace SupportDesk.Core.Tickets;

[JsonConverter(typeof(StrictEnumJsonConverter<TicketStatus>))]
public enum TicketStatus
{
    Open,
    InProgress,
    Resolved,
    Closed
}
