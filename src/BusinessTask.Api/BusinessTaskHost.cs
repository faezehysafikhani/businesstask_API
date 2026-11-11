using Microsoft.OpenApi.Models;
using Nexus.Actions;
using Nexus.Actions.Infrastructure;
using Nexus.Bundles;
using Nexus.Calendar.Infrastructure.Meetings;
using Nexus.Integrations.CalendarOrgStructure;
using Nexus.Integrations.CalendarTasks;
using Nexus.Integrations.ProjectDeletion;
using Nexus.Integrations.ProjectVisibility;
using Nexus.Integrations.TaskNotifications;
using Nexus.Integrations.TaskReporting;
using Nexus.Portfolio;
using Nexus.Portfolio.Endpoints;
using Nexus.ProjectManagement.Core;
using Nexus.ProjectManagement.Core.Endpoints;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.ProjectManagement.StakeholderManagement;
using Nexus.ProjectManagement.StakeholderManagement.Endpoints;
using Nexus.ProjectManagement.StakeholderManagement.Infrastructure;
using Nexus.ProjectManagement.Team;
using Nexus.ProjectManagement.Team.Endpoints;
using Nexus.ProjectManagement.Team.Infrastructure;
using Nexus.TaskManagement;
using Nexus.TaskManagement.Endpoints;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.Infrastructure.Persistence;
using Serilog;

namespace BusinessTask.Api;

/// <summary>
/// The whole composition of the host, in three calls, so a test can build the same host without a database:
/// <see cref="AddBusinessTaskHost"/> (services), <see cref="UseBusinessTaskHost"/> (pipeline and endpoints) and
/// <see cref="InitializeBusinessTaskHostAsync"/> (seed and schema, the only step that needs SQL Server).
/// </summary>
public static class BusinessTaskHost
{
    public static WebApplicationBuilder AddBusinessTaskHost(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

        // Platform: identity, organization + structure, calendar (+ Iranian holidays), workflow, chat, events, ticketing, notifications.
        builder.Services.AddNexusPlatform(builder.Configuration, builder.Environment);

        // Meetings (their minutes become tasks through the CalendarTasks bridge below). Before the bridges: they replace its placeholders.
        builder.Services.AddCalendarMeetings(builder.Configuration);

        // Task management and the bridges that connect it to the platform. Bridges live in Core, never here.
        builder.Services.AddTaskManagement();
        builder.Services.AddTaskManagementInfrastructure(builder.Configuration);
        builder.Services.AddTaskNotificationsIntegration();
        builder.Services.AddTaskReporting();
        builder.Services.AddCalendarTasksIntegration();
        builder.Services.AddCalendarOrgStructureIntegration();

        // Projects (phase 11): the project charter, the team of a project, its stakeholders (with the
        // power/interest matrix) and the portfolio view. Portfolio combines projects and actions, so
        // the action module comes with it; the bridges stay in Core.
        builder.Services.AddProjectManagementCore(o => o.RequireUniqueName = true);
        builder.Services.AddProjectManagementCoreInfrastructure(builder.Configuration);
        builder.Services.AddProjectTeam();
        builder.Services.AddProjectTeamInfrastructure(builder.Configuration);
        builder.Services.AddStakeholderManagement();
        builder.Services.AddStakeholderManagementInfrastructure(builder.Configuration);
        builder.Services.AddActionManagement();
        builder.Services.AddActionManagementInfrastructure(builder.Configuration);
        builder.Services.AddPortfolio();
        builder.Services.AddProjectVisibilityIntegration();
        builder.Services.AddProjectDeletionIntegration();

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        builder.Services.Configure<CspOptions>(builder.Configuration.GetSection(CspOptions.SectionName));

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo { Title = "TM API", Version = "v1", Description = "Task management, built on the NexusCore platform." });
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, [] }
            });
        });

        return builder;
    }

    public static WebApplication UseBusinessTaskHost(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseSerilogRequestLogging();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseNexusPlatform();

        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous().ExcludeFromDescription();

        app.MapNexusPlatform();
        app.MapTaskManagementEndpoints();
        app.MapMeetingEndpoints();
        app.MapCalendarTasksEndpoints();
        app.MapTaskReportEndpoints();

        // Projects (phase 11)
        app.MapProjectEndpoints();
        app.MapTeamEndpoints();
        app.MapStakeholderEndpoints();
        app.MapPortfolioEndpoints();
        return app;
    }

    public static async Task InitializeBusinessTaskHostAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await app.InitializeNexusPlatformAsync(cancellationToken);

        // Task management is not part of the platform bundle, so its schema is created here.
        if (app.Configuration.GetValue("Database:SeedOnStartup", true))
        {
            using var scope = app.Services.CreateScope();
            await ModuleSchemaInitializer.EnsureCreatedAsync(
                scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>(), cancellationToken);
            await ModuleSchemaInitializer.EnsureCreatedAsync(
                scope.ServiceProvider.GetRequiredService<MeetingDbContext>(), cancellationToken);

            // Projects (phase 11): one schema per module, then the patchers that bring an older
            // database up to date (creating tables never adds a column to an existing one).
            var sp = scope.ServiceProvider;
            await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ProjectManagementCoreDbContext>(), cancellationToken);
            await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<TeamDbContext>(), cancellationToken);
            await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<StakeholderManagementDbContext>(), cancellationToken);
            await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ActionsDbContext>(), cancellationToken);
            await StakeholderSchemaUpgrade.EnsureCurrentAsync(sp.GetRequiredService<StakeholderManagementDbContext>(), cancellationToken);
            await ActionsSchemaUpgrade.EnsureCurrentAsync(sp.GetRequiredService<ActionsDbContext>(), cancellationToken);
            await ActionsPlanningSchemaUpgrade.EnsureCurrentAsync(sp.GetRequiredService<ActionsDbContext>(), cancellationToken);
        }
    }
}
