using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BusinessTask.Api;
using Xunit;

namespace BusinessTask.Tests;

/// <summary>
/// Phase 1 acceptance for the host: it composes, needs no database to build, and exposes exactly the routes recorded in
/// expected-routes.txt. A route appearing or disappearing is a deliberate act: set TM_UPDATE_ROUTES=1, run, and review the diff.
/// </summary>
public class HostCompositionTests
{
    private static WebApplication BuildHost()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration["Database:SeedOnStartup"] = "false";
        builder.AddBusinessTaskHost();
        var app = builder.Build();
        app.UseBusinessTaskHost();
        return app;
    }

    private static List<RouteEndpoint> Endpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();

    private static string Describe(RouteEndpoint e)
    {
        var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        var verb = methods is { Count: > 0 } ? string.Join("|", methods.OrderBy(m => m, StringComparer.Ordinal)) : "ANY";
        return $"{verb} {e.RoutePattern.RawText}";
    }

    [Fact]
    public void Host_Builds_WithoutADatabase()
    {
        using var app = BuildHost();
        Assert.NotEmpty(Endpoints(app));
    }

    [Fact]
    public void Routes_MatchTheRecordedList()
    {
        using var app = BuildHost();
        var actual = Endpoints(app).Select(Describe).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        var file = Path.Combine(AppContext.BaseDirectory, "expected-routes.txt");

        if (Environment.GetEnvironmentVariable("TM_UPDATE_ROUTES") == "1")
        {
            var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "expected-routes.txt"));
            File.WriteAllLines(source, actual);
            return;
        }

        var expected = File.ReadAllLines(file).Where(l => l.Length > 0).ToList();
        Assert.Empty(actual.Except(expected));
        Assert.Empty(expected.Except(actual));
    }

    [Fact]
    public void EveryApiRoute_RequiresSignIn_ExceptTheKnownOpenOnes()
    {
        using var app = BuildHost();
        // Sign-in, password recovery, the CSP report and the host's own health/redirect are open by design.
        string[] open = ["/api/identity/", "/api/platform/csp-report", "/health", "/"];
        var unprotected = Endpoints(app)
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/", StringComparison.Ordinal))
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(Describe)
            .Where(d => !open.Any(o => d.Contains(" " + o, StringComparison.Ordinal)))
            .ToList();
        Assert.Empty(unprotected);
    }

    [Fact]
    public void TheWorkCalendar_DecidesWhichDaysCount_AndTheMeetingsBridgeIsWired()
    {
        using var app = BuildHost();
        using var scope = app.Services.CreateScope();
        // The calendar's policy (Fridays and the official Iranian holidays are not working days) wins over the task module's allow-all.
        Assert.IsType<Nexus.Calendar.Application.IranSchedulingDayPolicy>(scope.ServiceProvider.GetRequiredService<NexusCore.Application.Common.ISchedulingDayPolicy>());
        Assert.IsType<Nexus.Integrations.CalendarTasks.MeetingTaskDirectory>(scope.ServiceProvider.GetRequiredService<Nexus.Calendar.Application.Meetings.IMeetingTaskDirectory>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Nexus.Integrations.CalendarTasks.MeetingActionConverter>());
    }

    [Theory]
    [InlineData(CspMode.ReportOnly, "Content-Security-Policy-Report-Only")]
    [InlineData(CspMode.Enforce, "Content-Security-Policy")]
    public async Task TheApi_SendsItsContentSecurityPolicy_InTheChosenMode(CspMode mode, string header)
    {
        var middleware = new SecurityHeadersMiddleware(ctx => Task.CompletedTask, Microsoft.Extensions.Options.Options.Create(new CspOptions { Mode = mode }));
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Path = "/api/task-management/tasks";
        await middleware.InvokeAsync(context);

        Assert.Contains("default-src 'none'", context.Response.Headers[header].ToString());
        Assert.Contains("report-uri /api/platform/csp-report", context.Response.Headers[header].ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        // Only one of the two headers.
        Assert.Single(new[] { "Content-Security-Policy", "Content-Security-Policy-Report-Only" }, context.Response.Headers.ContainsKey);
    }

    [Fact]
    public async Task OffAndSwagger_SendNoPolicy()
    {
        foreach (var (mode, path) in new[] { (CspMode.Off, "/api/x"), (CspMode.Enforce, "/swagger/index.html") })
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Request.Path = path;
            await new SecurityHeadersMiddleware(ctx => Task.CompletedTask, Microsoft.Extensions.Options.Options.Create(new CspOptions { Mode = mode })).InvokeAsync(context);
    
            Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
            Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy-Report-Only"));
        }
    }

    [Theory]
    [InlineData("/api/task-management/tasks")]
    [InlineData("/api/task-reports/tasks")]
    [InlineData("/api/org-structure/nodes")]
    [InlineData("/api/calendar/meetings")]
    [InlineData("/api/calendar/work-calendars")]
    [InlineData("/hubs/chat")]
    [InlineData("/hubs/notifications")]
    public void KeyModules_AreMapped(string prefix)
    {
        using var app = BuildHost();
        Assert.Contains(Endpoints(app), e => e.RoutePattern.RawText!.StartsWith(prefix, StringComparison.Ordinal));
    }
}
