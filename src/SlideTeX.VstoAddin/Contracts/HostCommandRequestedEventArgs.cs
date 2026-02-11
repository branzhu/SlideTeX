// SlideTeX Note: Host command and notification contract shared across add-in components.

using System;

namespace SlideTeX.VstoAddin.Contracts
{
    /// <summary>
    /// Event args wrapper carrying host command request payload.
    /// </summary>
    public sealed class HostCommandRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates event args for a host command request.
        /// </summary>
        public HostCommandRequestedEventArgs(HostCommandPayload payload)
        {
            Payload = payload ?? throw new ArgumentNullException("payload");
        }

        /// <summary>
        /// Command payload forwarded from the host bridge callback.
        /// </summary>
        public HostCommandPayload Payload { get; }
    }
}


