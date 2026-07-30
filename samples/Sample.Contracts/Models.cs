using System.Runtime.Serialization;

namespace Sample.Contracts;

public enum OrderStatus
{
    [EnumMember(Value = "pending")]
    Pending = 0,
    [EnumMember(Value = "shipped")]
    Shipped = 1,
    [EnumMember(Value = "cancelled")]
    Cancelled = 2,
}

[DataContract]
public sealed record AddressDto
{
    [DataMember] public required string Line1 { get; init; }
    [DataMember] public string? Line2 { get; init; }
    [DataMember] public required string City { get; init; }
    [DataMember] public required string Country { get; init; }
}

[DataContract]
public sealed record OrderItemDto
{
    [DataMember] public required Guid ProductId { get; init; }
    [DataMember] public required string Sku { get; init; }
    [DataMember] public required int Quantity { get; init; }
    [DataMember] public required decimal UnitPrice { get; init; }
}

[DataContract]
public sealed record CustomerDto
{
    [DataMember] public required Guid Id { get; init; }
    [DataMember] public required string Name { get; init; }
    [DataMember] public required AddressDto BillingAddress { get; init; }
    [DataMember] public AddressDto? ShippingAddress { get; init; }
    [DataMember] public required IReadOnlyCollection<string> Tags { get; init; }
    [DataMember] public required IReadOnlyDictionary<string, string> Metadata { get; init; }
}

[DataContract]
public sealed record OrderDto
{
    [DataMember] public required Guid Id { get; init; }
    [DataMember] public required CustomerDto Customer { get; init; }
    [DataMember] public required List<OrderItemDto> Items { get; init; }
    [DataMember] public required OrderStatus Status { get; init; }
    [DataMember] public required DateOnly OrderDate { get; init; }
    [DataMember] public required DateTimeOffset CreatedAt { get; init; }
    [DataMember] public required byte[] RowVersion { get; init; }
    [DataMember] public string? Notes { get; init; }
}
