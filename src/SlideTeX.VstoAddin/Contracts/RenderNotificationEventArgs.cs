// SlideTeX Note: Host command and notification contract shared across add-in components.

using System;

namespace SlideTeX.VstoAddin.Contracts
{
    /// <summary>
    /// Event args carrying render status notifications sent from WebUI to host.
    /// </summary>
    public sealed class RenderNotificationEventArgs : EventArgs
    {
        /// <summary>
        /// Creates render notification payload with success flag, payload, and error message.
        /// </summary>
        public RenderNotificationEventArgs(bool isSuccess, string payload, string errorMessage)
        {
            IsSuccess = isSuccess;
            Payload = payload;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Indicates whether render succeeded on the web side.
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// JSON payload for successful render result.
        /// </summary>
        public string Payload { get; }

        /// <summary>
        /// Error message for failed render notifications.
        /// </summary>
        public string ErrorMessage { get; }
    }
}


