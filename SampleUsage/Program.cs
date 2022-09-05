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

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateTime.Now.AddDays(index),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");


app.MapBackgroundReporterEndpoint("/tasks/report");
app.MapGet("/tasks/spawn", (IBackgroundTaskExecutor executor) => {
    executor.AddAction(async () => {
        await Task.Delay(5000);
    }, false, "Test", false, "Sample Adhoc Spawned: " + Guid.NewGuid().ToString());
});


app.Run();

internal record WeatherForecast(DateTime Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}