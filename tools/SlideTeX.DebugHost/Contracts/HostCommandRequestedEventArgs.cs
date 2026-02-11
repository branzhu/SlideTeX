// SlideTeX Note: Contract DTO used for DebugHost message exchange with the web runtime.

namespace SlideTeX.DebugHost.Contracts;

/// <summary>
/// Event args carrying command payload emitted by the debug host bridge.
/// </summary>
public sealed class HostCommandRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Creates event args for command request callback.
    /// </summary>
    public HostCommandRequestedEventArgs(HostCommandPayload payload)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    /// <summary>
    /// Command payload requested by web runtime.
    /// </summary>
    public HostCommandPayload Payload { get; }
}


