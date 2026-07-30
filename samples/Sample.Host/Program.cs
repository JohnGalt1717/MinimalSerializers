using System.Text.Json;
using MinimalSerializers.Json;
using Sample.Contracts;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddMinimalJsonContext<SampleContractsJsonSerializerContext>();

var order = new OrderDto
{
    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
    Customer = new CustomerDto
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Name = "Ada Lovelace",
        BillingAddress = new AddressDto { Line1 = "1 Analytical Engine Way", City = "London", Country = "UK" },
        Tags = ["vip", "founder"],
        Metadata = new Dictionary<string, string> { ["source"] = "sample" },
    },
    Items = [new OrderItemDto { ProductId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Sku = "BOOK-001", Quantity = 2, UnitPrice = 19.99m }],
    Status = OrderStatus.Pending,
    OrderDate = new DateOnly(2026, 7, 30),
    CreatedAt = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
    RowVersion = [1, 2, 3, 4],
    Notes = "hello",
};

var json = JsonSerializer.Serialize(order, options);
Console.WriteLine(json);
var roundTrip = JsonSerializer.Deserialize<OrderDto>(json, options);
Console.WriteLine(roundTrip is null ? "deserialize-failed" : $"ok:{roundTrip.Customer.Name}:{roundTrip.Items.Count}");
var arrayJson = JsonSerializer.Serialize(new[] { order }, options);
_ = JsonSerializer.Deserialize<OrderDto[]>(arrayJson, options);
Console.WriteLine("array-ok");
