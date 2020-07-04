using System;

namespace Skyward.Threading
{
    public struct PeriodicTaskStatus
    {
        public string Name { get; set; }
        public DateTime? LastExecution { get; set; }
        public DateTime? ScheduledExecution { get; set; }
    }
}
