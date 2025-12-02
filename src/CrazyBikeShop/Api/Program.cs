using System.Text.Json;
using Api;
using CrazyBikeShop.ServiceDefaults;
using CrazyBikeShop.Shared;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ScheduledTasks;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.All;
});

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger<Program>());
builder.Services.AddDurableTaskClient(b =>
{
    b.UseDurableTaskScheduler(Environment.GetEnvironmentVariable("ConnectionStrings__dts-orchestrator")!);
    //b.UseScheduledTasks();
});
// builder.Services.AddDurableTaskWorker(b =>
// {
//     b.UseDurableTaskScheduler(Environment.GetEnvironmentVariable("ConnectionStrings__dts")!);
//     b.UseScheduledTasks();
// });

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapPost("/schedule", async (/*[FromBody] ScheduleRequest scheduleRequest,*/ 
        /*[FromServices] ScheduledTaskClient scheduledTaskClient*/
        [FromServices] DurableTaskClient durableTaskClient
        ) =>
{
    var scheduleRequest = new ScheduleRequest
    {
        Id = Guid.NewGuid().ToString(),
        OrchestrationName = "CrazyBikeOrchestrator",
        Interval = TimeSpan.FromSeconds(10),
        StartAt = DateTimeOffset.Now.AddSeconds(10),
        EndAt = DateTimeOffset.Now.AddMinutes(1)
    };
    
    try
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, CrazyBikeSelector.GetOne());
        stream.Position = 0;
        scheduleRequest.Input = new StreamReader(stream).ReadToEnd();
        
        var scheduleCreationOptions = new ScheduleCreationOptions(scheduleRequest.Id, scheduleRequest.OrchestrationName, scheduleRequest.Interval)
        {
            OrchestrationInput = scheduleRequest.Input,
            StartAt = scheduleRequest.StartAt,
            EndAt = scheduleRequest.EndAt,
            StartImmediatelyIfLate = true
        };
        
        var startOrchestrationOptions = new StartOrchestrationOptions
        {
            InstanceId = scheduleRequest.Id
        };

        var bike = CrazyBikeSelector.GetOne();
        var instanceId = await durableTaskClient.ScheduleNewOrchestrationInstanceAsync("CrazyBikeOrchestrator", bike, startOrchestrationOptions);
        //var metadata = await durableTaskClient.WaitForInstanceStartAsync(instanceId);
        
        //var scheduleClient = await scheduledTaskClient.CreateScheduleAsync(scheduleCreationOptions);
        //var description = await scheduleClient.DescribeAsync();

        app.Logger.LogInformation("Created new schedule with ID: {ScheduleId}", scheduleRequest.Id);
        
        return Results.CreatedAtRoute("GetOrchestration", new { id = scheduleRequest.Id });
    }
    catch (ScheduleClientValidationException ex)
    {
        app.Logger.LogError(ex, "Validation failed while creating schedule {ScheduleId}", scheduleRequest.Id);
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating schedule {ScheduleId}", scheduleRequest.Id);
        return Results.InternalServerError(ex.Message);
    }
})
.WithName("ScheduleOrchestration");

app.MapGet("/schedule/{id}", async (string id, 
        /*[FromServices] ScheduledTaskClient scheduledTaskClient*/
        [FromServices] DurableTaskClient durableTaskClient) =>
{
    try
    {
        var metadata = await durableTaskClient.GetInstanceAsync(id);
        //var schedule = await scheduledTaskClient.GetScheduleAsync(id);
        return Results.Ok(metadata);
    }
    catch (ScheduleNotFoundException)
    {
        return Results.NotFound();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving schedule {ScheduleId}", id);
        return Results.InternalServerError(ex.Message);
    }
})
.WithName("GetOrchestration");

app.Run();