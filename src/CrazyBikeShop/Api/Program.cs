using System.Text.Json;
using Api;
using CrazyBikeShop.ServiceDefaults;
using CrazyBikeShop.Shared;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ScheduledTasks;
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

builder.Services.AddDurableTaskClient(b =>
{
    b.UseDurableTaskScheduler(Environment.GetEnvironmentVariable("ConnectionStrings__dts")!);
    b.UseScheduledTasks();
});
builder.Services.AddSingleton(typeof(ILogger), sp => 
    sp.GetRequiredService<ILoggerFactory>().CreateLogger("DurableTask"));

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapPost("/schedule", async (/*[FromBody] ScheduleRequest scheduleRequest,*/ [FromServices] ScheduledTaskClient scheduledTaskClient) =>
{
    var scheduleRequest = new ScheduleRequest
    {
        Id = Guid.NewGuid().ToString(),
        OrchestrationName = "CrazyBikeOrchestration",
        Interval = TimeSpan.FromSeconds(5)
    };
    
    try
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, CrazyBikeSelector.GetOne());
        stream.Position = 0;
        scheduleRequest.Input = new StreamReader(stream).ReadToEnd();
        
        var creationOptions = new ScheduleCreationOptions(scheduleRequest.Id, scheduleRequest.OrchestrationName, scheduleRequest.Interval)
        {
            OrchestrationInput = scheduleRequest.Input,
            StartAt = scheduleRequest.StartAt,
            EndAt = scheduleRequest.EndAt,
            StartImmediatelyIfLate = true
        };

        var scheduleClient = await scheduledTaskClient.CreateScheduleAsync(creationOptions);
        var description = await scheduleClient.DescribeAsync();

        app.Logger.LogInformation("Created new schedule with ID: {ScheduleId}", scheduleRequest.Id);

        //await RunSequentialOrchestrations(scheduleRequest, durableTaskClient);
        //return Results.Ok(new { Message = "Orchestrations scheduled and completed." });
        
        return Results.CreatedAtRoute("GetOrchestration", new { id = scheduleRequest.Id }, description);
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

app.MapGet("/schedule/{id}", async (string id, [FromServices] ScheduledTaskClient scheduledTaskClient) =>
{
    try
    {
        var schedule = await scheduledTaskClient.GetScheduleAsync(id);
        return Results.Ok(schedule);
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