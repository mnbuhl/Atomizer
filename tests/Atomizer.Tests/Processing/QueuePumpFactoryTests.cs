using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Processing;
using Microsoft.Extensions.Logging;

namespace Atomizer.Tests.Processing
{
    /// <summary>
    /// Unit tests for <see cref="QueuePumpFactory"/>.
    /// </summary>
    public class QueuePumpFactoryTests
    {
        private readonly IQueuePoller _queuePoller = Substitute.For<IQueuePoller>();
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory =
            Substitute.For<IAtomizerStorageScopeFactory>();
        private readonly ILoggerFactory _loggerFactory = Substitute.For<ILoggerFactory>();
        private readonly AtomizerRuntimeIdentity _identity = new AtomizerRuntimeIdentity();
        private readonly IJobWorkerFactory _workerFactory = Substitute.For<IJobWorkerFactory>();

        private readonly QueuePumpFactory _sut;

        public QueuePumpFactoryTests()
        {
            _sut = new QueuePumpFactory(_queuePoller, _storageScopeFactory, _loggerFactory, _identity, _workerFactory);
        }

        [Fact]
        public void Create_WhenCalledWithValidArguments_ShouldReturnQueuePumpWithCorrectDependencies()
        {
            // Arrange
            var queueOptions = new QueueOptions(QueueKey.Default);

            // Act
            var pump = _sut.Create(queueOptions);

            // Assert
            pump.Should().NotBeNull();
            pump.Should().BeOfType<QueuePump>();

            var leaseToken = NonPublicSpy.GetFieldValue<QueuePump, LeaseToken>("_leaseToken", pump as QueuePump);
            leaseToken.ToString().Should().StartWith($"{_identity.InstanceId}:*:{queueOptions.QueueKey}:*:");
        }
    }
}
