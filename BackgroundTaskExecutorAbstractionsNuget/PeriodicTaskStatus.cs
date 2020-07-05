using System;

namespace Skyward.Threading.Abstractions
{
    public struct PeriodicTaskStatus
    {
        public string Name { get; set; }
        public DateTime? LastExecution { get; set; }
        public DateTime? ScheduledExecution { get; set; }
    }
}
