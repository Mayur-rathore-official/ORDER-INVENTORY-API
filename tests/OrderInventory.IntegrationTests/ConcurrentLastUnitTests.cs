using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderInventory.Application.Abstractions;

namespace OrderInventory.IntegrationTests;

public class ConcurrentLastUnitTests : IClassFixture<ConcurrencyApiFactory>
{
    private readonly ConcurrencyApiFactory _factory;

    public ConcurrentLastUnitTests(ConcurrencyApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Two_customers_buying_the_last_unit_yield_one_201_and_one_409()
    {
        using var client = _factory.CreateClient();
        var firstToken = await LoginAsync(client, "customer1@store.local");
        var secondToken = await LoginAsync(client, "customer2@store.local");
        var product = await FindProductAsync(client, "SKU-1013");
        Assert.Equal(1, product.StockQuantity);

        _factory.Gate.ArmForTwoRequests = true;
        var first = PlaceAsync(client, firstToken, product.Id, "idem-last-unit-customer-1");
        var second = PlaceAsync(client, secondToken, product.Id, "idem-last-unit-customer-2");
        var responses = await Task.WhenAll(first, second);

        var statuses = responses.Select(response => response.StatusCode).OrderBy(code => code).ToArray();
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.True(
            statuses.SequenceEqual(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }),
            $"Expected 201 and 409 but got {(int)statuses[0]} and {(int)statuses[1]}.{Environment.NewLine}{bodies[0]}{Environment.NewLine}{bodies[1]}");

        var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        var conflictBody = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("Concurrency conflict", conflictBody, StringComparison.OrdinalIgnoreCase);

        var after = await FindProductAsync(client, "SKU-1013");
        Assert.Equal(0, after.StockQuantity);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Customer123!" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var auth = JsonSerializer.Deserialize<AuthPayload>(body, JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(auth?.AccessToken));
        return auth!.AccessToken;
    }

    private static async Task<ProductPayload> FindProductAsync(HttpClient client, string sku)
    {
        var response = await client.GetAsync($"/api/products?name=Last%20Unit&page=1&pageSize=20");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var page = JsonSerializer.Deserialize<ProductPage>(body, JsonOptions);
        var product = page?.Items.SingleOrDefault(item => item.Sku == sku);
        Assert.NotNull(product);
        return product;
    }

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, string token, Guid productId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                items = new[] { new { productId, quantity = 1 } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record AuthPayload(string AccessToken);

    private sealed record ProductPage(List<ProductPayload> Items);

    private sealed record ProductPayload(Guid Id, string Sku, int StockQuantity);
}

public sealed class ConcurrencyApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"order-inventory-{Guid.NewGuid():N}.db");

    public BarrierOrderPlacementGate Gate { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}");
        builder.UseSetting("Jwt:SigningKey", "TestingOnlySigningKey_AtLeast32Bytes!!");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_databasePath}",
                ["Jwt:SigningKey"] = "TestingOnlySigningKey_AtLeast32Bytes!!",
                ["Seed:Enabled"] = "true"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOrderPlacementGate>();
            services.AddSingleton<IOrderPlacementGate>(Gate);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        TryDelete(_databasePath);
        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // The test host may still be releasing the SQLite file.
        }
    }
}

public sealed class BarrierOrderPlacementGate : IOrderPlacementGate
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public bool ArmForTwoRequests { get; set; }

    public async Task OnProductsReservedAsync(CancellationToken cancellationToken)
    {
        if (!ArmForTwoRequests)
            return;

        if (Interlocked.Increment(ref _arrived) >= 2)
            _release.TrySetResult();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await _release.Task.WaitAsync(timeout.Token);
    }
}
