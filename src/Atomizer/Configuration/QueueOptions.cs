using System;
using Atomizer.Abstractions;

namespace Atomizer.Configuration
{
    public sealed class QueueOptions
    {
        public QueueKey QueueKey { get; private set; }
        public int BatchSize { get; set; } = 10;
        public int DegreeOfParallelism { get; set; } = 4;
        public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(10);
        public RetryOptions RetryOptions { get; set; } = new RetryOptions();

        public QueueOptions(string queueName)
        {
            QueueKey = new QueueKey(queueName);
        }
    }
}
