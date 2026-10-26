namespace SupportDesk.Core.Errors;

public class NotFoundException(string message) : Exception(message);
