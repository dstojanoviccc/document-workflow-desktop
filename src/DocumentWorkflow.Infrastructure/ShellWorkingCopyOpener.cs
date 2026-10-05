using System.Diagnostics;
using DocumentWorkflow.Application;

namespace DocumentWorkflow.Infrastructure;

public sealed class ShellWorkingCopyOpener : IWorkingCopyOpener
{
    public void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
