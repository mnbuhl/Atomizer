using System;
using Atomizer.Abstractions;

namespace Atomizer.Configuration
{
    public sealed class QueueOptions
    {
        public QueueKey QueueKey { get; set; } = null!;
        public QueueType QueueType { get; set; } = QueueType.Default;
        public int? BatchSize { get; set; }
        public int? DegreeOfParallelism { get; set; }
        public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(10);
        public RetryOptions RetryOptions { get; set; } = new RetryOptions();
    }

    public enum QueueType
    {
        Default,
        Fifo,
    }
}
