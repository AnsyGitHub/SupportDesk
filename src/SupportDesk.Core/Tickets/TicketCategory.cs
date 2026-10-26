using System.Text.Json.Serialization;

namespace SupportDesk.Core.Tickets;

[JsonConverter(typeof(StrictEnumJsonConverter<TicketCategory>))]
public enum TicketCategory
{
    AccountAccess,
    Billing,
    Technical,
    Other
}
