using Castle.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Skyward.Threading;
using Skyward.Threading.Abstractions;
using System.Linq;

namespace BackgroundTaskExecutorTests
{
    [TestClass]
    public class TestReporter
    {
        [TestMethod]
        public void TestEmptyQueues()
        {
            var loggerMock = new Mock<ILogger<BackgroundTaskExecutor>>();
            var executor = new BackgroundTaskExecutor(new OptionsWrapper<BackgroundTaskExecutor.Config >(new BackgroundTaskExecutor.Config { ConcurrentGeneralBackgroundThreads = 1, ConcurrentUnnamedQueueTasks = 1 }), loggerMock.Object);
            var reporter = (IBackgroundTaskReporter)executor;

            reporter.GetCurrentExecutingTasks().ShouldBeEmpty();
            reporter.GetHistoricalTasks().ShouldBeEmpty();
            reporter.GetQueuedTasks().Count().ShouldBe(2); // generic adhock, and periodic
            reporter.GetQueuedTasks().Values.Any(v => v.Any()).ShouldBeFalse();
            reporter.GetRegisteredPeriodicTasks().ShouldBeEmpty();
        }
    }
}
