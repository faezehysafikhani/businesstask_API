# Content-Security-Policy

Two places send a policy: the **API** (this host) and the **UI** (whatever serves `dist/`, for example nginx).

## The API
`Security:Csp:Mode` in appsettings: `Off`, `ReportOnly` (default) or `Enforce`. The policy is `default-src 'none'; frame-ancestors 'none'; ...`
because an API serves data, not pages. Swagger pages (`/swagger`) are left alone. Violations are posted by the browser to
`/api/platform/csp-report` and only written to the log (`NexusCore.SecurityReports`).

## The UI (nginx example)
The UI is a static single-page app with no inline scripts. Start in report-only mode:

```nginx
add_header Content-Security-Policy-Report-Only
  "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self' https://YOUR-API-HOST wss://YOUR-API-HOST; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; report-uri https://YOUR-API-HOST/api/platform/csp-report"
  always;
```
(`'unsafe-inline'` for styles is needed by Tailwind's runtime utilities only if you use inline `style=`; the UI uses a few. Remove it if your build has none.)

## Switching to enforce
1. Run in `ReportOnly` for **two weeks** while people use the system normally.
2. Read the log: `grep SecurityReports` - every line is something the policy would have blocked.
3. Fix or allow what is legitimate. When two weeks pass with no violations, change `Security:Csp:Mode` to `Enforce`
   and rename the nginx header to `Content-Security-Policy`.
4. If something breaks, switch back to `ReportOnly` - no redeploy of code is needed, only configuration.
