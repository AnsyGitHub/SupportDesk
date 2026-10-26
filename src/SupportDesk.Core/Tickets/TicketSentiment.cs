using System.Text.Json.Serialization;

namespace SupportDesk.Core.Tickets;

[JsonConverter(typeof(StrictEnumJsonConverter<TicketSentiment>))]
public enum TicketSentiment
{
    Positive,
    Neutral,
    Negative
}
