namespace SupportDesk.Core.Errors;

public class AiProviderException(bool isTimeout = false) : Exception(
    isTimeout ? "The AI provider timed out." : "The AI provider is unavailable or returned invalid Analysis.")
{
    public bool IsTimeout { get; } = isTimeout;
}
