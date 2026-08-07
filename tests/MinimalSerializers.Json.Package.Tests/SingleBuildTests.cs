using System.Diagnostics;
using FluentAssertions;

namespace MinimalSerializers.Json.Package.Tests;

public sealed class SingleBuildTests
{
    [Fact]
    public void Pack_consume_and_single_build_includes_datacontract_roots()
    {
        var repoRoot = FindRepoRoot();
        var artifacts = CreateArtifactsDir();
        var feed = Path.Combine(artifacts, "feed");
        Directory.CreateDirectory(feed);

        var packageVersion = PackToFeed(repoRoot, feed);
        var consumer = Path.Combine(artifacts, "Consumer");
        Directory.CreateDirectory(consumer);
        WriteIsolatedConsumerProject(consumer, feed, packageVersion);

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
        var build1 = RunCapture(consumer, "dotnet", "build --no-restore --nologo");
        build1.ExitCode.Should().Be(0, because: build1.StdOutAndErr);
        build1.StdOutAndErr.Should().NotContain("SYSLIB1031");
        var generated = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories);
        generated.Should().NotBeEmpty("generated MinimalJson file should exist after one build");
        var generatedText = File.ReadAllText(generated[0]);
        generatedText.Should().Contain("FooDto");
        generatedText.Should().Contain("TypeInfoPropertyName = \"ListOf_");

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

    [Fact]
    public void ListStarDto_with_EmitList_builds_without_SYSLIB1031()
    {
        var repoRoot = FindRepoRoot();
        var artifacts = CreateArtifactsDir();
        var feed = Path.Combine(artifacts, "feed");
        Directory.CreateDirectory(feed);

        var packageVersion = PackToFeed(repoRoot, feed);
        var consumer = Path.Combine(artifacts, "ConsumerList");
        Directory.CreateDirectory(consumer);
        WriteIsolatedConsumerProject(consumer, feed, packageVersion);

        File.WriteAllText(
            Path.Combine(consumer, "Models.cs"),
            """
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;

            namespace Consumer;

            [DataContract]
            public sealed record MoneyDetailsDto
            {
                [DataMember] public required string Id { get; init; }
            }

            [DataContract]
            public sealed record ListMoneyDetailsDto
            {
                [DataMember] public required MoneyDetailsDto[] Items { get; init; }
            }

            [DataContract]
            public sealed record ListOrderDto
            {
                [DataMember] public required string Id { get; init; }
            }

            [MinimalJsonSerializerContext]
            [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
            public partial class AppJsonSerializerContext : JsonSerializerContext;
            """
        );

        File.WriteAllText(
            Path.Combine(consumer, "Program.cs"),
            """
            using System.Text.Json;
            using Consumer;

            var options = new JsonSerializerOptions { TypeInfoResolver = AppJsonSerializerContext.Default };
            if (options.GetTypeInfo(typeof(ListMoneyDetailsDto)) is null) throw new Exception("ListMoneyDetailsDto missing");
            if (options.GetTypeInfo(typeof(ListOrderDto)) is null) throw new Exception("ListOrderDto missing");
            if (options.GetTypeInfo(typeof(MoneyDetailsDto)) is null) throw new Exception("MoneyDetailsDto missing");
            if (options.GetTypeInfo(typeof(List<MoneyDetailsDto>)) is null) throw new Exception("List<MoneyDetailsDto> missing");
            if (options.GetTypeInfo(typeof(List<ListMoneyDetailsDto>)) is null) throw new Exception("List<ListMoneyDetailsDto> missing");
            if (options.GetTypeInfo(typeof(ListMoneyDetailsDto[])) is null) throw new Exception("ListMoneyDetailsDto[] missing");

            var payload = new ListMoneyDetailsDto
            {
                Items = [new MoneyDetailsDto { Id = "1" }],
            };
            var json = JsonSerializer.Serialize(payload, options);
            _ = JsonSerializer.Deserialize<ListMoneyDetailsDto>(json, options);
            Console.WriteLine("ok-list");
            """
        );

        Run(consumer, "dotnet", "restore --nologo");
        var build = RunCapture(consumer, "dotnet", "build --no-restore --nologo");
        build.ExitCode.Should().Be(0, because: build.StdOutAndErr);
        build.StdOutAndErr.Should().NotContain("SYSLIB1031");
        build.StdOutAndErr.Should().NotContain("error CS");

        var generated = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories).Single();
        var text = File.ReadAllText(generated);
        text.Should().Contain("ListMoneyDetailsDto");
        text.Should().Contain("TypeInfoPropertyName = \"ListOf_");
        text.Should().Contain("[JsonSerializable(typeof(global::Consumer.ListMoneyDetailsDto))]");

