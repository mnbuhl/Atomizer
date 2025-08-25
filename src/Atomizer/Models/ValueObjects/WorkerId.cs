using Atomizer.Models.Base;

namespace Atomizer;

internal sealed class WorkerId : ValueObject
{
    private readonly string _instanceId;
    private readonly QueueKey _queueKey;
    private readonly int _workerIndex;

    public WorkerId(string instanceId, QueueKey queueKey, int workerIndex)
    {
        _instanceId = instanceId;
        _queueKey = queueKey;
        _workerIndex = workerIndex;
    }

    public override string ToString()
    {
        return $"{_instanceId}:*:{_queueKey}:*:worker-{_workerIndex}";
    }

    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return _instanceId;
        yield return _queueKey;
        yield return _workerIndex;
    }
}
