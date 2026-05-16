using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atomizer.Storage;
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

    [Fact]
    public async Task SetScheduleEnabled_WhenScheduleExists_ShouldToggleScheduleAndSurviveRegistrationUpsert()
    {
        using var freshHost = new DashboardTestHost();
        var atomizerClient = freshHost.Services.GetRequiredService<IAtomizerClient>();
        var jobKey = new JobKey("toggle-schedule");
        await atomizerClient.ScheduleRecurringAsync(
            "recurring-payload",
            jobKey,
            Schedule.Every().Minute(),
            configure: options => options.Enabled = true,
            cancellation: TestContext.Current.CancellationToken
        );
        var storage = freshHost.Services.GetRequiredService<InMemoryStorage>();
        var schedule = (await storage.GetSchedulesAsync(TestContext.Current.CancellationToken)).Single(s =>
            s.JobKey == jobKey
        );
        var originalNextRunAt = schedule.NextRunAt;
        var client = freshHost.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/atomizer/api/schedules/{schedule.Id}/enabled",
            new { enabled = false },
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ScheduleActionResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Schedule.Enabled.Should().BeFalse();

        await atomizerClient.ScheduleRecurringAsync(
            "recurring-payload",
            jobKey,
            Schedule.Every().Minute(),
            configure: options => options.Enabled = true,
            cancellation: TestContext.Current.CancellationToken
        );

        var afterRestartRegistration = (await storage.GetSchedulesAsync(TestContext.Current.CancellationToken)).Single(
            s => s.JobKey == jobKey
        );
        afterRestartRegistration.Enabled.Should().BeFalse();
        afterRestartRegistration.NextRunAt.Should().Be(originalNextRunAt);
    }

    [Fact]
    public async Task RunScheduleNow_WhenScheduleExists_ShouldEnqueueOneJobWithoutMovingNextRun()
    {
        using var freshHost = new DashboardTestHost();
        var atomizerClient = freshHost.Services.GetRequiredService<IAtomizerClient>();
        await atomizerClient.ScheduleRecurringAsync(
            new DashboardActionPayload { Message = "scheduled-now" },
            new JobKey("manual-run-schedule"),
            Schedule.Every().Minute(),
            configure: options => options.Queue = new QueueKey("test-queue"),
            cancellation: TestContext.Current.CancellationToken
        );
        var storage = freshHost.Services.GetRequiredService<InMemoryStorage>();
        var schedule = (await storage.GetSchedulesAsync(TestContext.Current.CancellationToken)).Single(s =>
            s.JobKey == new JobKey("manual-run-schedule")
        );
        var originalNextRunAt = schedule.NextRunAt;
        var client = freshHost.CreateClient();

        var response = await client.PostAsync(
            $"/atomizer/api/schedules/{schedule.Id}/run-now",
            content: null,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<JobActionResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();

        var job = await storage.GetJobByIdAsync(result!.JobId, TestContext.Current.CancellationToken);
        job.Should().NotBeNull();
        job!.QueueKey.Should().Be(new QueueKey("test-queue"));
        job.ScheduleJobKey.Should().Be(schedule.JobKey);
        job.Payload.Should().Be(schedule.Payload);

        var scheduleAfterRun = (await storage.GetSchedulesAsync(TestContext.Current.CancellationToken)).Single(s =>
            s.Id == schedule.Id
        );
        scheduleAfterRun.NextRunAt.Should().Be(originalNextRunAt);
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

internal sealed class ScheduleActionResponseDto
{
    public ScheduleItemDto Schedule { get; init; } = new();
}
