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
        private readonly IJobProcessorFactory _jobProcessorFactory = Substitute.For<IJobProcessorFactory>();
        private readonly ILoggerFactory _loggerFactory = Substitute.For<ILoggerFactory>();
        private readonly AtomizerRuntimeIdentity _identity = new AtomizerRuntimeIdentity();

        private readonly JobWorkerFactory _sut;

        public JobWorkerFactoryTests()
        {
            _sut = new JobWorkerFactory(_loggerFactory, _jobProcessorFactory, _identity);
        }

        [Fact]
        public void Create_WhenCalledWithValidArguments_ShouldReturnJobWorkerWithCorrectDependencies()
        {
            // Arrange
            var workerIndex = 0;
            var queueKey = QueueKey.Default;

            // Act
            var worker = _sut.Create(queueKey, workerIndex);

            // Assert
            worker.Should().NotBeNull();
            worker.Should().BeOfType<JobWorker>();

            var workerId = NonPublicSpy<JobWorker>.GetFieldValue<WorkerId>("_workerId", worker as JobWorker);
            workerId.ToString().Should().Be($"{_identity.InstanceId}:*:{queueKey}:*:worker-{workerIndex}");
        }
    }
}
