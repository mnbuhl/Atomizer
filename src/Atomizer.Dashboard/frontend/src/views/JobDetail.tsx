import { useParams, Link } from 'react-router-dom';
import { useJob } from '../api/hooks';
import { routePrefix } from '../config';

const STATUS_COLORS: Record<string, string> = {
    Pending: 'bg-gray-100 text-gray-700',
    Processing: 'bg-blue-100 text-blue-700',
    Completed: 'bg-green-100 text-green-700',
    Failed: 'bg-red-100 text-red-700',
};

export default function JobDetail() {
    const { id } = useParams<{ id: string }>();
    const { data: job, isLoading, error } = useJob(id!);

    if (isLoading) return <p className="text-sm text-gray-400">Loading…</p>;
    if (error || !job) return (
        <div>
            <p className="text-sm text-red-500 mb-2">Job not found.</p>
            <Link to={`${routePrefix}/jobs`} className="text-sm text-blue-600 hover:underline">
                ← Back to jobs
            </Link>
        </div>
    );

    let formattedPayload = job.payload ?? '';
    try {
        if (job.payload) formattedPayload = JSON.stringify(JSON.parse(job.payload), null, 2);
    } catch { /* not valid JSON, show raw */ }

    return (
        <div className="space-y-6">
            <div className="flex items-start justify-between">
                <div>
                    <Link to={`${routePrefix}/jobs`} className="text-sm text-gray-400 hover:text-gray-700">
                        ← Jobs
                    </Link>
                    <h2 className="font-semibold text-gray-900 mt-1 font-mono text-base">{job.id}</h2>
                    <div className="flex gap-2 mt-1 text-sm text-gray-500">
                        <span>{job.payloadTypeName}</span>
                        <span>·</span>
                        <span>Queue: {job.queueKey}</span>
                        <span>·</span>
                        <span>Attempts: {job.attempts}</span>
                    </div>
                </div>
                <span className={`px-3 py-1 rounded-full text-sm font-medium ${STATUS_COLORS[job.status]}`}>
                    {job.status}
                </span>
            </div>

            <div className="grid grid-cols-2 gap-3 text-sm">
                <div>
                    <span className="text-gray-400">Created</span>
                    <br />
                    {new Date(job.createdAt).toLocaleString()}
                </div>
                {job.scheduledAt && (
                    <div>
                        <span className="text-gray-400">Scheduled</span>
                        <br />
                        {new Date(job.scheduledAt).toLocaleString()}
                    </div>
                )}
                {job.completedAt && (
                    <div>
                        <span className="text-gray-400">Completed</span>
                        <br />
                        {new Date(job.completedAt).toLocaleString()}
                    </div>
                )}
                {job.failedAt && (
                    <div>
                        <span className="text-gray-400">Failed</span>
                        <br />
                        {new Date(job.failedAt).toLocaleString()}
                    </div>
                )}
            </div>

            {formattedPayload && (
                <div>
                    <h3 className="text-sm font-medium text-gray-700 mb-2">Payload</h3>
                    <pre className="bg-gray-50 border border-gray-200 rounded p-3 text-xs overflow-x-auto">
                        {formattedPayload}
                    </pre>
                </div>
            )}

            {job.errors.length > 0 && (
                <div>
                    <h3 className="text-sm font-medium text-gray-700 mb-2">Error History ({job.errors.length})</h3>
                    <div className="space-y-3">
                        {job.errors.map(err => (
                            <details key={err.attempt} className="border border-red-100 rounded bg-red-50">
                                <summary className="px-3 py-2 cursor-pointer text-sm text-red-700 font-medium">
                                    Attempt {err.attempt} — {err.exceptionType}
                                </summary>
                                <div className="px-3 pb-3 text-xs space-y-2">
                                    <p className="text-red-600">{err.message}</p>
                                    <p className="text-gray-400">{new Date(err.occurredAt).toLocaleString()}</p>
                                    {err.stackTrace && (
                                        <pre className="bg-white border border-red-100 rounded p-2 overflow-x-auto text-gray-600">
                                            {err.stackTrace}
                                        </pre>
                                    )}
                                </div>
                            </details>
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
}
