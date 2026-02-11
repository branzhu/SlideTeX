// SlideTeX Note: Entry point for launching the local DebugHost harness.

namespace SlideTeX.DebugHost;

internal static class Program
{
    [STAThread]
    // Starts WinForms debug harness with standard application configuration.
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

