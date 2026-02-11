// SlideTeX Note: COM-visible object that exposes host callbacks to JavaScript in DebugHost.

using System.Runtime.InteropServices;
using SlideTeX.DebugHost.Contracts;

namespace SlideTeX.DebugHost.Hosting;

/// <summary>
/// Debug bridge object that mimics the COM API exposed in the Office host.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class SlideTeXHostObject
{
    public event EventHandler<RenderNotificationEventArgs>? RenderNotificationReceived;
    public event EventHandler? InsertRequested;
    public event EventHandler? UpdateRequested;
    public event EventHandler<HostCommandRequestedEventArgs>? CommandRequested;

    /// <summary>
    /// Passes successful render notifications from WebUI to the host test harness.
    /// </summary>
    public void notifyRenderSuccess(string jsonPayload)
    {
        RenderNotificationReceived?.Invoke(this, new RenderNotificationEventArgs(true, jsonPayload, null));
    }

    /// <summary>
    /// Passes render failure notifications from WebUI to the host test harness.
    /// </summary>
    public void notifyRenderError(string errorMessage)
    {
        RenderNotificationReceived?.Invoke(this, new RenderNotificationEventArgs(false, null, errorMessage));
    }

    /// <summary>
    /// Emits insert command events for manual command-routing verification.
    /// </summary>
    public void requestInsert()
    {
        InsertRequested?.Invoke(this, EventArgs.Empty);
        CommandRequested?.Invoke(this, new HostCommandRequestedEventArgs(new HostCommandPayload(HostCommandType.Insert)));
    }

    /// <summary>
    /// Emits update command events for manual command-routing verification.
    /// </summary>
    public void requestUpdate()
    {
        UpdateRequested?.Invoke(this, EventArgs.Empty);
        CommandRequested?.Invoke(this, new HostCommandRequestedEventArgs(new HostCommandPayload(HostCommandType.Update)));
    }

    /// <summary>
    /// Emits a command request to open the pane.
    /// </summary>
    public void requestOpenPane()
    {
        CommandRequested?.Invoke(this, new HostCommandRequestedEventArgs(new HostCommandPayload(HostCommandType.OpenPane)));
    }

    /// <summary>
    /// Emits a command request to edit selected formula content.
    /// </summary>
    public void requestEditSelected()
    {
        CommandRequested?.Invoke(this, new HostCommandRequestedEventArgs(new HostCommandPayload(HostCommandType.EditSelected)));
    }
}


