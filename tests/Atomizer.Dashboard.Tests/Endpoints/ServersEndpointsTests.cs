using System.Net;
using System.Text.Json;
using Atomizer.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Endpoints;

public class ServersEndpointsTests : IClassFixture<DashboardTestHost>
{
    private readonly DashboardTestHost _host;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = TestJsonOptions.CamelCase;

    public ServersEndpointsTests(DashboardTestHost host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    [Fact]
    public async Task GetServers_WhenNoServersActive_ShouldReturnEmptyList()
    {
        using var freshHost = new DashboardTestHost();
        var freshClient = freshHost.CreateClient();

        var response = await freshClient.GetAsync("/atomizer/api/servers", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<List<ServerItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetServers_WhenServerHeartbeatUpserted_ShouldReturnServer()
    {
        var storage = _host.Services.GetRequiredService<InMemoryStorage>();
        var server = new AtomizerActiveServer
        {
            InstanceId = "test-server-integration",
            LastHeartbeatAt = DateTimeOffset.UtcNow,
        };
        await storage.UpsertHeartbeatAsync(server, TestContext.Current.CancellationToken);

        var response = await _client.GetAsync("/atomizer/api/servers", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<List<ServerItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result.Should().Contain(s => s.InstanceId == "test-server-integration");
    }
}

internal sealed class ServerItemDto
{
    public string InstanceId { get; init; } = string.Empty;
    public DateTimeOffset LastHeartbeatAt { get; init; }
    public int AgeSeconds { get; init; }
}
