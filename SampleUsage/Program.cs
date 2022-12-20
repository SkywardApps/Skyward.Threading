using BackgroundTask.AspNet;
using Skyward.Threading;
using Skyward.Threading.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.Configure<BackgroundTaskExecutor.Config>(opt => {
    opt.ConcurrentUnnamedQueueTasks = 2;
    opt.ConcurrentGeneralBackgroundThreads = 2;
});

builder.Services.AddBackgroundExecutor();
builder.Services.AddSingleton(new BackgroundQueueConfig
{
    Name = "Test",
    MaximumConcurrentExecutions = 1
});

builder.Services.AddSingleton<IPeriodicExecution>(new PeriodicExecution("Example Periodic Execution", TimeSpan.FromSeconds(3), async () =>
{
    await Task.Delay(15000);
}, true));


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapBackgroundReporterEndpoint("/tasks/report");
// Add an endpoint for testing purposes that will launch a new background task
app.MapGet("/tasks/spawn", (IBackgroundTaskExecutor executor) => {
    executor.AddAction(async () => {
        await Task.Delay(5000);
    }, false, "Test", false, "Sample Adhoc Spawned: " + Guid.NewGuid().ToString());
    return "OK";
});
// Add an endpoint for testing purposes that will launch several background tasks for the same item.
app.MapGet("/tasks/spawn/duplicates", (IBackgroundTaskExecutor executor) => {

    var idString = Guid.NewGuid().ToString();
    executor.AddAction(async () => {
        await Task.Delay(5000);
        Console.WriteLine("Task 1");
    }, true, "Test", false, "Sample Adhoc Spawned: " + idString);
    executor.AddAction(async () => {
        await Task.Delay(5000);
        Console.WriteLine("Task 2");
    }, true, "Test", false, "Sample Adhoc Spawned: " + idString);
    executor.AddAction(async () =>
    {
        await Task.Delay(5000);
        Console.WriteLine("Task 3");
    }, true, "Test", false, "Sample Adhoc Spawned: " + idString);
    executor.AddAction(async () =>
    {
        await Task.Delay(5000);
        Console.WriteLine("Task 4");
    }, true, "Test", false, "Sample Adhoc Spawned: " + idString);
    executor.AddAction(async () =>
    {
        await Task.Delay(5000);
        Console.WriteLine("Task 5");
    }, true, "Test", false, "Sample Adhoc Spawned: " + idString);
    return "OK";
});

app.Run();

internal record WeatherForecast(DateTime Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
