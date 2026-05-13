using Atomizer.Dashboard.Configuration;
using Atomizer.Dashboard.StaticFiles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Atomizer.Dashboard.Endpoints;

internal sealed class StaticFilesEndpointHandler
{
    private readonly DashboardOptions _options;

    public StaticFilesEndpointHandler(IOptions<DashboardOptions> options)
    {
        _options = options.Value;
    }

    public Task ServeIndexAsync(HttpContext context, string routePrefix) =>
        EmbeddedSpaFileProvider.ServeIndexAsync(context, routePrefix, _options);

    public Task ServeAsync(HttpContext context, string routePrefix) =>
        EmbeddedSpaFileProvider.ServeAsync(context, routePrefix, _options);
}
