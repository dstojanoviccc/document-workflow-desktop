using System.Reflection;

namespace DocumentWorkflow.App;

public static class ApplicationVersion
{
    public static string Display => "Version " + (typeof(ApplicationVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Unknown");
}
