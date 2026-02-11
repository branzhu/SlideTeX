// SlideTeX Note: Contract DTO used for DebugHost message exchange with the web runtime.

namespace SlideTeX.DebugHost.Contracts;

/// <summary>
/// Command identifiers emitted by WebUI into the debug host.
/// </summary>
public enum HostCommandType
{
    OpenPane = 0,
    Insert = 1,
    Update = 2,
    EditSelected = 3
}


