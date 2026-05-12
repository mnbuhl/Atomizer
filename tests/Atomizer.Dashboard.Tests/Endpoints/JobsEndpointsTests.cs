using System.Net;
using System.Text.Json;
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
}

internal sealed class PagedResponseDto<T>
{
    public IReadOnlyList<T> Items { get; init; } = new List<T>();
    public int TotalCount { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
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

internal sealed class JobErrorItemDto
{
    public int Attempt { get; init; }
    public string Message { get; init; } = string.Empty;
}
