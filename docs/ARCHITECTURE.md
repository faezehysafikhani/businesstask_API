# TM host — architecture (phase 1)

A new product host. It owns no domain logic; it composes the NexusCore packages and holds what is specific to the product:
`Program.cs`, configuration, SQL upgrade scripts, the offline package feed and the acceptance tests.

## Where code goes
| The change… | Goes in |
|---|---|
| touches one Core module | that module, in Core, on branch `feature/1405-08-19-UxChanges` (optional column + SQL script) |
| connects two modules | a `Nexus.Integrations.*` project in Core (modules must not reference each other) |
| is a new independent feature (Requests, Timesheets, Automation) | a new `Nexus.*` module in Core |
| only composes or configures | this host |

## Composition (src/BusinessTask.Api/BusinessTaskHost.cs)
`AddNexusPlatform` (identity, organization + structure, calendar + Iranian holidays, workflow, chat, events, ticketing, notifications)
+ `AddTaskManagement(+Infrastructure)` + bridges `TaskNotifications`, `TaskReporting`, `CalendarTasks`, `CalendarOrgStructure`
+ (phase 11) `AddProjectManagementCore(+Infrastructure)`, `AddProjectTeam(+Infrastructure)`, `AddStakeholderManagement(+Infrastructure)`,
`AddActionManagement(+Infrastructure)` (Portfolio combines projects and actions), `AddPortfolio` and the bridges
`ProjectVisibility` (who may see which project) and `ProjectDeletion` (what a delete must refuse).
Each module has its own schema; the host creates them and runs the module patchers on startup.

## Rules
* Core version is one number: `NexusCoreVersion` in `Directory.Build.props`.
* Restore only from `./packages` (closed network). `eng/pack-core.sh` then `eng/make-offline-feed.sh`.
* SQL Server only. Production: `Database:SeedOnStartup=false`; schema changes come as scripts in `docs/upgrade/`.
* Never push to the reference hosts (TMPB_API, TMPB-UI). They are read-only examples.
* This folder is not a git repository yet (decision: stay local until the repo is created).
