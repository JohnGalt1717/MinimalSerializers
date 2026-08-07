using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Xunit;

namespace MinimalSerializers.Json.Package.Tests;

/// <summary>
/// Package acceptance: one shared pack per class (not per test) and sequential
/// execution so concurrent packs cannot lock Tasks/bin outputs on CI.
/// </summary>
[Collection(nameof(PackageAcceptanceCollection))]
public sealed class SingleBuildTests
{
    private readonly PackageFeedFixture _feed;

    public SingleBuildTests(PackageFeedFixture feed) => _feed = feed;

    [Fact]
    public void Pack_consume_and_single_build_includes_datacontract_roots()
    {
        var consumer = _feed.CreateConsumer("Consumer");
        WriteModelsAndProgram(
            consumer,
            models: """
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
                """,
            program: """
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

        var build1 = _feed.Dotnet(consumer, "build --nologo");
        build1.ExitCode.Should().Be(0, because: build1.Output);
        build1.Output.Should().NotContain("SYSLIB1031");

        var generated = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories);
        generated.Should().NotBeEmpty("generated MinimalJson file should exist after one build");
        var generatedText = File.ReadAllText(generated[0]);
        generatedText.Should().Contain("FooDto");
        generatedText.Should().Contain("TypeInfoPropertyName = \"ListOf_");

        _feed.Dotnet(consumer, "run --no-build --nologo").ExitCode.Should().Be(0);

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
        _feed.Dotnet(consumer, "build --nologo").ExitCode.Should().Be(0);
        var generated2 = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories);
        File.ReadAllText(generated2.Single()).Should().Contain("BarDto");
    }

    [Fact]
    public void ListStarDto_with_EmitList_builds_without_SYSLIB1031()
    {
        var consumer = _feed.CreateConsumer("ConsumerList");
        WriteModelsAndProgram(
            consumer,
            models: """
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
                """,
            program: """
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

        var build = _feed.Dotnet(consumer, "build --nologo");
        build.ExitCode.Should().Be(0, because: build.Output);
        build.Output.Should().NotContain("SYSLIB1031");
        build.Output.Should().NotContain("error CS");

        var generated = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories).Single();
        var text = File.ReadAllText(generated);
        text.Should().Contain("ListMoneyDetailsDto");
        text.Should().Contain("TypeInfoPropertyName = \"ListOf_");
        text.Should().Contain("[JsonSerializable(typeof(global::Consumer.ListMoneyDetailsDto))]");

        _feed.Dotnet(consumer, "run --no-build --nologo").ExitCode.Should().Be(0);
    }

    [Fact]
    public void Generic_dto_inheritance_emits_MSJ0009_and_does_not_fail_discovery()
    {
        var consumer = _feed.CreateConsumer("ConsumerGeneric");
        WriteModelsAndProgram(
            consumer,
            models: """
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
                """,
            program: """
                using System.Text.Json;
                using Consumer;

                var options = new JsonSerializerOptions { TypeInfoResolver = AppJsonSerializerContext.Default };
                if (options.GetTypeInfo(typeof(HolderDto)) is null) throw new Exception("HolderDto missing");
                Console.WriteLine("ok-generic");
                """
        );

        var build = _feed.Dotnet(consumer, "build --nologo");
        // Discovery must surface MSJ0009 rather than only a cryptic STJ CS0102.
        build.Output.Should().Contain("MSJ0009", because: build.Output);

        var generated = Directory.GetFiles(consumer, "*.MinimalJson.g.cs", SearchOption.AllDirectories);
        generated.Should().NotBeEmpty(because: build.Output);
        var text = File.ReadAllText(generated[0]);
        text.Should().Contain("QueryGroupResultDto");
        text.Should().Contain("QuerySubGroupResultDto");
        text.Should().Contain("HolderDto");
    }

    private static void WriteModelsAndProgram(string consumer, string models, string program)
    {
        File.WriteAllText(Path.Combine(consumer, "Models.cs"), models);
        File.WriteAllText(Path.Combine(consumer, "Program.cs"), program);
    }
}

[CollectionDefinition(nameof(PackageAcceptanceCollection), DisableParallelization = true)]
public sealed class PackageAcceptanceCollection : ICollectionFixture<PackageFeedFixture>;

/// <summary>
/// Packs MinimalSerializers.Json once and reuses the local feed for all package tests.
/// </summary>
public sealed class PackageFeedFixture : IDisposable
{
    public PackageFeedFixture()
    {
        RepoRoot = FindRepoRoot();
        Artifacts = Path.Combine(Path.GetTempPath(), "minimaljson-pkg-tests", Guid.NewGuid().ToString("N"));
        Feed = Path.Combine(Artifacts, "feed");
        Directory.CreateDirectory(Feed);

        var baseVersion = ReadPackageVersion(RepoRoot);
        // Unique version avoids colliding with a stale global NuGet cache entry.
        PackageVersion = baseVersion + "-pkg." + Guid.NewGuid().ToString("N")[..8];

        // Single pack for the whole class. Do not pack per-test (slow + file-lock risk on CI).
        var pack = Dotnet(
            RepoRoot,
            $"pack \"{Path.Combine(RepoRoot, "src/MinimalSerializers.Json/MinimalSerializers.Json.csproj")}\" -c Release -o \"{Feed}\" --nologo -p:Version={PackageVersion} -p:PackageVersion={PackageVersion}"
        );
        if (pack.ExitCode != 0)
        {
            throw new InvalidOperationException($"pack failed\n{pack.Output}");
        }
    }

    public string RepoRoot { get; }
    public string Artifacts { get; }
    public string Feed { get; }
    public string PackageVersion { get; }

    public string CreateConsumer(string name)
    {
        var consumer = Path.Combine(Artifacts, name);
        Directory.CreateDirectory(consumer);
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
                <add key="local" value="{Feed}" />
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
                <PackageReference Include="MinimalSerializers.Json" Version="{PackageVersion}" />
              </ItemGroup>
            </Project>
            """
        );
        return consumer;
    }

    public (int ExitCode, string Output) Dotnet(string cwd, string args) =>
        Run(cwd, "dotnet", args);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Artifacts))
            {
                Directory.Delete(Artifacts, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
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

    private static (int ExitCode, string Output) Run(string cwd, string fileName, string args)
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
        var output = new StringBuilder();
        output.Append(stdout);
        if (stderr.Length > 0)
        {
            output.Append('\n').Append(stderr);
        }

        return (p.ExitCode, output.ToString());
    }
}
