using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalSerializers.Json;

namespace MinimalSerializers.Json.Tests.Infrastructure;

internal static class CompilationHelper
{
    public static CSharpCompilation Create(params string[] sources)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, parseOptions, path: $"Source{i}.cs")).ToList();

        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DataContractAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(JsonSerializerContext).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MinimalJsonSerializerContextAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
        };

        // netcore shared framework refs
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (!string.IsNullOrEmpty(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                var name = Path.GetFileName(path);
                if (
                    name is "System.Runtime.dll"
                        or "netstandard.dll"
                        or "System.Collections.dll"
                        or "System.Private.CoreLib.dll"
                        or "System.Memory.dll"
                )
                {
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        return CSharpCompilation.Create(
            "Tests",
            trees,
            refs.DistinctBy(r => r.Display).ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }
}
