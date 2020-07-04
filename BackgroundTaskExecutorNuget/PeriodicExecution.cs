using System;
using System.Threading.Tasks;

namespace Skyward.Threading
{
    public class PeriodicExecution : IPeriodicExecution
    {
        public PeriodicExecution() { }

        public PeriodicExecution(string name, TimeSpan period, Func<Task> asyncAction, bool executeImmediately)
        {
            Name = name;
            Period = period;
            Action = asyncAction;
            LastExecution = executeImmediately ? (DateTime?)null : DateTime.UtcNow;
        }

        public string Name { get; protected set; }
        public TimeSpan Period { get; protected set; }
        public Func<Task> Action { get; protected set; }

        public DateTime? LastExecution { get; set; }
    }
}
