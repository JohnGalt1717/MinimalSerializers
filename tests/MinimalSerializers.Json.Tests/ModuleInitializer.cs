using System.Runtime.CompilerServices;

namespace MinimalSerializers.Json.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        Verifier.UseProjectRelativeDirectory("Snapshots");
    }
}
