import { useQueueStats } from '../api/hooks';
import { statsRefreshMs } from '../config';

const STATUS_COLORS = {
    pending: 'text-gray-600',
    processing: 'text-blue-600',
    completed: 'text-green-600',
    failed: 'text-red-600',
};

export default function QueueStats() {
    const { data, isLoading, error } = useQueueStats();

    return (
        <div>
            <div className="flex justify-between items-center mb-4">
                <h2 className="text-lg font-semibold text-gray-900">Queue Stats</h2>
                <span className="text-xs text-gray-400">Refreshes every {statsRefreshMs / 1000}s</span>
            </div>
            {isLoading && <p className="text-sm text-gray-400">Loading…</p>}
            {error && <p className="text-sm text-red-500">Error loading queue stats.</p>}
            {data && (
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                    {data.queues.map(q => (
                        <div key={q.queueKey} className="bg-white border border-gray-200 rounded-lg p-4">
                            <h3 className="font-medium text-gray-800 mb-3">{q.queueKey}</h3>
                            <dl className="grid grid-cols-2 gap-y-2 text-sm">
                                <dt className={STATUS_COLORS.pending}>Pending</dt>
                                <dd className={`text-right font-semibold ${STATUS_COLORS.pending}`}>{q.pending}</dd>
                                <dt className={STATUS_COLORS.processing}>Processing</dt>
                                <dd className={`text-right font-semibold ${STATUS_COLORS.processing}`}>{q.processing}</dd>
                                <dt className={STATUS_COLORS.completed}>Completed</dt>
                                <dd className={`text-right font-semibold ${STATUS_COLORS.completed}`}>{q.completed}</dd>
                                <dt className={STATUS_COLORS.failed}>Failed</dt>
                                <dd className={`text-right font-semibold ${STATUS_COLORS.failed}`}>{q.failed}</dd>
                            </dl>
                        </div>
                    ))}
                    {data.queues.length === 0 && (
                        <p className="text-sm text-gray-400 col-span-full">No queue data available.</p>
                    )}
                </div>
            )}
        </div>
    );
}
