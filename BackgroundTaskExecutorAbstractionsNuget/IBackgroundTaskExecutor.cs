using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Skyward.Threading.Abstractions
{
    public interface IBackgroundTaskExecutor
    {
        void AddAction(Func<Task> item, bool queueIfUnique, string queueName = null, bool priority = false, [CallerMemberName] string memberName = "");
        void AddAction(Func<Task> item, string queueName = null, bool priority = false, [CallerMemberName] string memberName = null);
        void AddQueue(string name, int concurrency);
        void SetPeriodicExecution(string name, TimeSpan period, Func<Task> asyncAction, bool executeImmediately = false);
        void SetPeriodicExecution(IPeriodicExecution task);
        void StartExecuteBackgroundTasks();
        void WaitBackgroundTasks();
    }
}