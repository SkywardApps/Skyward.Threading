# Getting Started

Assuming you are using this in an ASP.NET 5+ project:

```csharp
// Configure the service with your concurrency settings:
builder.Services.Configure<BackgroundTaskExecutor.Config>(opt => {
    opt.ConcurrentUnnamedQueueTasks = 4; // How many items that aren't added to a specific queue to run in parallel.
    opt.ConcurrentGeneralBackgroundThreads = 8; // How many items across all queues can be run in parallel in total.
});

// Add the service itself
builder.Services.AddBackgroundExecutor();

// Add any dedicated named queues
builder.Services.AddSingleton(new BackgroundQueueConfig
{
    Name = "MyQueue",
    MaximumConcurrentExecutions = 2
});

// Add any periodic tasks 
// Add a sample that runs every 3 hours
builder.Services.AddSingleton<IPeriodicExecution>(new PeriodicExecution("Example 3 hour periodic execution", TimeSpan.FromHours(3), async () =>
{
    // Do whatever work here should happen every 3 hours.  This may be to add additional background tasks in the queue.
    await Task.Delay(15000);
}, true));
```

Now you can also add actions as desired by getting a `IBackgroundTaskExecutor` injected.

```csharp
app.MapGet("/tasks/spawn", (IBackgroundTaskExecutor executor) => {
    executor.AddAction(async () => {
        await Task.Delay(5000);
    }, false, "Test", false, "Sample Adhoc Spawned: " + Guid.NewGuid().ToString());
});
```

# Overview

This is a system for running items in background queues.  All tasks run locally in-process, so this is not a replacement for eg Redis or RabbitMQ or Kafka, 
but is a simpler implementation of deferring worker items.

While it is not required to run this in a particular host, it is typically used in something like ASP.NET in order to offload long-running work items and 
requests from the endpoint request flow.

## Features

### Concurrent Execution

The system allows you to configure how many jobs can run concurrently; Each slot will allow a single job at a time to run in a background thread, and once completed, the next 
waiting job will start.  

If there are empty slots and no waiting jobs, when you add a job it will immediately start in the available slot.

### Named Queues

You can define individual 'queues', each with their own concurrently restriction.  Adding a job to a named queue will further refine how many slots are 
available for that job to run.

Note that these are limits, not reservations!  Their purpose is to throttle any individual type of work so that it doesn't 'starve out' other queues.
For example, if you can configured:

 * Maximum concurrent execution to be 8 slots
 * Queue 1 is limited to 4 slots
 * Queue 2 is limited to 4 slots
 # Queue 3 is limited to 4 slots

In this case, any individual queue will only ever take at most 4 concurrent slots for the jobs in its queue.  If all queues have waiting items, however,
there will still never be more than 8 jobs executing at the same time.   In this way, even if Queue 1 has 100 jobs waiting, if you add an item to Queue 2 or 3
it will still have a slot available and run quickly.

### Periodic Jobs

You can define jobs that should run periodically.  This is _similar_ in some ways to a cron job, but it is aimed more at "run a job roughly every X minutes", 
and does not explicitly support "run a job at X time".  There are also no guarantees that the job _will_ run at that time, but the service will make its best effort
to do so.  If you configure a periodic job to run every hour, but it takes 2 hours to run, then only _one_ will run at a time and the end result is that it will
only run once every two hours.

This is true _between_ jobs as well, so if one job takes an hour and a second should be running every 30 seconds, the second job will be blocked behind the hour-long
process.  For this reason it is common for long processes to be again queued in a background thread -- so a job will run every hour and add the hour-long process to 
a queue, to offload it and not block.

### Job uniqueness

Jobs can be given names that can be declared as unique -- if you add a second job to the queue while the first one is in the queue or running, the second version
will be discareded.  This can be helpful for 'change notifications' in which you don't care about the number of changes reported, just that you have processed the 
results as of the last change.

### Rudimentary priority

Jobs can be declared as priority jobs in which case they can be added to the _front_ of the queue.  This is a very naive implementation and can lead to critical problems
such as permanently blocked jobs and reverse-order processing, so ideally avoid this unless you have very clearly defined and limited mechanics.

