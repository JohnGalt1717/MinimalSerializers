namespace MinimalSerializers.Json.Discovery;

public enum DiscoveryDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed class DiscoveryDiagnostic
{
    public DiscoveryDiagnostic(
        string id,
        DiscoveryDiagnosticSeverity severity,
        string message,
        string? path = null,
        int? line = null
    )
    {
        Id = id;
        Severity = severity;
        Message = message;
        Path = path;
        Line = line;
    }

    public string Id { get; }
    public DiscoveryDiagnosticSeverity Severity { get; }
    public string Message { get; }
    public string? Path { get; }
    public int? Line { get; }

    public override string ToString() => $"{Id}: {Message}";
}
