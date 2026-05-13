using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Endpoints;

public class QueueStatsEndpointsTests : IClassFixture<DashboardTestHost>
{
    private readonly DashboardTestHost _host;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = TestJsonOptions.CamelCase;

    public QueueStatsEndpointsTests(DashboardTestHost host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    [Fact]
    public async Task GetQueueStats_WhenNoJobsExist_ShouldReturnEmptyQueues()
    {
        using var freshHost = new DashboardTestHost();
        var freshClient = freshHost.CreateClient();

        var response = await freshClient.GetAsync("/atomizer/api/queues/stats", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<QueueStatsResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Queues.Should().NotBeNull();
        result.Queues.Should().BeEmpty();
    }

    [Fact]
    public async Task GetQueueStats_WhenJobsInMultipleQueues_ShouldGroupByQueue()
    {
        var client = _host.Services.GetRequiredService<IAtomizerClient>();
        await client.EnqueueAsync(
            "stats-multi-queue-default",
            configure: o => o.Queue = QueueKey.Default,
            cancellation: TestContext.Current.CancellationToken
        );
        await client.EnqueueAsync(
            "stats-multi-queue-test",
            configure: o => o.Queue = new QueueKey("test-queue"),
            cancellation: TestContext.Current.CancellationToken
        );

        var response = await _client.GetAsync("/atomizer/api/queues/stats", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<QueueStatsResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Queues.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetQueueStats_WhenJobsEnqueued_ShouldCountPendingJobs()
    {
        var client = _host.Services.GetRequiredService<IAtomizerClient>();
        await client.EnqueueAsync(
            "stats-count-payload",
            configure: o => o.Queue = QueueKey.Default,
            cancellation: TestContext.Current.CancellationToken
        );

        var response = await _client.GetAsync("/atomizer/api/queues/stats", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<QueueStatsResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();
        var defaultStats = result!.Queues.FirstOrDefault(q => q.QueueKey == "default");
        defaultStats.Should().NotBeNull();
        defaultStats!.Pending.Should().BeGreaterThan(0);
    }
}

internal sealed class QueueStatsResponseDto
{
    public IReadOnlyList<QueueStatItemDto> Queues { get; init; } = new List<QueueStatItemDto>();
}

internal sealed class QueueStatItemDto
{
    public string QueueKey { get; init; } = string.Empty;
    public int Pending { get; init; }
    public int Processing { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
}
