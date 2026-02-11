// SlideTeX Note: Host command and notification contract shared across add-in components.

namespace SlideTeX.VstoAddin.Contracts
{
    /// <summary>
    /// Payload envelope for a command request raised by the WebUI host bridge.
    /// </summary>
    public sealed class HostCommandPayload
    {
        /// <summary>
        /// Creates a command payload with optional argument text.
        /// </summary>
        public HostCommandPayload(HostCommandType commandType, string argument = null)
        {
            CommandType = commandType;
            Argument = argument;
        }

        /// <summary>
        /// Command identifier requested by the web runtime.
        /// </summary>
        public HostCommandType CommandType { get; }

        /// <summary>
        /// Optional command argument serialized as plain text.
        /// </summary>
        public string Argument { get; }
    }
}


