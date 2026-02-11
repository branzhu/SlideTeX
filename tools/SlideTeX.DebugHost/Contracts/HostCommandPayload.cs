// SlideTeX Note: Contract DTO used for DebugHost message exchange with the web runtime.

namespace SlideTeX.DebugHost.Contracts;

/// <summary>
/// Envelope describing a command request raised by the debug host bridge.
/// </summary>
public sealed class HostCommandPayload
{
    /// <summary>
    /// Creates a command payload with optional argument text.
    /// </summary>
    public HostCommandPayload(HostCommandType commandType, string? argument = null)
    {
        CommandType = commandType;
        Argument = argument;
    }

    /// <summary>
    /// Command identifier requested by the web runtime.
    /// </summary>
    public HostCommandType CommandType { get; }

    /// <summary>
    /// Optional command argument.
    /// </summary>
    public string? Argument { get; }
}


