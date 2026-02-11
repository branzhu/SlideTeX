// SlideTeX Note: COM automation bridge consumed by the embedded editor runtime.

using System;
using System.Runtime.InteropServices;
using SlideTeX.VstoAddin.Contracts;

namespace SlideTeX.VstoAddin.Hosting
{
    /// <summary>
    /// COM-visible bridge object consumed by WebView JavaScript to send host callbacks.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public sealed class SlideTeXHostObject
    {
        public event EventHandler<RenderNotificationEventArgs> RenderNotificationReceived;
        public event EventHandler<HostCommandRequestedEventArgs> CommandRequested;

        /// <summary>
        /// Receives successful render payloads from WebUI.
        /// </summary>
        public void notifyRenderSuccess(string jsonPayload)
        {
            var handler = RenderNotificationReceived;
            if (handler != null)
            {
                handler(this, new RenderNotificationEventArgs(true, jsonPayload, null));
            }
        }

        /// <summary>
        /// Receives render failure notifications from WebUI.
        /// </summary>
        public void notifyRenderError(string errorMessage)
        {
            var handler = RenderNotificationReceived;
            if (handler != null)
            {
                handler(this, new RenderNotificationEventArgs(false, null, errorMessage));
            }
        }

        /// <summary>
        /// Requests host-side insert action for the latest render result.
        /// </summary>
        public void requestInsert()
        {
            RaiseCommand(new HostCommandPayload(HostCommandType.Insert));
        }

        /// <summary>
        /// Requests host-side update action for the selected formula shape.
        /// </summary>
        public void requestUpdate()
        {
            RaiseCommand(new HostCommandPayload(HostCommandType.Update));
        }

        /// <summary>
        /// Requests opening the task pane from JavaScript.
        /// </summary>
        public void requestOpenPane()
        {
            RaiseCommand(new HostCommandPayload(HostCommandType.OpenPane));
        }

        /// <summary>
        /// Requests editing of the currently selected SlideTeX shape.
        /// </summary>
        public void requestEditSelected()
        {
            RaiseCommand(new HostCommandPayload(HostCommandType.EditSelected));
        }

        /// <summary>
        /// Requests equation renumbering across all slides.
        /// </summary>
        public void requestRenumber()
        {
            RaiseCommand(new HostCommandPayload(HostCommandType.Renumber));
        }

        /// <summary>
        /// Normalizes command event dispatch through a single payload shape.
        /// </summary>
        private void RaiseCommand(HostCommandPayload payload)
        {
            var handler = CommandRequested;
            if (handler != null)
            {
                handler(this, new HostCommandRequestedEventArgs(payload));
            }
        }
    }
}