## Usage

The first thing you will want to do is configure your service.  You can do this via the normal mechanics of ASP.NET configuration:

```csharp
// Configure the service with your concurrency settings:
builder.Services.Configure<BackgroundTaskExecutor.Config>(opt => {
    opt.ConcurrentUnnamedQueueTasks = 4; // How many items that aren't added to a specific queue to run in parallel.
    opt.ConcurrentGeneralBackgroundThreads = 8; // How many items across all queues can be run in parallel in total.
});
```

```csharp
// Or perhaps you are loading from appsettings.json:
builder.Services.Configure(Configuration.GetSection("BackgroundQueueConfig"));
```

The service comes with a single built-in 'unnamed' queue, essentially named vi an empty string.  This is used for any jobs not added to a specific queue. 
These could be small miscellaneous jobs that it doesn't make sense to dedicate specific resources to.

You can add additional named queues by adding a `BackgroundQueueConfig` singleton to the services for each queue.  When the service starts it will locate
all instances of `BackgroundQueueConfig` in the service provider and create a queue for each one.

```csharp
builder.Services.AddSingleton(new BackgroundQueueConfig
{
    Name = "QueueName",
    MaximumConcurrentExecutions = 2 // The limit of how many jobs from this queue can possibly run at the same time.
});
```

Adding the service itself is straightforward:
```csharp
builder.Services.AddBackgroundExecutor();
```

Behind the scenes this is adding the executor itself, two interfaces for interacting with it, and a hosted service dedicated to running any
periodic jobs you have configured.

To configure periodic jobs, add an IPeriodicExecution singleton to the service collection.  We have provided a concrete PeriodicExecution class 
to allow for quick job definitions, but this can be any class that implements the IPeriodicExecution interface -- allowing you to use the dependency
injection process within your definition.

You will need to indicate, additionally:
 * How long the system should wait between executions
 * Whether the task should run immediately and then wait the period, or not run a first execution until after the period is over.

```csharp
builder.Services.AddSingleton<IPeriodicExecution>(new PeriodicExecution("Example Periodic Execution", TimeSpan.FromSeconds(3), async () =>
{
    await Task.Delay(15000);
}, true));
```

We have recently added a 'Reporter' middleware that will allow you to view the contents of the background queues and some limited history.  This is
for diagnostic and debugging purposes only.

It will return a `IEndpointConventionBuilder` so you may add any additional ASP.NET wrappings around it, such as authorization requirements.

```csharp
app.MapBackgroundReporterEndpoint("/tasks/report").AllowAnonymous();
```

From within your controllers you may now add jobs to a background queue as needed:
```csharp
[ApiController]
public class ExampleController : ControllerBase
{
    private readonly IBackgroundTaskExecutor _taskExecutor;

    public ExampleController(IBackgroundTaskExecutor taskExecutor)
    {
        _taskExecutor = taskExecutor;
    }

    [Httpost, Route("start_long_job")]
    public async Task<ActionResult> StartJob(string userId)
    {
        string name = $"{nameof(StartJob)}:{userId}"; // We will restrict the work so that each user can only be in the queue once for this particular job.
        _taskExecutor.AddAction(
            DoWork,     // The work to perform -- an async task
            true,       // Optionally restrict to unique jobs in the queue -- will enforce name uniqueness
            "QueueName",// Optionally the specific name of a queue to add this work to.  Will default to the unnamed queue.
            false,      // If true this job is priority and will be added to the front of the queue
            name        // The name of the job. This is used for reporting and, if this job is labeled unique, will be the unique key.
        );
        return this.Ok();
    }

    /**
    * It is helpful to put the work code in a static method to ensure you don't accidentally capture any request-scoped items, as they
    * may have been disposed of by the time the background task has to do its job.
    */
    private static async Task DoWork()
    {
        await Task.Delay(15000);
    }
}
```

## Examples

See the [SampleUsage](./SampleUsage) project for examples of starting up a queue and creating some tasks.

# Contributing

Please note we have a [code of conduct](./CODE_OF_CONDUCT.md), please follow it in all your interactions with the project.

You can find our guide to contributing [here](./CONTRIBUTING.md).