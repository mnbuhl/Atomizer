using System;
using Atomizer.Models.Base;

namespace Atomizer
{
    public class AtomizerJobError : Model
    {
        public Guid JobId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
        public string? ExceptionType { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int Attempt { get; set; }
        public string? RuntimeIdentity { get; set; }

        public static AtomizerJobError Create(
            Guid jobId,
            DateTimeOffset createdAt,
            int attempt,
            Exception? exception,
            string? runtimeIdentity
        )
        {
            var stackTrace = exception?.StackTrace;
            return new AtomizerJobError
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                ErrorMessage = exception?.Message,
                StackTrace = stackTrace?.Length > 5120 ? stackTrace[..5120] : stackTrace,
                ExceptionType = exception?.GetType().FullName,
                CreatedAt = createdAt,
                Attempt = attempt,
                RuntimeIdentity = runtimeIdentity,
            };
        }
    }
}
