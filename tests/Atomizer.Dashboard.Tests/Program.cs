using Atomizer;
using Atomizer.Dashboard.Authorization;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAtomizer(options =>
{
    options.UseInMemoryStorage();
    options.AddQueue("test-queue", _ => { });
});
builder.Services.AddAtomizerDashboard(options =>
{
    options.Authorization.Add(new AlwaysAllowAuthFilter());
});

var app = builder.Build();
app.MapAtomizerDashboard();
app.Run();

internal sealed class AlwaysAllowAuthFilter : IAtomizerDashboardAuthorizationFilter
{
    public DashboardAuthorizationResult Authorize(HttpContext context) => DashboardAuthorizationResult.Authorized;
}

public partial class Program { }
