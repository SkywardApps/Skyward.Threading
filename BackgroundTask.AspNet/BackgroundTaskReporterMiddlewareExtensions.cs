using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Skyward.Threading;
using Skyward.Threading.Abstractions;
using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundTask.AspNet
{
    public static class BackgroundTaskReporterMiddlewareExtensions
    {
        /// <summary>
        /// Report the background queue status on a specific endpoint
        /// </summary>
        public static IEndpointConventionBuilder MapBackgroundReporterEndpoint(
            this IEndpointRouteBuilder endpoints, string endpointPath)
        {
            var pipeline = endpoints.CreateApplicationBuilder()
                .UseMiddleware<BackgroundTaskReporterMiddleware>()
                .Build();

            return endpoints.MapGet(endpointPath, pipeline).WithDisplayName("Background Task Reporter");
        }

        /// <summary>
        /// Add the required services for background task execution
        /// 
        /// Make sure to call builder.Services.Configure<BackgroundTaskExecutor.Config>(...) to configure the options here.
        /// </summary>
        public static IServiceCollection AddBackgroundExecutor(this IServiceCollection self)
        {
            self.AddSingleton<BackgroundTaskExecutor>();
            self.AddSingleton<IBackgroundTaskExecutor>((s) => s.GetRequiredService<BackgroundTaskExecutor>());
            self.AddSingleton<IBackgroundTaskReporter>((s) => s.GetRequiredService<BackgroundTaskExecutor>());
            self.AddHostedService<PeriodicJobExecutionService>();

            return self;
        }

        /// <summary>
        /// Add the required services for background task execution and configure the basic options.
        /// </summary>
        public static IServiceCollection AddBackgroundExecutor(this IServiceCollection self, BackgroundTaskExecutor.Config config)
        {
            self.Configure<BackgroundTaskExecutor.Config>(opt => {
                opt.ConcurrentUnnamedQueueTasks = config.ConcurrentUnnamedQueueTasks;
                opt.ConcurrentGeneralBackgroundThreads = config.ConcurrentGeneralBackgroundThreads;
            });

            AddBackgroundExecutor(self);
            return self;
        }
    }
}