        Run(consumer, "dotnet", "run --no-build --nologo");
    }

    [Fact]
    public void Generic_dto_inheritance_emits_MSJ0009_and_does_not_fail_discovery()
    {
        var repoRoot = FindRepoRoot();
        var artifacts = CreateArtifactsDir();
        var feed = Path.Combine(artifacts, "feed");
        Directory.CreateDirectory(feed);

        var packageVersion = PackToFeed(repoRoot, feed);
        var consumer = Path.Combine(artifacts, "ConsumerGeneric");
        Directory.CreateDirectory(consumer);
        WriteIsolatedConsumerProject(consumer, feed, packageVersion);

        File.WriteAllText(
            Path.Combine(consumer, "Models.cs"),
            """
            using System;
            using System.Collections.Generic;
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;

            namespace Consumer;

            public enum CategoryFields
            {
                Name = 0,
                Code = 1,
            }

            [DataContract]
            public record QuerySubGroupResultDto<TFields>
                where TFields : Enum
            {
                [DataMember] public required TFields FieldName { get; init; }
                [DataMember] public required string? Value { get; init; }
            }

            [DataContract]
            public sealed record QueryGroupResultDto<TFields> : QuerySubGroupResultDto<TFields>
                where TFields : Enum
            {
                [DataMember]
                public IReadOnlyCollection<QuerySubGroupResultDto<TFields>>? SubGroupResults { get; init; }
            }

            [DataContract]
            public sealed record HolderDto
            {
                [DataMember]
                public required QueryGroupResultDto<CategoryFields> Group { get; init; }
            }

            [MinimalJsonSerializerContext]
            public partial class AppJsonSerializerContext : JsonSerializerContext;
            """
        );

        File.WriteAllText(
            Path.Combine(consumer, "Program.cs"),
            """
            using System.Text.Json;
            using Consumer;

            // Prefer options GetTypeInfo so we do not depend on STJ nested accessor naming.
            var options = new JsonSerializerOptions { TypeInfoResolver = AppJsonSerializerContext.Default };
            if (options.GetTypeInfo(typeof(HolderDto)) is null) throw new Exception("HolderDto missing");
            Console.WriteLine("ok-generic");
            """
        );

        Run(consumer, "dotnet", "restore --nologo");
        var build = RunCapture(consumer, "dotnet", "build --no-restore --nologo");

        // Discovery must surface MSJ0009 rather than only a cryptic STJ CS0102.
        build.StdOutAndErr.Should().Contain("MSJ0009", because: build.StdOutAndErr);

        var generated = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories);
        generated.Should().NotBeEmpty(because: build.StdOutAndErr);
        var text = File.ReadAllText(generated[0]);
        text.Should().Contain("QueryGroupResultDto");
        text.Should().Contain("QuerySubGroupResultDto");
        text.Should().Contain("HolderDto");
    }

    private static string CreateArtifactsDir()
    {
        var artifacts = Path.Combine(Path.GetTempPath(), "minimaljson-pkg-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        return artifacts;
    }

    private static void WriteIsolatedConsumerProject(string consumer, string feed, string packageVersion)
    {
        Directory.CreateDirectory(Path.Combine(consumer, "packages"));
        File.WriteAllText(
            Path.Combine(consumer, "Directory.Build.props"),
            $$"""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                <RestorePackagesPath>{{Path.Combine(consumer, "packages")}}</RestorePackagesPath>
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
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="MinimalSerializers.Json" Version="{packageVersion}" />
              </ItemGroup>
            </Project>
            """
        );
    }

    private static string PackToFeed(string repoRoot, string feed)
    {
        var packageVersion = ReadPackageVersion(repoRoot);
        // Unique version per run avoids colliding with a stale global NuGet cache entry.
        var uniqueVersion = packageVersion + "-pkg." + Guid.NewGuid().ToString("N")[..8];
        Run(
            repoRoot,
            "dotnet",
            $"pack \"{Path.Combine(repoRoot, "src/MinimalSerializers.Json/MinimalSerializers.Json.csproj")}\" -c Release -o \"{feed}\" --nologo -p:Version={uniqueVersion} -p:PackageVersion={uniqueVersion}"
        );
        return uniqueVersion;
    }

    private static string ReadPackageVersion(string repoRoot)
    {
        var props = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"));
        const string marker = "<VersionPrefix>";
        var start = props.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("VersionPrefix not found");
        }

        start += marker.Length;
        var end = props.IndexOf('<', start);
        return props[start..end].Trim();
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
        var result = RunCapture(cwd, fileName, args);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {args}\nEXIT {result.ExitCode}\n{result.StdOutAndErr}");
        }
    }

    private static (int ExitCode, string StdOutAndErr) RunCapture(string cwd, string fileName, string args)
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
        return (p.ExitCode, stdout + "\n" + stderr);
    }
}
