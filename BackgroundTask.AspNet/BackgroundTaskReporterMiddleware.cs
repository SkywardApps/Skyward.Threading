using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Skyward.Threading.Abstractions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BackgroundTask.AspNet
{
    public class BackgroundTaskReporterMiddleware
    {
        private readonly RequestDelegate _next;

        public BackgroundTaskReporterMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        static string PageTemplate { get; } = (new System.IO.StreamReader(
            System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BackgroundTask.AspNet.PageTemplate.html")
            )).ReadToEnd();

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Query.ContainsKey("data"))
            {
                var reporter = context.RequestServices.GetService<IBackgroundTaskReporter>() ?? throw new NullReferenceException($"Could not retrieve service: {nameof(IBackgroundTaskReporter)}");
                context.Response.ContentType = "application/json";
                var data = System.Text.Json.JsonSerializer.Serialize(new {
                    Current = reporter.GetCurrentExecutingTasks(),
                    History = reporter.GetHistoricalTasks().Select(h => new { 
                        Name = h.Item1,
                        Started = h.Item2,
                        Completed = h.Item3,
                        Duration = h.Item4
                    }),
                    Queued = reporter.GetQueuedTasks(),
                    Periodic = reporter.GetRegisteredPeriodicTasks()
                });
                await context.Response.WriteAsync(data);
                return;
            }

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(PageTemplate);
        }
    }
}
