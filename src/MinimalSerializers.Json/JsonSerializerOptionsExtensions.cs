using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MinimalSerializers.Json;

/// <summary>
/// Helpers for wiring generated <see cref="JsonSerializerContext"/> instances into options.
/// </summary>
public static class JsonSerializerOptionsExtensions
{
    /// <summary>
    /// Inserts <typeparamref name="TContext"/>.Default into the TypeInfoResolverChain.
    /// </summary>
    public static JsonSerializerOptions AddMinimalJsonContext<TContext>(
        this JsonSerializerOptions options,
        bool insertAtFront = true
    )
        where TContext : JsonSerializerContext
    {
        ArgumentNullException.ThrowIfNull(options);

        var context = GetDefaultContext<TContext>();
        if (insertAtFront)
        {
            options.TypeInfoResolverChain.Insert(0, context);
        }
        else
        {
            options.TypeInfoResolverChain.Add(context);
        }

        return options;
    }

    private static TContext GetDefaultContext<TContext>()
        where TContext : JsonSerializerContext
    {
        var property = typeof(TContext).GetProperty(
            "Default",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );

        if (property?.GetValue(null) is TContext context)
        {
            return context;
        }

        throw new InvalidOperationException(
            $"Type '{typeof(TContext).FullName}' does not expose a public static Default property of type {typeof(TContext).Name}."
        );
    }
}
