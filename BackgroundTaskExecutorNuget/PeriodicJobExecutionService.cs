using Microsoft.Extensions.Logging;
using Skyward.Threading.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Skyward.Threading
{
    /// <summary>
    /// A hosted background service that will periodically execute jobs on a custom schedule
    /// </summary>
    public class PeriodicJobExecutionService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly ILogger<PeriodicJobExecutionService> _logger;
        private readonly IBackgroundTaskExecutor _executor;

        public PeriodicJobExecutionService(ILogger<PeriodicJobExecutionService> logger, IEnumerable<IPeriodicExecution> queuedTasks, IEnumerable<BackgroundQueueConfig> backgroundQueueConfigs, IBackgroundTaskExecutor executor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            foreach(var config in backgroundQueueConfigs)
            {
                _executor.AddQueue(config.Name, config.MaximumConcurrentExecutions);
            }

            foreach(var task in queuedTasks)
            {
                _executor.SetPeriodicExecution(task);
            }
        }

        /// <summary>
        /// The function that is executed as the background thread
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PeriodicJobExecutionService ExecuteAsync");
            // Bail out if a cancellation was requested.
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug($"PeriodicJobExecutionService cancelled");
                return;
            }

            _executor.StartExecuteBackgroundTasks();

            while(!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
            }

            _executor.WaitBackgroundTasks();
        }
    }
}
