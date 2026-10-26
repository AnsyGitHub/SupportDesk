using System.Text.Json.Serialization;

namespace SupportDesk.Core.Tickets;

[JsonConverter(typeof(StrictEnumJsonConverter<TicketPriority>))]
public enum TicketPriority
{
    Low,
    Medium,
    High,
    Critical
}
