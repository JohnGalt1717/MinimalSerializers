using System.Diagnostics;
using FluentAssertions;

namespace MinimalSerializers.Json.Package.Tests;

public sealed class SingleBuildTests
{
    [Fact]
    public void Pack_consume_and_single_build_includes_datacontract_roots()
    {
        var repoRoot = FindRepoRoot();
        var artifacts = Path.Combine(Path.GetTempPath(), "minimaljson-pkg-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        var feed = Path.Combine(artifacts, "feed");
        Directory.CreateDirectory(feed);

        Run(repoRoot, "dotnet", $"pack \"{Path.Combine(repoRoot, "src/MinimalSerializers.Json/MinimalSerializers.Json.csproj")}\" -c Release -o \"{feed}\" --nologo");

        var consumer = Path.Combine(artifacts, "Consumer");
        Directory.CreateDirectory(consumer);

        // Isolate from any ambient Directory.Build.props / CPM.
        File.WriteAllText(
            Path.Combine(consumer, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """
        );
        File.WriteAllText(
            Path.Combine(consumer, "NuGet.Config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """
        );
        File.WriteAllText(
            Path.Combine(consumer, "Consumer.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="MinimalSerializers.Json" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        File.WriteAllText(
            Path.Combine(consumer, "Models.cs"),
            """
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;

            namespace Consumer;

            [DataContract]
            public sealed class FooDto
            {
                [DataMember]
                public required string Name { get; init; }
            }

            [MinimalJsonSerializerContext]
            public partial class ConsumerJsonContext : JsonSerializerContext;
            """
        );

        File.WriteAllText(
            Path.Combine(consumer, "Program.cs"),
            """
            using System.Text.Json;
            using Consumer;

            var options = new JsonSerializerOptions { TypeInfoResolver = ConsumerJsonContext.Default };
            if (options.GetTypeInfo(typeof(FooDto)) is null) throw new Exception("FooDto missing");
            if (options.GetTypeInfo(typeof(FooDto[])) is null) throw new Exception("FooDto[] missing");
            if (options.GetTypeInfo(typeof(List<FooDto>)) is null) throw new Exception("List<FooDto> missing");
            var json = JsonSerializer.Serialize(new FooDto { Name = "x" }, options);
            _ = JsonSerializer.Deserialize<FooDto>(json, options);
            Console.WriteLine("ok");
            """
        );

        Run(consumer, "dotnet", "restore --nologo");
        Run(consumer, "dotnet", "build --no-restore --nologo");
        var generated = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories);
        generated.Should().NotBeEmpty("generated MinimalJson file should exist after one build");
        File.ReadAllText(generated[0]).Should().Contain("FooDto");

        Run(consumer, "dotnet", "run --no-build --nologo");

        File.AppendAllText(
            Path.Combine(consumer, "Models.cs"),
            """

            [DataContract]
            public sealed class BarDto
            {
                [DataMember]
                public required int Value { get; init; }
            }
            """
        );
        Run(consumer, "dotnet", "build --nologo");
        var generated2 = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories);
        File.ReadAllText(generated2.Single()).Should().Contain("BarDto");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MinimalSerializers.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }

    private static void Run(string cwd, string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {args}\nEXIT {p.ExitCode}\n{stdout}\n{stderr}");
        }
    }
}
