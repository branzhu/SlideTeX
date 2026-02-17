// SlideTeX Note: Host command and notification contract shared across add-in components.

namespace SlideTeX.VstoAddin.Contracts
{
    /// <summary>
    /// Command identifiers that WebUI can request from the host.
    /// </summary>
    public enum HostCommandType
    {
        OpenPane = 0,
        Insert = 1,
        Update = 2,
        EditSelected = 3,
        Renumber = 4,
        TagSelected = 5
    }
}


