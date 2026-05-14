using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Atomizer.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Endpoints;

public class JobsEndpointsTests : IClassFixture<DashboardTestHost>
{
    private readonly DashboardTestHost _host;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = TestJsonOptions.CamelCase;

    public JobsEndpointsTests(DashboardTestHost host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    [Fact]
    public async Task GetJobs_WhenNoJobsExist_ShouldReturnEmptyList()
    {
        using var freshHost = new DashboardTestHost();
        var client = freshHost.CreateClient();

        var response = await client.GetAsync("/atomizer/api/jobs", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PagedResponseDto<JobItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetJobs_WhenJobsExist_ShouldReturnJobsInResponse()
    {
        var client = _host.Services.GetRequiredService<IAtomizerClient>();
        await client.EnqueueAsync("hello-world", cancellation: TestContext.Current.CancellationToken);

        var response = await _client.GetAsync("/atomizer/api/jobs", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PagedResponseDto<JobItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetJobs_WhenTakeExceedsHardCeiling_ShouldCapAt500()
    {
        var response = await _client.GetAsync("/atomizer/api/jobs?take=1000", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PagedResponseDto<JobItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Take.Should().Be(500);
    }

    [Fact]
    public async Task GetJobs_ShouldReturnCamelCaseJson()
    {
        var response = await _client.GetAsync("/atomizer/api/jobs", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"totalCount\"");
        body.Should().NotContain("\"TotalCount\"");
    }

    [Fact]
    public async Task GetJobs_WhenFilteredByStatus_ShouldReturnOnlyMatchingJobs()
    {
        var client = _host.Services.GetRequiredService<IAtomizerClient>();
        await client.EnqueueAsync("filter-by-status-payload", cancellation: TestContext.Current.CancellationToken);

        var response = await _client.GetAsync(
            "/atomizer/api/jobs?status=Pending",
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PagedResponseDto<JobItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Items.Should().AllSatisfy(j => j.Status.Should().Be("Pending"));
    }

    [Fact]
    public async Task GetJobs_WhenJobsHaveDifferentStatuses_ShouldReturnStatusCounts()
    {
        using var freshHost = new DashboardTestHost();
        var storage = freshHost.Services.GetRequiredService<InMemoryStorage>();
        await InsertJobAsync(storage, QueueKey.Default, AtomizerJobStatus.Pending);
        await InsertJobAsync(storage, QueueKey.Default, AtomizerJobStatus.Processing);
        await InsertJobAsync(storage, QueueKey.Default, AtomizerJobStatus.Failed);
        await InsertJobAsync(storage, new QueueKey("other-queue"), AtomizerJobStatus.Completed);

        var client = freshHost.CreateClient();

        var response = await client.GetAsync(
            $"/atomizer/api/jobs?queue={QueueKey.Default.Key}&status=Pending",
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"statusCounts\"");
        var result = JsonSerializer.Deserialize<PagedResponseDto<JobItemDto>>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Items.Should().AllSatisfy(j => j.Status.Should().Be("Pending"));
        result.StatusCounts.Pending.Should().Be(1);
        result.StatusCounts.Processing.Should().Be(1);
        result.StatusCounts.Completed.Should().Be(0);
        result.StatusCounts.Failed.Should().Be(1);
    }

    [Fact]
    public async Task GetJobDetail_WhenJobExists_ShouldReturnPayloadAndErrors()
    {
        var client = _host.Services.GetRequiredService<IAtomizerClient>();
        var id = await client.EnqueueAsync("job-detail-payload", cancellation: TestContext.Current.CancellationToken);

        var response = await _client.GetAsync($"/atomizer/api/jobs/{id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<JobDetailResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Payload.Should().NotBeNullOrEmpty();
        result.Errors.Should().NotBeNull();
    }

    [Fact]
    public async Task GetJobDetail_WhenJobDoesNotExist_ShouldReturn404()
    {
        var response = await _client.GetAsync(
            $"/atomizer/api/jobs/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RetryJob_WhenJobFailed_ShouldEnqueueReplacementJob()
    {
        using var freshHost = new DashboardTestHost();
        var storage = freshHost.Services.GetRequiredService<InMemoryStorage>();
        await InsertJobAsync(storage, QueueKey.Default, AtomizerJobStatus.Failed);
        var source = (
            await storage.GetJobsAsync(new JobQuery { Take = 10 }, TestContext.Current.CancellationToken)
        ).Items.Single(j => j.Status == AtomizerJobStatus.Failed);
        var client = freshHost.CreateClient();

        var response = await client.PostAsync(
            $"/atomizer/api/jobs/{source.Id}/retry",
            content: null,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<JobActionResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.JobId.Should().NotBe(source.Id);

        var replacement = await storage.GetJobByIdAsync(result.JobId, TestContext.Current.CancellationToken);
        replacement.Should().NotBeNull();
        replacement!.Status.Should().Be(AtomizerJobStatus.Pending);
        replacement.Payload.Should().Be(source.Payload);
        replacement.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task CancelJob_WhenJobPending_ShouldMarkJobCancelled()
    {
        using var freshHost = new DashboardTestHost();
        var atomizerClient = freshHost.Services.GetRequiredService<IAtomizerClient>();
        var id = await atomizerClient.EnqueueAsync("cancel-me", cancellation: TestContext.Current.CancellationToken);
        var client = freshHost.CreateClient();

        var response = await client.PostAsync(
            $"/atomizer/api/jobs/{id}/cancel",
            content: null,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<JobActionResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();
        result!.Status.Should().Be("Cancelled");

        var detail = await client.GetAsync($"/atomizer/api/jobs/{id}", TestContext.Current.CancellationToken);
        var detailBody = await detail.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var job = JsonSerializer.Deserialize<JobDetailResponseDto>(detailBody, JsonOptions);
        job.Should().NotBeNull();
        job!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task TriggerJob_WhenRegisteredPayloadTypeAndValidJson_ShouldEnqueueJob()
    {
        using var freshHost = new DashboardTestHost();
        var client = freshHost.CreateClient();

        var optionsResponse = await client.GetAsync("/atomizer/api/job-types", TestContext.Current.CancellationToken);
        optionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var optionsBody = await optionsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var options = JsonSerializer.Deserialize<List<JobTypeOptionDto>>(optionsBody, JsonOptions);
        var option = options
            .Should()
            .NotBeNull()
            .And.ContainSingle(o => o.PayloadTypeName == nameof(DashboardActionPayload))
            .Subject;

        var response = await client.PostAsJsonAsync(
            "/atomizer/api/jobs/trigger",
            new
            {
                payloadTypeId = option.Id,
                queueKey = "test-queue",
                payload = """{"message":"from-dashboard"}""",
            },
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<JobActionResponseDto>(body, JsonOptions);
        result.Should().NotBeNull();

        var storage = freshHost.Services.GetRequiredService<InMemoryStorage>();
        var job = await storage.GetJobByIdAsync(result!.JobId, TestContext.Current.CancellationToken);
        job.Should().NotBeNull();
        job!.QueueKey.Should().Be(new QueueKey("test-queue"));
        job.Payload.Should().Be("""{"message":"from-dashboard"}""");
        job.PayloadType.Should().Be(typeof(DashboardActionPayload));
    }

    [Fact]
    public async Task TriggerJob_WhenRequestBodyMalformedJson_ShouldReturnBadRequest()
    {
        using var freshHost = new DashboardTestHost();
        var client = freshHost.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            "/atomizer/api/jobs/trigger",
            content,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("Malformed JSON request body.");
    }

    private static async Task InsertJobAsync(InMemoryStorage storage, QueueKey queue, AtomizerJobStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var job = AtomizerJob.Create(queue, typeof(string), "payload", now, now);

        await storage.InsertAsync(job, TestContext.Current.CancellationToken);

        if (status == AtomizerJobStatus.Pending)
            return;

        var leaseToken = new LeaseToken($"server-1:*:{job.QueueKey.Key}:*:{Guid.NewGuid()}");
        job.Lease(leaseToken, now, TimeSpan.FromMinutes(5));

        if (status == AtomizerJobStatus.Completed)
        {
            job.Attempt();
            job.MarkAsCompleted(now);
        }
        else if (status == AtomizerJobStatus.Failed)
        {
            job.Attempt();
            job.MarkAsFailed(now);
        }

        await storage.UpdateJobsAsync([job], TestContext.Current.CancellationToken);
    }
}

internal sealed class PagedResponseDto<T>
{
    public IReadOnlyList<T> Items { get; init; } = new List<T>();
    public int TotalCount { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public JobStatusCountsDto StatusCounts { get; init; } = new();
}

internal sealed class JobStatusCountsDto
{
    public int Pending { get; init; }
    public int Processing { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int Cancelled { get; init; }
}

internal sealed class JobItemDto
{
    public Guid Id { get; init; }
    public string QueueKey { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class JobDetailResponseDto
{
    public Guid Id { get; init; }
    public string QueueKey { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Payload { get; init; } = string.Empty;
    public IReadOnlyList<JobErrorItemDto> Errors { get; init; } = new List<JobErrorItemDto>();
}

internal sealed class JobActionResponseDto
{
    public Guid JobId { get; init; }
    public Guid? SourceJobId { get; init; }
    public string Status { get; init; } = string.Empty;
}

internal sealed class JobTypeOptionDto
{
    public string Id { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public string PayloadTypeFullName { get; init; } = string.Empty;
}

internal sealed class JobErrorItemDto
{
    public int Attempt { get; init; }
    public string Message { get; init; } = string.Empty;
}
