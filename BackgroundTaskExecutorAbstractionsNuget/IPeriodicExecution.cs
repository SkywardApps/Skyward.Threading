using System;
using System.Threading.Tasks;

namespace Skyward.Threading.Abstractions
{
    public interface IPeriodicExecution
    {
        Func<Task> Action { get; }
        DateTime? LastExecution { get; set; }
        string Name { get; }
        TimeSpan Period { get; }
    }
}
