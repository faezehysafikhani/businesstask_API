namespace BusinessTask.Api;

public enum CspMode
{
    Off,
    ReportOnly,
    Enforce
}

/// <summary>Security:Csp - the Content-Security-Policy the API itself sends. See docs/CSP.md for the UI's policy and the switch to enforcing.</summary>
public sealed class CspOptions
{
    public const string SectionName = "Security:Csp";

    /// <summary>ReportOnly first: violations are reported (to /api/platform/csp-report) but nothing is blocked. Enforce after a quiet period.</summary>
    public CspMode Mode { get; set; } = CspMode.ReportOnly;

    /// <summary>An API serves data, not pages: nothing may load, nobody may frame it.</summary>
    public string Policy { get; set; } = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; report-uri /api/platform/csp-report";
}

/// <summary>Adds the Content-Security-Policy and the basic hardening headers to every answer except the Swagger pages (which need scripts).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, Microsoft.Extensions.Options.IOptions<CspOptions> options)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/swagger"))
        {
            var headers = context.Response.Headers;
            var csp = options.Value;
            if (csp.Mode != CspMode.Off)
            {
                headers[csp.Mode == CspMode.Enforce ? "Content-Security-Policy" : "Content-Security-Policy-Report-Only"] = csp.Policy;
            }

            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("Referrer-Policy", "no-referrer");
        }

        return next(context);
    }
}
