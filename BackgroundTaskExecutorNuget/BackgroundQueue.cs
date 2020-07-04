using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Skyward.Threading
{
    /// <summary>
    /// Represents a specific named queue
    /// </summary>
    internal class BackgroundQueue
    {
        public int CurrentExecutionsCount { get; set; }

        /// <summary>
        /// This is the maximum number of concurrent threads that can process this specific queue.  As this approaches 
        /// EluminateBackgroundTasks.ConcurrentBackgroundThreads, you allow for the possibility of flooding the processors
        /// with a single queue.
        /// </summary>
        public int MaximumConcurrentExecutions { get; set; }

        public Queue<KeyValuePair<string, Func<Task>>> PriorityTasks = new Queue<KeyValuePair<string, Func<Task>>>();
        public Queue<KeyValuePair<string, Func<Task>>> BackgroundTasks = new Queue<KeyValuePair<string, Func<Task>>>();

    }
}
