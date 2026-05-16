using System.Text;
using StackExchange.Redis;

namespace Atomizer.Redis.Storage;

internal sealed class RedisStorageKeys
{
    private readonly string _prefix;

    public RedisStorageKeys(string prefix)
    {
        _prefix = prefix.TrimEnd(':');
    }

    public RedisKey InsertLock => Key("locks", "insert");

    public RedisKey SchedulesLock => Key("locks", "schedules");

    public RedisKey HeartbeatsLock => Key("locks", "heartbeats");

    public RedisKey JobsByCreated => Key("jobs", "created");

    public RedisKey SchedulesByNextRun => Key("schedules", "next-run");

    public RedisKey ServersByHeartbeat => Key("servers", "heartbeats");

    public RedisKey Job(Guid id) => Key("jobs", id.ToString("N"));

    public RedisKey Idempotency(string idempotencyKey) => Key("idempotency", Encode(idempotencyKey));

    public RedisKey PartitionSequence(QueueKey queueKey, PartitionKey partitionKey) =>
        Key("partition-sequences", Encode(queueKey.Key), Encode(partitionKey.Key));

    public RedisKey LeaseSet(string leaseToken) => Key("leases", Encode(leaseToken));

    public RedisKey QueueLease(QueueKey queueKey) => Key("locks", "queues", Encode(queueKey.Key));

    public RedisKey Schedule(string jobKey) => Key("schedules", Encode(jobKey));

    public RedisKey Server(string instanceId) => Key("servers", Encode(instanceId));

    private RedisKey Key(params string[] parts) => _prefix + ":" + string.Join(":", parts);

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
