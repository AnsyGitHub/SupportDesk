using System.Text.Json.Serialization;

namespace SupportDesk.Core.Tickets;

public class StrictEnumJsonConverter<TEnum>() : JsonStringEnumConverter<TEnum>(allowIntegerValues: false)
    where TEnum : struct, Enum;
