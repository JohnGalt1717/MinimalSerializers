using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalSerializers.Json.Discovery;
using Task = Microsoft.Build.Utilities.Task;

namespace MinimalSerializers.Json.Tasks;

/// <summary>
/// MSBuild task that discovers DataContract graphs and writes [JsonSerializable] partial contexts
/// before CoreCompile so the built-in STJ generator sees them in a single build.
/// </summary>
public sealed class GenerateMinimalJsonSerializerContexts : Task
{
    [Required]
    public ITaskItem[] CompileFiles { get; set; } = [];

    public ITaskItem[] ReferencePaths { get; set; } = [];

    [Required]
    public string OutputDirectory { get; set; } = "";

    public string LangVersion { get; set; } = "latest";

    public bool IncludeArrays { get; set; } = true;
    public bool IncludeList { get; set; } = true;
    public bool IncludeDeclaredCollectionInterfaces { get; set; } = true;
    public bool IncludeDictionaries { get; set; } = true;

    /// <summary>
    /// MSJ0004 open-generic warning mode: summary (default), all, or none.
    /// </summary>
    public string WarnOpenGenerics { get; set; } = "summary";

    [Output]
    public ITaskItem[] GeneratedFiles { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);

            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(
                ParseLangVersion(LangVersion)
            );
            var trees = new List<SyntaxTree>();
            foreach (var item in CompileFiles)
            {
                var path = item.ItemSpec;
                if (!File.Exists(path))
                {
                    continue;
                }

                // Skip previously generated files to avoid feedback loops.
                if (path.EndsWith(".MinimalJson.g.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = File.ReadAllText(path);
                trees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, path));
            }

            var references = new List<MetadataReference>();

            // Prefer full trusted platform set so BCL types (DateOnly, Guid, etc.) resolve.
            var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (!string.IsNullOrEmpty(tpa))
            {
                foreach (
                    var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                )
                {
                    TryAddReference(references, path);
                }
            }
            else
            {
                TryAddReference(references, typeof(object).Assembly.Location);
                TryAddReference(references, typeof(Attribute).Assembly.Location);
                TryAddReference(references, typeof(Enumerable).Assembly.Location);
                TryAddReference(
                    references,
                    typeof(System.Runtime.Serialization.DataContractAttribute).Assembly.Location
                );
                TryAddReference(
                    references,
                    typeof(System.Text.Json.Serialization.JsonSerializerContext).Assembly.Location
                );
            }

            foreach (var item in ReferencePaths)
            {
                TryAddReference(references, item.ItemSpec);
            }

            var compilation = CSharpCompilation.Create(
                assemblyName: "MinimalJson_Discovery_Assembly",
                syntaxTrees: trees,
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            var options = new DiscoveryOptions
            {
                IncludeArrays = IncludeArrays,
                IncludeList = IncludeList,
                IncludeDeclaredCollectionInterfaces = IncludeDeclaredCollectionInterfaces,
                IncludeDictionaries = IncludeDictionaries,
                OpenGenericWarningMode = ParseOpenGenericWarningMode(WarnOpenGenerics),
            };

            var result = JsonSerializableRootCollector.Collect(compilation, options);
            foreach (var d in result.Diagnostics)
            {
                LogDiagnostic(d);
            }

            var generated = new List<ITaskItem>();
            var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var context in result.Contexts)
            {
                foreach (var d in context.Diagnostics)
                {
                    LogDiagnostic(d);
                }

                if (!context.IsPartial || !context.DerivesFromJsonSerializerContext)
                {
                    continue;
                }

                var source = MinimalJsonContextSourceEmitter.Emit(context);
                var fileName = MinimalJsonContextSourceEmitter.GetGeneratedFileName(context);
                var fullPath = Path.Combine(OutputDirectory, fileName);
                WriteIfChanged(fullPath, source);
                generated.Add(new TaskItem(fullPath));
                writtenNames.Add(fileName);
            }

            // Clean stale generated files.
            if (Directory.Exists(OutputDirectory))
            {
                foreach (
                    var existing in Directory.EnumerateFiles(OutputDirectory, "*.MinimalJson.g.cs")
                )
                {
                    var name = Path.GetFileName(existing);
                    if (!writtenNames.Contains(name))
                    {
                        File.Delete(existing);
                    }
                }
            }

            GeneratedFiles = generated.ToArray();

            // Never stamp a failed or empty-but-expected generation run. A stamp without
            // *.MinimalJson.g.cs makes the MSBuild target look up-to-date forever.
            if (Log.HasLoggedErrors)
            {
                DeleteStamp(OutputDirectory);
                return false;
            }

            if (result.Contexts.Any(static c => c.IsPartial && c.DerivesFromJsonSerializerContext)
                && generated.Count == 0)
            {
                Log.LogError(
                    "MSJ0006: MinimalJson contexts were discovered but no generated sources were written."
                );
                DeleteStamp(OutputDirectory);
                return false;
            }

            WriteStamp(OutputDirectory, generated);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError(
                "MSJ0006: Failed to generate MinimalJson serializer contexts: {0}",
                ex.Message
            );
            Log.LogMessage(MessageImportance.Low, ex.ToString());
            return false;
        }
    }

    private void LogDiagnostic(DiscoveryDiagnostic d)
    {
        switch (d.Severity)
        {
            case DiscoveryDiagnosticSeverity.Error:
                Log.LogError("{0}: {1}", d.Id, d.Message);
                break;
            case DiscoveryDiagnosticSeverity.Warning:
                Log.LogWarning("{0}: {1}", d.Id, d.Message);
                break;
            default:
                Log.LogMessage(MessageImportance.Normal, "{0}: {1}", d.Id, d.Message);
                break;
        }
    }

    private static OpenGenericWarningMode ParseOpenGenericWarningMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OpenGenericWarningMode.Summary;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "all" or "verbose" or "each" => OpenGenericWarningMode.All,
            "none" or "off" or "false" or "0" => OpenGenericWarningMode.None,
            "summary" or "one" or "default" or "true" or "1" => OpenGenericWarningMode.Summary,
            _ => OpenGenericWarningMode.Summary,
        };
    }

    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Encoding.UTF8);
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }

    private static void WriteStamp(string outputDirectory, List<ITaskItem> generated)
    {
        Directory.CreateDirectory(outputDirectory);
        var stamp = Path.Combine(outputDirectory, "stamp.minimaljson");
        var payload = new StringBuilder();
        payload.AppendLine(DateTime.UtcNow.ToString("O"));
        foreach (
            var path in generated
                .Select(static g => g.ItemSpec)
                .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
        )
        {
            payload.AppendLine(path);
        }

        File.WriteAllText(stamp, payload.ToString(), Encoding.UTF8);
    }

    private static void DeleteStamp(string outputDirectory)
    {
        var stamp = Path.Combine(outputDirectory, "stamp.minimaljson");
        if (File.Exists(stamp))
        {
            File.Delete(stamp);
        }
    }

    private static void TryAddReference(List<MetadataReference> references, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        if (
            references
                .OfType<PortableExecutableReference>()
                .Any(r => string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase))
        )
        {
            return;
        }

        references.Add(MetadataReference.CreateFromFile(path));
    }

    private static LanguageVersion ParseLangVersion(string? value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Equals("latest", StringComparison.OrdinalIgnoreCase)
            || value.Equals("preview", StringComparison.OrdinalIgnoreCase)
        )
        {
            return LanguageVersion.Preview;
        }

        return LanguageVersionFacts.TryParse(value, out var lv) ? lv : LanguageVersion.Preview;
    }
}
