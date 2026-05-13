import type { KeyboardEvent } from 'react';
import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useJobs, useJobStatusCounts, type JobFilters } from '../api/hooks';
import { routePrefix, jobsRefreshMs } from '../config';
import type { JobDto } from '../api/types';
import {
    cx,
    EmptyState,
    formatNumber,
    MetricCard,
    PageHeader,
    Panel,
    RelativeTime,
    StatusPill,
} from '../components/DashboardUi';
import { useNow } from '../hooks/useNow';
import { getJobPageWindow } from './jobsPagination';

const STATUS_OPTIONS = ['Pending', 'Processing', 'Completed', 'Failed'] as const;
const PAGE_SIZE = 20;
const TIME_FILTERS = [
    { key: 'all', label: 'All time', getFrom: () => undefined },
    { key: '15m', label: '15m', getFrom: () => new Date(Date.now() - 15 * 60 * 1000).toISOString() },
    { key: '1h', label: '1h', getFrom: () => new Date(Date.now() - 60 * 60 * 1000).toISOString() },
    { key: '24h', label: '24h', getFrom: () => new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString() },
    { key: '7d', label: '7d', getFrom: () => new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString() },
] as const;

export default function JobsList() {
    const [searchParams] = useSearchParams();
    const navigate = useNavigate();
    const now = useNow(1_000);
    const [timeFilter, setTimeFilter] = useState<(typeof TIME_FILTERS)[number]['key']>('all');
    const [filters, setFilters] = useState<JobFilters>(() => ({
        skip: 0,
        take: PAGE_SIZE,
        queue: searchParams.get('queue') ?? undefined,
        payload: searchParams.get('payload') ?? undefined,
        status: searchParams.getAll('status').length > 0 ? searchParams.getAll('status') : undefined,
    }));
    const { data, isLoading, error, refetch, dataUpdatedAt, isFetching } = useJobs(filters);
    const { counts, isLoading: countsLoading } = useJobStatusCounts(filters);

    const take = filters.take ?? PAGE_SIZE;
    const currentPage = Math.floor((filters.skip ?? 0) / take) + 1;
    const visibleCount = data?.items.length ?? 0;
    const pageWindow = getJobPageWindow(filters.skip ?? 0, visibleCount, data?.totalCount ?? 0);

    const setPage = (skip: number) => setFilters(f => ({ ...f, skip }));
    const openJob = (jobId: string) => navigate(`${routePrefix}/jobs/${jobId}`);

    const handleRowKeyDown = (event: KeyboardEvent<HTMLTableRowElement>, jobId: string) => {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            openJob(jobId);
        }
    };

    const applyTimeFilter = (key: (typeof TIME_FILTERS)[number]['key']) => {
        const selected = TIME_FILTERS.find(option => option.key === key)!;
        setTimeFilter(key);
        setFilters(f => ({ ...f, skip: 0, from: selected.getFrom(), to: undefined }));
    };

    const clearFilters = () => {
        setTimeFilter('all');
        setFilters({ skip: 0, take: PAGE_SIZE });
    };

    return (
        <div className="space-y-6">
            <PageHeader
                eyebrow="Live queue monitor"
                title="Jobs"
                description="A focused view of queued, running, completed, and failed jobs. Open a row to inspect payload and error history."
                actions={
                    <>
                        <div className="rounded-2xl bg-slate-100 px-3 py-2 text-xs font-medium text-slate-600">
                            Updated <RelativeTime value={dataUpdatedAt || null} now={now} />
                        </div>
                        <button
                            onClick={() => refetch()}
                            className="rounded-2xl bg-slate-950 px-4 py-2 text-sm font-semibold text-white shadow-lg shadow-slate-900/20 transition hover:-translate-y-0.5 hover:bg-slate-800 disabled:opacity-60"
                            disabled={isFetching}
                        >
                            {isFetching ? 'Refreshing…' : 'Refresh'}
                        </button>
                    </>
                }
            />

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                <MetricCard
                    label="Matching jobs"
                    value={formatNumber(data?.totalCount ?? 0, true)}
                    helper={`${formatNumber(visibleCount)} visible on page ${currentPage}`}
                    tone="blue"
                />
                <MetricCard
                    label="Pending"
                    value={countsLoading ? '—' : formatNumber(counts.Pending)}
                    helper="Matching queued backlog"
                    tone="amber"
                />
                <MetricCard
                    label="Processing"
                    value={countsLoading ? '—' : formatNumber(counts.Processing)}
                    helper="Matching leased work"
                    tone="cyan"
                />
                <MetricCard
                    label="Failed"
                    value={countsLoading ? '—' : formatNumber(counts.Failed)}
                    helper="Matching jobs needing attention"
                    tone="red"
                />
            </div>

            <Panel>
                <div className="border-b border-slate-200/70 bg-gradient-to-r from-white via-sky-50/60 to-violet-50/40 p-5">
                    <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
                        <div>
                            <h2 className="text-base font-semibold text-slate-950">Filters</h2>
                            <p className="mt-1 text-sm text-slate-500">
                                Narrow the table without exposing noisy full date values.
                            </p>
                        </div>

                        <div className="flex flex-wrap gap-2">
                            {TIME_FILTERS.map(option => (
                                <button
                                    key={option.key}
                                    onClick={() => applyTimeFilter(option.key)}
                                    className={cx(
                                        'rounded-full px-3 py-1.5 text-xs font-semibold ring-1 ring-inset transition',
                                        timeFilter === option.key
                                            ? 'bg-slate-950 text-white ring-slate-950'
                                            : 'bg-white text-slate-500 ring-slate-200 hover:bg-slate-50 hover:text-slate-900',
                                    )}
                                >
                                    {option.label}
                                </button>
                            ))}
                        </div>
                    </div>

                    <div className="mt-5 grid gap-3 lg:grid-cols-[1fr_1fr_auto]">
                        <label className="block">
                            <span className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">Queue</span>
                            <input
                                type="text"
                                placeholder="default"
                                className="mt-2 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                value={filters.queue ?? ''}
                                onChange={e => setFilters(f => ({ ...f, skip: 0, queue: e.target.value || undefined }))}
                            />
                        </label>

                        <label className="block">
                            <span className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">
                                Payload type
                            </span>
                            <input
                                type="text"
                                placeholder="Namespace.JobPayload"
                                className="mt-2 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                value={filters.payload ?? ''}
                                onChange={e => setFilters(f => ({ ...f, skip: 0, payload: e.target.value || undefined }))}
                            />
                        </label>

                        <button
                            onClick={clearFilters}
                            className="self-end rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-500 transition hover:border-slate-300 hover:text-slate-900"
                        >
                            Clear
                        </button>
                    </div>

                    <div className="mt-4 flex flex-wrap gap-2">
                        {STATUS_OPTIONS.map(status => {
                            const active = filters.status?.includes(status);
                            return (
                                <button
                                    key={status}
                                    onClick={() =>
                                        setFilters(f => ({
                                            ...f,
                                            skip: 0,
                                            status: f.status?.includes(status)
                                                ? f.status.filter(x => x !== status)
                                                : [...(f.status ?? []), status],
                                        }))
                                    }
                                    className={cx(
                                        'rounded-full px-3 py-1.5 text-xs font-semibold ring-1 ring-inset transition',
                                        active
                                            ? 'bg-slate-950 text-white ring-slate-950'
                                            : 'bg-slate-50 text-slate-500 ring-slate-200 hover:bg-white hover:text-slate-900',
                                    )}
                                >
                                    {status}
                                </button>
                            );
                        })}
                    </div>
                </div>

                {isLoading && <div className="p-8 text-sm text-slate-500">Loading jobs…</div>}
                {error && <div className="p-8 text-sm font-medium text-rose-600">Error loading jobs.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[900px] text-left text-sm">
                                <thead>
                                    <tr className="border-b border-slate-200/80 bg-slate-50/80 text-xs uppercase tracking-[0.16em] text-slate-400">
                                        <th className="px-5 py-4 font-semibold">Job</th>
                                        <th className="px-5 py-4 font-semibold">Status</th>
                                        <th className="px-5 py-4 font-semibold">Queue / Partition</th>
                                        <th className="px-5 py-4 font-semibold">Attempts</th>
                                        <th className="px-5 py-4 font-semibold">Timing</th>
                                        <th className="px-5 py-4 font-semibold">Payload</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100">
                                    {data.items.map((job: JobDto) => (
                                        <tr
                                            key={job.id}
                                            role="link"
                                            tabIndex={0}
                                            aria-label={`Open job ${job.id}`}
                                            onClick={() => openJob(job.id)}
                                            onKeyDown={event => handleRowKeyDown(event, job.id)}
                                            className="group cursor-pointer bg-white/70 transition hover:bg-sky-50/70 focus:bg-sky-50/70 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-sky-300"
                                        >
                                            <td className="px-5 py-4">
                                                <div className="font-mono text-sm font-semibold text-slate-900">
                                                    {job.id.slice(0, 8)}…
                                                </div>
                                                <div className="mt-1 text-xs text-slate-400">
                                                    Created <RelativeTime value={job.createdAt} now={now} />
                                                </div>
                                            </td>
                                            <td className="px-5 py-4">
                                                <StatusPill status={job.status} />
                                            </td>
                                            <td className="px-5 py-4">
                                                <span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-semibold text-slate-600">
                                                    {job.queueKey}
                                                </span>
                                                <div className="mt-2 flex items-center gap-2 text-xs text-slate-500">
                                                    <span className="max-w-36 truncate">
                                                        {job.partitionKey ? `Partition ${job.partitionKey}` : 'Unpartitioned'}
                                                    </span>
                                                    {job.sequenceNumber !== null && (
                                                        <span className="rounded-full bg-indigo-50 px-2 py-0.5 font-semibold text-indigo-600">
                                                            #{formatNumber(job.sequenceNumber)}
                                                        </span>
                                                    )}
                                                </div>
                                            </td>
                                            <td className="px-5 py-4 text-slate-600">{formatNumber(job.attempts)}</td>
                                            <td className="px-5 py-4 text-slate-600">
                                                <JobTiming job={job} now={now} />
                                            </td>
                                            <td className="max-w-xs px-5 py-4">
                                                <div className="truncate text-slate-700">{job.payloadTypeName}</div>
                                                <div className="mt-1 text-xs font-semibold text-sky-600 opacity-0 transition group-hover:opacity-100 group-focus:opacity-100">
                                                    Open details →
                                                </div>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>

                        {data.items.length === 0 && (
                            <EmptyState
                                title="No jobs found"
                                description="Try clearing filters or broadening the relative time window."
                            />
                        )}

                        <div className="flex flex-col gap-3 border-t border-slate-200/80 px-5 py-4 text-sm text-slate-500 sm:flex-row sm:items-center sm:justify-between">
                            <span>
                                Showing {pageWindow.label} of {formatNumber(data.totalCount)} matching jobs · page size{' '}
                                {take} · auto-refresh{' '}
                                {jobsRefreshMs / 1000}s
                            </span>
                            <div className="flex gap-2">
                                <button
                                    disabled={filters.skip === 0}
                                    onClick={() => setPage(Math.max(0, (filters.skip ?? 0) - take))}
                                    className="rounded-xl border border-slate-200 bg-white px-3 py-2 font-semibold text-slate-600 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-40"
                                >
                                    ← Prev
                                </button>
                                <button
                                    disabled={(filters.skip ?? 0) + take >= data.totalCount}
                                    onClick={() => setPage((filters.skip ?? 0) + take)}
                                    className="rounded-xl border border-slate-200 bg-white px-3 py-2 font-semibold text-slate-600 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-40"
                                >
                                    Next →
                                </button>
                            </div>
                        </div>
                    </>
                )}
            </Panel>
        </div>
    );
}

function JobTiming({ job, now }: { job: JobDto; now: number }) {
    if (job.failedAt) {
        return (
            <span>
                Failed <RelativeTime value={job.failedAt} now={now} />
            </span>
        );
    }

    if (job.completedAt) {
        return (
            <span>
                Completed <RelativeTime value={job.completedAt} now={now} />
            </span>
        );
    }

    if (job.scheduledAt) {
        return (
            <span>
                Scheduled <RelativeTime value={job.scheduledAt} now={now} />
            </span>
        );
    }

    return (
        <span>
            Queued <RelativeTime value={job.createdAt} now={now} />
        </span>
    );
}
