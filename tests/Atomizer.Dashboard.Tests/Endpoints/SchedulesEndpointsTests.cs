using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Endpoints;

public class SchedulesEndpointsTests : IClassFixture<DashboardTestHost>
{
    private readonly DashboardTestHost _host;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = TestJsonOptions.CamelCase;

    public SchedulesEndpointsTests(DashboardTestHost host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    [Fact]
    public async Task GetSchedules_WhenNoSchedulesExist_ShouldReturnEmptyList()
    {
        using var freshHost = new DashboardTestHost();
        var freshClient = freshHost.CreateClient();

        var response = await freshClient.GetAsync("/atomizer/api/schedules", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<List<ScheduleItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSchedules_WhenSchedulesExist_ShouldReturnAllSchedules()
    {
        var client = _host.Services.GetRequiredService<IAtomizerClient>();
        await client.ScheduleRecurringAsync(
            "recurring-payload",
            new JobKey("test-recurring-schedule"),
            Schedule.Default,
            cancellation: TestContext.Current.CancellationToken
        );

        var response = await _client.GetAsync("/atomizer/api/schedules", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<List<ScheduleItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        result!.Should().Contain(s => s.JobKey == "test-recurring-schedule");
    }
}

internal sealed class ScheduleItemDto
{
    public Guid Id { get; init; }
    public string JobKey { get; init; } = string.Empty;
    public string QueueKey { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public string Cron { get; init; } = string.Empty;
    public DateTimeOffset NextRunAt { get; init; }
    public bool Enabled { get; init; }
}
