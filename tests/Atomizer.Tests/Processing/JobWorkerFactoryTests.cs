using Atomizer.Core;
using Atomizer.Processing;
using Microsoft.Extensions.Logging;

namespace Atomizer.Tests.Processing
{
    /// <summary>
    /// Unit tests for <see cref="JobWorkerFactory"/>.
    /// </summary>
    public class JobWorkerFactoryTests
    {
        [Fact]
        public void Create_WhenCalledWithValidArguments_ShouldReturnJobWorkerWithCorrectDependencies()
        {
            // Arrange
            var loggerFactory = Substitute.For<ILoggerFactory>();
            var jobProcessorFactory = Substitute.For<IJobProcessorFactory>();
            var workerIndex = 0;
            var queueKey = QueueKey.Default;
            var identity = new AtomizerRuntimeIdentity();
            var factory = new JobWorkerFactory(loggerFactory, jobProcessorFactory, identity);

            // Act
            var worker = factory.Create(queueKey, workerIndex);

            // Assert
            worker.Should().NotBeNull();
            worker.Should().BeOfType<JobWorker>();

            worker.WorkerId.ToString().Should().Be($"{identity.InstanceId}:*:{queueKey}:*:worker-{workerIndex}");
        }
    }
}
