using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Skyward.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackgroundTaskExecutorTests
{
    [TestClass]
    public class TestAdhocExecution
    {
        [TestMethod]
        public async Task TestDualConcurrent()
        {
            var loggerMock = new Mock<ILogger<BackgroundTaskExecutor>>();
            var executor = new BackgroundTaskExecutor(new BackgroundTaskExecutor.Config { ConcurrentGeneralBackgroundThreads = 2, ConcurrentUnnamedQueueTasks = 2 }, loggerMock.Object);
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), memberName: "Concurrent1");
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), memberName: "Concurrent2");

            executor.StartExecuteBackgroundTasks();
            await Task.Delay(TimeSpan.FromSeconds(0.5));

            var currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(2);

            executor.WaitBackgroundTasks();

            currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(0);
        }

        [TestMethod]
        public async Task TestDualConcurrentByQueue()
        {
            var loggerMock = new Mock<ILogger<BackgroundTaskExecutor>>();
            var executor = new BackgroundTaskExecutor(new BackgroundTaskExecutor.Config { ConcurrentGeneralBackgroundThreads = 2, ConcurrentUnnamedQueueTasks = 1 }, loggerMock.Object);
            executor.AddQueue("Concurrent1", 1);
            executor.AddQueue("Concurrent2", 1);
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), queueName:"Concurrent1", memberName: "Concurrent1");
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), queueName: "Concurrent2", memberName: "Concurrent2");

            executor.StartExecuteBackgroundTasks();
            await Task.Delay(TimeSpan.FromSeconds(0.5));

            var currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(2);

            executor.WaitBackgroundTasks();

            currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(0);
        }

        [TestMethod]
        public async Task TestDualConcurrentBySingleQueue()
        {
            var loggerMock = new Mock<ILogger<BackgroundTaskExecutor>>();
            var executor = new BackgroundTaskExecutor(new BackgroundTaskExecutor.Config { ConcurrentGeneralBackgroundThreads = 2, ConcurrentUnnamedQueueTasks = 1 }, loggerMock.Object);
            executor.AddQueue("Concurrent1", 2);
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), queueName: "Concurrent1", memberName: "Concurrent1");
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), queueName: "Concurrent1", memberName: "Concurrent2");

            executor.StartExecuteBackgroundTasks();
            await Task.Delay(TimeSpan.FromSeconds(0.5));

            var currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(2);

            executor.WaitBackgroundTasks();

            currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(0);
        }

        [TestMethod]
        public async Task TestDualSequential()
        {
            var loggerMock = new Mock<ILogger<BackgroundTaskExecutor>>();
            var executor = new BackgroundTaskExecutor(new BackgroundTaskExecutor.Config { ConcurrentGeneralBackgroundThreads = 2, ConcurrentUnnamedQueueTasks = 1 }, loggerMock.Object);
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), memberName: "Queue1");
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), memberName: "Queue2");

            executor.StartExecuteBackgroundTasks();
            await Task.Delay(TimeSpan.FromSeconds(0.5));

            var currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.First().ShouldBe("Adhoc : Queue1");
            currentTasks.Count.ShouldBe(1);

            await Task.Delay(TimeSpan.FromSeconds(1));

            currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.First().ShouldBe("Adhoc : Queue2");
            currentTasks.Count.ShouldBe(1);

            executor.WaitBackgroundTasks();

            currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(0);
        }

        [TestMethod]
        public async Task TestDualSequentialByQueue()
        {
            var loggerMock = new Mock<ILogger<BackgroundTaskExecutor>>();
            var executor = new BackgroundTaskExecutor(new BackgroundTaskExecutor.Config { ConcurrentGeneralBackgroundThreads = 1, ConcurrentUnnamedQueueTasks = 1 }, loggerMock.Object);
            executor.AddQueue("Queue1", 1);
            executor.AddQueue("Queue2", 1);
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), queueName: "Queue1", memberName: "Queue1");
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), queueName: "Queue2", memberName: "Queue2");

            executor.StartExecuteBackgroundTasks();
            await Task.Delay(TimeSpan.FromSeconds(0.5));

            var currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.First().ShouldBe("Adhoc Queue1: Queue1");
            currentTasks.Count.ShouldBe(1);

            await Task.Delay(TimeSpan.FromSeconds(1));

            currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.First().ShouldBe("Adhoc Queue2: Queue2");
            currentTasks.Count.ShouldBe(1);

            executor.WaitBackgroundTasks();

            currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(0);
        }

        [TestMethod]
        public async Task TestAutomaticNameNoPriority()
        {
            var loggerMock = new Mock<ILogger<BackgroundTaskExecutor>>();
            var executor = new BackgroundTaskExecutor(new BackgroundTaskExecutor.Config { ConcurrentGeneralBackgroundThreads = 1, ConcurrentUnnamedQueueTasks = 1 }, loggerMock.Object);
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)));

            executor.StartExecuteBackgroundTasks();
            await Task.Delay(TimeSpan.FromSeconds(0.5));

            var currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(1);
            currentTasks.First().ShouldBe($"Adhoc : {nameof(TestAutomaticNameNoPriority)}");

            executor.WaitBackgroundTasks();
        }

        [TestMethod]
        public async Task TestAutomaticNameWithPriority()
        {
            var loggerMock = new Mock<ILogger<BackgroundTaskExecutor>>();
            var executor = new BackgroundTaskExecutor(new BackgroundTaskExecutor.Config { ConcurrentGeneralBackgroundThreads = 1, ConcurrentUnnamedQueueTasks = 1 }, loggerMock.Object);
            executor.AddAction(async () => await Task.Delay(TimeSpan.FromSeconds(1)), false);

            executor.StartExecuteBackgroundTasks();
            await Task.Delay(TimeSpan.FromSeconds(0.5));

            var currentTasks = executor.GetCurrentExecutingTasks();
            currentTasks.Count.ShouldBe(1);
            currentTasks.First().ShouldBe($"Adhoc : {nameof(TestAutomaticNameWithPriority)}");

            executor.WaitBackgroundTasks();
        }

    }
}
