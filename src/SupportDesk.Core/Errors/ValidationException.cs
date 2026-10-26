namespace SupportDesk.Core.Errors;

public class ValidationException(string message) : Exception(message);
