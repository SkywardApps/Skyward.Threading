using System;
using System.Threading.Tasks;

namespace Skyward.Threading
{
    /// <summary>
    /// Represents a specific task in a queue
    /// </summary>
    internal struct TaskReference
    {
        public string QueueName { get; set; }
        public string TaskName { get; set; }
        public Func<Task> Action { get; set; }
    }
}
