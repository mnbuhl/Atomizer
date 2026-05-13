using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Dashboard.Contracts;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Endpoints;

internal sealed class ServersEndpointHandler
{
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerStorage _storage;

    public ServersEndpointHandler(IAtomizerStorage storage, IAtomizerClock clock)
    {
        _storage = storage;
        _clock = clock;
    }

    public async Task ListAsync(HttpContext context)
    {
        var servers = await _storage.GetActiveServersAsync(context.RequestAborted);
        var now = _clock.UtcNow;
        await DashboardJsonResponse.WriteAsync(
            context,
            servers.Select(s => ServerDto.From(s, now)).ToList(),
            context.RequestAborted
        );
    }
}
