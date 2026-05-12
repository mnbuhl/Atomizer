import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useJobs, type JobFilters } from '../api/hooks';
import { routePrefix, jobsRefreshMs } from '../config';
import type { JobDto } from '../api/types';

const STATUS_OPTIONS = ['Pending', 'Processing', 'Completed', 'Failed'] as const;
const STATUS_COLORS: Record<string, string> = {
    Pending: 'bg-gray-100 text-gray-700',
    Processing: 'bg-blue-100 text-blue-700',
    Completed: 'bg-green-100 text-green-700',
    Failed: 'bg-red-100 text-red-700',
};

export default function JobsList() {
    const [filters, setFilters] = useState<JobFilters>({ skip: 0, take: 50 });
    const { data, isLoading, error, refetch, dataUpdatedAt } = useJobs(filters);

    const setPage = (skip: number) => setFilters(f => ({ ...f, skip }));
    const take = filters.take ?? 50;

    return (
        <div>
            <div className="flex justify-between items-center mb-4">
                <h2 className="text-lg font-semibold text-gray-900">Jobs</h2>
                <div className="flex items-center gap-3 text-xs text-gray-400">
                    {dataUpdatedAt > 0 && (
                        <span>Updated {new Date(dataUpdatedAt).toLocaleTimeString()}</span>
                    )}
                    <span>Auto-refreshes every {jobsRefreshMs / 1000}s</span>
                    <button
                        onClick={() => refetch()}
                        className="px-2 py-1 border border-gray-200 rounded hover:bg-gray-50 text-gray-600"
                    >
                        Refresh
                    </button>
                </div>
            </div>

            <div className="flex flex-wrap gap-3 mb-4">
                <div className="flex gap-1">
                    {STATUS_OPTIONS.map(s => (
                        <button
                            key={s}
                            onClick={() =>
                                setFilters(f => ({
                                    ...f,
                                    skip: 0,
                                    status: f.status?.includes(s)
                                        ? f.status.filter(x => x !== s)
                                        : [...(f.status ?? []), s],
                                }))
                            }
                            className={`px-2 py-1 rounded text-xs font-medium border transition-colors ${
                                filters.status?.includes(s)
                                    ? STATUS_COLORS[s] + ' border-current'
                                    : 'border-gray-200 text-gray-500 hover:bg-gray-50'
                            }`}
                        >
                            {s}
                        </button>
                    ))}
                </div>

                <input
                    type="text"
                    placeholder="Queue"
                    className="border border-gray-200 rounded px-2 py-1 text-sm"
                    value={filters.queue ?? ''}
                    onChange={e => setFilters(f => ({ ...f, skip: 0, queue: e.target.value || undefined }))}
                />

                <input
                    type="text"
                    placeholder="Payload type"
                    className="border border-gray-200 rounded px-2 py-1 text-sm"
                    value={filters.payload ?? ''}
                    onChange={e => setFilters(f => ({ ...f, skip: 0, payload: e.target.value || undefined }))}
                />

                <input
                    type="datetime-local"
                    className="border border-gray-200 rounded px-2 py-1 text-sm"
                    value={filters.from ?? ''}
                    onChange={e => setFilters(f => ({ ...f, skip: 0, from: e.target.value || undefined }))}
                />

                <input
                    type="datetime-local"
                    className="border border-gray-200 rounded px-2 py-1 text-sm"
                    value={filters.to ?? ''}
                    onChange={e => setFilters(f => ({ ...f, skip: 0, to: e.target.value || undefined }))}
                />

                <button onClick={() => setFilters({ skip: 0, take: 50 })} className="text-xs text-gray-400 hover:text-gray-700">
                    Clear
                </button>
            </div>

            {isLoading && <p className="text-sm text-gray-400">Loading…</p>}
            {error && <p className="text-sm text-red-500">Error loading jobs.</p>}
            {data && (
                <>
                    <div className="overflow-x-auto">
                        <table className="w-full text-sm border-collapse">
                            <thead>
                                <tr className="border-b border-gray-200 text-left text-gray-500 text-xs uppercase tracking-wide">
                                    <th className="py-2 pr-4">ID</th>
                                    <th className="py-2 pr-4">Type</th>
                                    <th className="py-2 pr-4">Queue</th>
                                    <th className="py-2 pr-4">Status</th>
                                    <th className="py-2 pr-4">Attempts</th>
                                    <th className="py-2">Created</th>
                                </tr>
                            </thead>
                            <tbody>
                                {data.items.map((job: JobDto) => (
                                    <tr key={job.id} className="border-b border-gray-100 hover:bg-gray-50">
                                        <td className="py-2 pr-4 font-mono text-xs text-gray-400">
                                            <Link
                                                to={`${routePrefix}/jobs/${job.id}`}
                                                className="hover:text-blue-600"
                                            >
                                                {job.id.slice(0, 8)}…
                                            </Link>
                                        </td>
                                        <td className="py-2 pr-4 text-gray-700 max-w-xs truncate">{job.payloadTypeName}</td>
                                        <td className="py-2 pr-4 text-gray-500">{job.queueKey}</td>
                                        <td className="py-2 pr-4">
                                            <span
                                                className={`px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_COLORS[job.status]}`}
                                            >
                                                {job.status}
                                            </span>
                                        </td>
                                        <td className="py-2 pr-4 text-gray-500">{job.attempts}</td>
                                        <td className="py-2 text-gray-400 text-xs">
                                            {new Date(job.createdAt).toLocaleString()}
                                        </td>
                                    </tr>
                                ))}
                                {data.items.length === 0 && (
                                    <tr>
                                        <td colSpan={6} className="py-8 text-center text-gray-400 text-sm">
                                            No jobs found.
                                        </td>
                                    </tr>
                                )}
                            </tbody>
                        </table>
                    </div>

                    <div className="flex justify-between items-center mt-3 text-sm text-gray-500">
                        <span>{data.totalCount} total</span>
                        <div className="flex gap-2">
                            <button
                                disabled={filters.skip === 0}
                                onClick={() => setPage(Math.max(0, (filters.skip ?? 0) - take))}
                                className="px-3 py-1 border border-gray-200 rounded disabled:opacity-40 hover:bg-gray-50"
                            >
                                ← Prev
                            </button>
                            <button
                                disabled={(filters.skip ?? 0) + take >= data.totalCount}
                                onClick={() => setPage((filters.skip ?? 0) + take)}
                                className="px-3 py-1 border border-gray-200 rounded disabled:opacity-40 hover:bg-gray-50"
                            >
                                Next →
                            </button>
                        </div>
                    </div>
                </>
            )}
        </div>
    );
}
