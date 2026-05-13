namespace Atomizer.Dashboard.Tests;

public class Program
{
    public static void Main(string[] args)
    {
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
    }
}
