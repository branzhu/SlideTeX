// SlideTeX Note: Contract DTO used for DebugHost message exchange with the web runtime.

namespace SlideTeX.DebugHost.Contracts;

/// <summary>
/// Event args carrying render notification details emitted by WebUI.
/// </summary>
public sealed class RenderNotificationEventArgs : EventArgs
{
    /// <summary>
    /// Creates render notification payload with optional payload/error fields.
    /// </summary>
    public RenderNotificationEventArgs(bool isSuccess, string? payload, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Payload = payload;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Indicates whether web-side render succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// JSON payload for successful render callbacks.
    /// </summary>
    public string? Payload { get; }

    /// <summary>
    /// Error message for failed render callbacks.
    /// </summary>
    public string? ErrorMessage { get; }
}


