using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skyward.Threading.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Skyward.Threading
{
    /// <summary>
    /// Provide a rudimentary background queueing system for asynchronous jobs.
    /// Ideally with would be handed off to a rabbitmq cluster or something, but for now we just execute it locally.
    /// </summary>
    public class BackgroundTaskExecutor : IBackgroundTaskExecutor, IBackgroundTaskReporter
    {
        public class Config
        {
            public int ConcurrentGeneralBackgroundThreads { get; set; }
            public int ConcurrentUnnamedQueueTasks { get; set; }
        }

        public BackgroundTaskExecutor(IOptions<Config> config, ILogger<BackgroundTaskExecutor> logger)
        {
            if (config is null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _concurrentGeneralBackgroundThreads = config.Value.ConcurrentGeneralBackgroundThreads;
            BackgroundQueues = new Dictionary<string, BackgroundQueue>
            {
                // catchall queue for unnamed tasks, this will hopefully never get used
                [UnnamedQueue] = new BackgroundQueue
                {
                    MaximumConcurrentExecutions = config.Value.ConcurrentUnnamedQueueTasks
                },
                // periodic tasks
                ["periodic"] = new BackgroundQueue
                {
                    MaximumConcurrentExecutions = 0
                },
            };
        }

        // This is the total number of threads we will run for processing tasks, regardless of individual queue limites
        private readonly int _concurrentGeneralBackgroundThreads;
        private readonly ILogger<BackgroundTaskExecutor> _logger;
        private CancellationTokenSource _cancelleationSource;
        private CancellationToken? _cancellationToken;
        Thread _genericExecutionThread;
        readonly List<Thread> _concurrentExecutionThreads = new List<Thread>();

        private const string UnnamedQueue = "";
        private const string UnnamedTask = "";
        private readonly SemaphoreSlim TaskListLock = new SemaphoreSlim(1, 1);
        private readonly AutoResetEvent NewTaskEvent = new AutoResetEvent(false);
        private readonly Dictionary<string, BackgroundQueue> BackgroundQueues;


        #region Status Tracking
        private readonly SemaphoreSlim StatusLock = new SemaphoreSlim(1, 1);
        // We want to track each job, its name, when it started.
        private readonly Dictionary<Guid, (string, DateTimeOffset)> CurrentlyExecutingTasks = new Dictionary<Guid, (string, DateTimeOffset)>();
        private List<(string, DateTimeOffset, DateTimeOffset, TimeSpan)> HistoricalTasks = new List<(string, DateTimeOffset, DateTimeOffset, TimeSpan)>();
        #endregion

        private readonly Dictionary<string, IPeriodicExecution> PeriodicExecutions = new Dictionary<string, IPeriodicExecution>();

        /// <summary>
        /// Add a new queue with the specific name
        /// </summary>
        /// <param name="name"></param>
        /// <param name="concurrency"></param>
        public void AddQueue(string name, int concurrency)
        {
            // Get our lock for our items
            TaskListLock.Wait();
            try
            {
                BackgroundQueues.Add(name, new BackgroundQueue
                {
                    MaximumConcurrentExecutions = concurrency
                });
            }
            finally
            {
                TaskListLock.Release();
            }
        }

        /// <summary>
        /// Kick off the background threads for tasks execution
        /// </summary>
        public void StartExecuteBackgroundTasks()
        {
            _cancelleationSource = new CancellationTokenSource();
            _cancellationToken = _cancelleationSource.Token;

            // A single thread for periodic tasks.  
            _genericExecutionThread = new Thread(ExecutePeriodicBackgroundTasks);
            _genericExecutionThread.Start();

            // Extra threads for adhoc tasks. 
            for (int i = 0; i < _concurrentGeneralBackgroundThreads; ++i)
            {
                var thread = new Thread(ExecuteAdhocBackgroundTasks);
                thread.Start();
                _concurrentExecutionThreads.Add(thread);
            }
        }

        /// <summary>
        /// Wait for the cancelled threads to terminate
        /// </summary>
        public void WaitBackgroundTasks()
        {
            if (_cancelleationSource == null || !_cancellationToken.HasValue)
            {
                throw new InvalidOperationException("The cancellation token does not exist");
            }

            _cancelleationSource.Cancel();

            // Make sure we flag this event to make all the threads check the cancellation token
            NewTaskEvent.Set();

            _genericExecutionThread.Join();

            foreach(var thread in _concurrentExecutionThreads)
            {
                thread.Join();
            }
        }



        /// <summary>
        /// Returns the top ad-hoc background item and any pending periodic items.
        /// </summary>
        /// <returns></returns>
        private Dictionary<string, Func<Task>> GetWaitingPeriodicTasks()
        {
            DateTime now = DateTime.UtcNow;
            Dictionary<string, Func<Task>> pendingPeriodicTasks = new Dictionary<string, Func<Task>>();

            // Get our lock for our items
            TaskListLock.Wait();
            try
            {
                // Find all periodic executions that are due to be run
                foreach (var kv in PeriodicExecutions)
                {
                    if (kv.Value.LastExecution == null || now > kv.Value.LastExecution + kv.Value.Period)
                    {
                        pendingPeriodicTasks.Add(kv.Key, kv.Value.Action);
                    }
                }
            }
            finally
            {
                TaskListLock.Release();
            }

            return pendingPeriodicTasks;
        }

        /// <summary>
        /// Returns the top ad-hoc background item and any pending periodic items.
        /// </summary>
        /// <returns></returns>
        private TaskReference? GetWaitingAdhocTask()
        {
            // Get our lock for our items
            TaskListLock.Wait();
            try
            {
                // This does in theory give preference to the queue whose hash value _happens_ to come first.
                // this probably isn't a big issue, but something to note.

                // Check for priority items first
                foreach (var queueKeyValue in BackgroundQueues)
                {
                    // And take the first background item to be run
                    if (queueKeyValue.Value.PriorityTasks.Any()
                        && queueKeyValue.Value.CurrentExecutionsCount < queueKeyValue.Value.MaximumConcurrentExecutions)
                    {
                        var task = queueKeyValue.Value.PriorityTasks.Dequeue();
                        return new TaskReference { QueueName = queueKeyValue.Key, TaskName = task.Key, Action = task.Value };
                    }
                }

                foreach (var queueKeyValue in BackgroundQueues)
                {
                    // And take the first background item to be run
                    if (queueKeyValue.Value.BackgroundTasks.Any()
                        && queueKeyValue.Value.CurrentExecutionsCount < queueKeyValue.Value.MaximumConcurrentExecutions)
                    {
                        var task = queueKeyValue.Value.BackgroundTasks.Dequeue();
                        return new TaskReference { QueueName = queueKeyValue.Key, TaskName = task.Key, Action = task.Value };
                    }
                }
            }
            finally
            {
                TaskListLock.Release();
            }

            return null;
        }

        #region Status

        private Guid AddExecutingTask(string taskName, string queueName)
        {
            StatusLock.Wait();
            try
            {
                var id = Guid.NewGuid();
                BackgroundQueues[queueName].CurrentExecutionsCount++;
                CurrentlyExecutingTasks.Add(id, (taskName, DateTimeOffset.Now));
                return id;
            }
            finally
            {
                StatusLock.Release();
            }
        }

        private void RemoveExecutingTask(Guid taskId, string queueName)
        {
            StatusLock.Wait();
            try
            {
                if (!CurrentlyExecutingTasks.ContainsKey(taskId))
                {
                    _logger?.LogCritical($"Attempted to remove the task {taskId} but it wasn't in the collection");
                }
                else
                {
                    BackgroundQueues[queueName].CurrentExecutionsCount--;
                    var (taskName, taskStart) = CurrentlyExecutingTasks[taskId];
                    // Add this task to the history in reverse chronological order (top is newest)
                    HistoricalTasks.Insert(0, (taskName, taskStart, DateTimeOffset.Now, DateTimeOffset.Now - taskStart));
                    // Only keep the last 100-150 items.  We reduce to 100 so that this only triggers every fifty calls, rather than resizing
                    // on every single call
                    if (HistoricalTasks.Count() > 150)
                    {
                        HistoricalTasks = HistoricalTasks.Take(100).ToList();
                    }
                    CurrentlyExecutingTasks.Remove(taskId);
                }
            }
            finally
            {
                StatusLock.Release();
            }
        }

        public IList<string> GetCurrentExecutingTasks()
        {
            StatusLock.Wait();
            try
            {
                return (CurrentlyExecutingTasks.Select(t => t.Value.Item1)).ToList();
            }
            finally
            {
                StatusLock.Release();
            }
        }

        public IList<(string, DateTimeOffset, DateTimeOffset, TimeSpan)> GetHistoricalTasks()
        {
            StatusLock.Wait();
            try
            {
                return HistoricalTasks.ToList();
            }
            finally
            {
                StatusLock.Release();
            }
        }

        public Dictionary<string, List<string>> GetQueuedTasks()
        {
            TaskListLock.Wait();
            try
            {
                return BackgroundQueues.ToDictionary(kv => kv.Key, kv =>
                        kv.Value.PriorityTasks
                        .Union(kv.Value.BackgroundTasks)
                            .Select(task => task.Key).ToList());
            }
            finally
            {
                TaskListLock.Release();
            }
        }

        public IList<PeriodicTaskStatus> GetRegisteredPeriodicTasks()
        {
            TaskListLock.Wait();
            try
            {
                return new List<PeriodicTaskStatus>(PeriodicExecutions.Select(task => new PeriodicTaskStatus
                {
                    Name = task.Key,
                    LastExecution = task.Value.LastExecution,
                    ScheduledExecution = task.Value.LastExecution.HasValue ? task.Value.LastExecution.Value + task.Value.Period : DateTime.Now
                }));
            }
            finally
            {
                TaskListLock.Release();
            }
        }

        #endregion

        private void ExecutePeriodicBackgroundTasks()
        {
            // Loop forever
            while (true)
            {
                if(_cancellationToken.HasValue && _cancellationToken.Value.IsCancellationRequested)
                {
                    return;
                }

                // Get any tasks that are waiting
                // This would be any periodic tasks that are due, and the top item from the action queue
                var pendingPeriodicTasks = GetWaitingPeriodicTasks();

                // Loop over any due or overdue periodic executions
                foreach (var periodicExecution in pendingPeriodicTasks)
                {
                    if (_cancellationToken.HasValue && _cancellationToken.Value.IsCancellationRequested)
                    {
                        return;
                    }

                    var taskName = "Periodic: " + periodicExecution.Key;
                    var taskId = AddExecutingTask(taskName, "periodic");
                    try
                    {

                        ExecutePeriodicTask(periodicExecution);
                    }
                    finally
                    {
                        RemoveExecutingTask(taskId, "periodic");
                    }
                }


                if (!pendingPeriodicTasks.Any())
                {
                    // Wait for 5 minutes if there was nothing to do on this tick
                    // This may end earlier if someone actively adds something to the event queue.
                    NewTaskEvent.WaitOne(TimeSpan.FromSeconds(5));
                    NewTaskEvent.Reset();
                }
            }
        }


        /// <summary>
        /// Permanent loop executing tasks in the background
        /// </summary>
        private void ExecuteAdhocBackgroundTasks()
        {
            // Loop forever
            while (true)
            {
                if (_cancellationToken.HasValue && _cancellationToken.Value.IsCancellationRequested)
                {
                    return;
                }

                // Get any tasks that are waiting
                // This would be any periodic tasks that are due, and the top item from the action queue
                var adhocTask = GetWaitingAdhocTask();

                // If there was an item in the action queue, run it
                if (adhocTask != null)
                {
                    var taskName = $"Adhoc {adhocTask.Value.QueueName}: {adhocTask.Value.TaskName}";
                    var taskId = AddExecutingTask(taskName, adhocTask.Value.QueueName);
                    try
                    {
                        ExecuteAdHocTask(adhocTask.Value.Action);
                    }
                    finally
                    {
                        RemoveExecutingTask(taskId, adhocTask.Value.QueueName);
                    }
                }
                else
                {
                    // Wait for 5 seconds if there was nothing to do on this tick
                    // This may end earlier if someone actively adds something to the event queue.
                    NewTaskEvent.WaitOne(TimeSpan.FromSeconds(5));
                    NewTaskEvent.Reset();
                }
            }
        }

        /// <summary>
        /// Run a periodic task that is due, then update the execution time so it will wait the appropriate length of time.
        /// </summary>
        /// <param name="periodicExecution"></param>
        private void ExecutePeriodicTask(KeyValuePair<string, Func<Task>> periodicExecution)
        {
            // Assigns a guid that is common for all logged messages that are part of this specific task.
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["BackgroundTaskId"] = Guid.NewGuid(),
                ["BackgroundTaskType"] = "Periodic"
            }))
            {
                try
                {
                    // Run it
                    periodicExecution.Value().Wait();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"A background task {periodicExecution.Key} threw an exception");
                    // @TODO ex.VerifyLogged();
                }

                TaskListLock.Wait();
                try
                {
                    // Update the last execution time
                    PeriodicExecutions[periodicExecution.Key].LastExecution = DateTime.UtcNow;
                }
                finally
                {
                    TaskListLock.Release();
                }
            }
        }

        /// <summary>
        /// Just run the ad-hoc task
        /// </summary>
        /// <param name="item"></param>
        private void ExecuteAdHocTask(Func<Task> item)
        {
            // Assigns a guid that is common for all logged messages that are part of this specific task.
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["BackgroundTaskId"] = Guid.NewGuid(),
                ["BackgroundTaskType"] = "AdHoc"
            }))
            {
                try
                {
                    item().Wait();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An anonymous background task threw an exception");
                    // @TODO  ex.VerifyLogged();
                }
            }
        }

        /// <summary>
        /// Add an action, keyed by name, that should be executed on a certain period.  This will overwrite any existing item with the same name.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="period"></param>
        /// <param name="asyncAction"></param>
        /// <param name="executeImmediately"></param>
        public void SetPeriodicExecution(string name, TimeSpan period, Func<Task> asyncAction, bool executeImmediately = false)
        {
            TaskListLock.Wait();
            try
            {
                PeriodicExecutions[name] = new PeriodicExecution(name, period, asyncAction, executeImmediately);
            }
            finally
            {
                TaskListLock.Release();
            }

            // If this is supposed to run immediately, trigger the event indicating there is new item
            if (executeImmediately)
            {
                NewTaskEvent.Set();
            }
        }

        public void SetPeriodicExecution(IPeriodicExecution task)
        {
            TaskListLock.Wait();
            try
            {
                PeriodicExecutions[task.Name] = task;
            }
            finally
            {
                TaskListLock.Release();
            }

            NewTaskEvent.Set();
        }

        /// <summary>
        /// Add a task to the background queue if an existing task of the same name does not exist.
        /// This can be used to prevent duplication of tasks in the queue that would other wise be redundant.
        /// </summary>
        /// <param name="item">The function to add to the background queue</param>
        /// <param name="queueIfUnique">Should the task being added be unique in the queue.  Prevents multiples of an item being added to the list.</param>
        /// <param name="queueName">The name of the queue to assign this task to</param>
        /// <param name="priority">True if this should jump to the front of the queue</param>
        /// <param name="memberName">The name of the task being added.</param>
        public void AddAction(Func<Task> item, bool queueIfUnique, string queueName = null, bool priority = false, [CallerMemberName] string memberName = UnnamedTask)
        {
            if(queueName == null)
            {
                queueName = UnnamedQueue;
            }

            //test if the memberName is in the list yet and if it is return
            if (queueIfUnique &&
                ((BackgroundQueues[queueName].PriorityTasks.Union(BackgroundQueues[queueName].BackgroundTasks)
                    .Where(bgt => bgt.Key != UnnamedTask && bgt.Key == memberName).Count() != 0) ||
                 CurrentlyExecutingTasks.Values.Where(t => t.Item1.Contains(memberName)).Count() != 0))
            {
                return;
            }

            AddAction(item, queueName, priority, memberName);
        }


        /// <summary>
        /// Add a task to the background queue
        /// </summary>
        /// <param name="item">The function to add to the background queue</param>
        /// <param name="queueName">The name of the queue to assign this task to</param>
        /// <param name="priority">True if this should jump to the front of the queue</param>
        /// <param name="memberName">The name of the task being added.</param>
        public void AddAction(Func<Task> item, string queueName = null, bool priority = false, [CallerMemberName] string memberName = null)
        {
            if (queueName == null)
            {
                queueName = UnnamedQueue;
            }


            TaskListLock.Wait();
            try
            {
                var element = new KeyValuePair<string, Func<Task>>(memberName, async () =>
                {
                    for (int tries = 0; tries < 5; ++tries)
                    {
                        try
                        {
                            await item();
                            return;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Exception thrown from background task");
                            // @TODO -- hmm, should this be here as well?  Or in logging? ex.VerifyLogged();
                        }
                        await Task.Delay(137);
                    }
                });

                // Push it to the front if it is a priority, otherwise append it to the back
                if (priority)
                    BackgroundQueues[queueName].PriorityTasks.Enqueue(element);
                else
                    BackgroundQueues[queueName].BackgroundTasks.Enqueue(element);
            }
            finally
            {
                TaskListLock.Release();
            }
            NewTaskEvent.Set();
        }
    }
}
