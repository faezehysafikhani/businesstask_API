using Microsoft.OpenApi.Models;
using Nexus.Bundles;
using Nexus.Integrations.CalendarOrgStructure;
using Nexus.Integrations.CalendarTasks;
using Nexus.Integrations.TaskNotifications;
using Nexus.Integrations.TaskReporting;
using Nexus.TaskManagement;
using Nexus.TaskManagement.Endpoints;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.Infrastructure.Persistence;
using Serilog;

namespace TM.Host.Api;

/// <summary>
/// The whole composition of the host, in three calls, so a test can build the same host without a database:
/// <see cref="AddTmHost"/> (services), <see cref="UseTmHost"/> (pipeline and endpoints) and
/// <see cref="InitializeTmHostAsync"/> (seed and schema, the only step that needs SQL Server).
/// </summary>
public static class TmHost
{
    public static WebApplicationBuilder AddTmHost(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

        // Platform: identity, organization + structure, calendar (+ Iranian holidays), workflow, chat, events, ticketing, notifications.
        builder.Services.AddNexusPlatform(builder.Configuration, builder.Environment);

        // Task management and the bridges that connect it to the platform. Bridges live in Core, never here.
        builder.Services.AddTaskManagement();
        builder.Services.AddTaskManagementInfrastructure(builder.Configuration);
        builder.Services.AddTaskNotificationsIntegration();
        builder.Services.AddTaskReporting();
        builder.Services.AddCalendarTasksIntegration();
        builder.Services.AddCalendarOrgStructureIntegration();

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

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

    public static WebApplication UseTmHost(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseSerilogRequestLogging();
        app.UseNexusPlatform();

        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous().ExcludeFromDescription();

        app.MapNexusPlatform();
        app.MapTaskManagementEndpoints();
        app.MapTaskReportEndpoints();
        return app;
    }

    public static async Task InitializeTmHostAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await app.InitializeNexusPlatformAsync(cancellationToken);

        // Task management is not part of the platform bundle, so its schema is created here.
        if (app.Configuration.GetValue("Database:SeedOnStartup", true))
        {
            using var scope = app.Services.CreateScope();
            await ModuleSchemaInitializer.EnsureCreatedAsync(
                scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>(), cancellationToken);
        }
    }
}
