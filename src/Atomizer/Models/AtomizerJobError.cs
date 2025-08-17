using System;

namespace Atomizer.Models
{
    public class AtomizerJobError
    {
        public Guid Id { get; set; }
        public Guid JobId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
        public string? ExceptionType { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int Attempt { get; set; }
        public string? RuntimeIdentity { get; set; }
    }
}
