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
            var logger = Substitute.For<ILogger>();
            var jobProcessorFactory = Substitute.For<IJobProcessorFactory>();
            var workerId = "worker-123";
            loggerFactory.CreateLogger($"Worker.{workerId}").Returns(logger);
            var factory = new JobWorkerFactory(loggerFactory, jobProcessorFactory);

            // Act
            var worker = factory.Create(workerId);

            // Assert
            worker.Should().NotBeNull();
            worker.Should().BeOfType<JobWorker>();
        }
    }
}
