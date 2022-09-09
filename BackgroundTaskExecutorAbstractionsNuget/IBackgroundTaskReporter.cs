using System;
using System.Collections.Generic;

namespace Skyward.Threading.Abstractions
{
    public interface IBackgroundTaskReporter
    {
        IList<string> GetCurrentExecutingTasks();
        IList<(string, DateTimeOffset, DateTimeOffset, TimeSpan)> GetHistoricalTasks();
        Dictionary<string, List<string>> GetQueuedTasks();
        IList<PeriodicTaskStatus> GetRegisteredPeriodicTasks();
        CurrentConfiguration GetCurrentConfiguration();
    }
}