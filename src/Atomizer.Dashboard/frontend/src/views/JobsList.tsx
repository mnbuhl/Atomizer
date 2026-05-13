import type { KeyboardEvent } from 'react';
import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useJobs, useJobStatusCounts, type JobFilters } from '../api/hooks';
import { routePrefix, jobsRefreshMs } from '../config';
import type { JobDto } from '../api/types';
import {
    cx,
    EmptyState,
    formatNumber,
    MetricCard,
    MetricCardSkeleton,
    PageHeader,
    Panel,
    RelativeTime,
    TableSkeleton,
    StatusPill,
    ui,
} from '../components/DashboardUi';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
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
    const [queueSearch, setQueueSearch] = useState(filters.queue ?? '');
    const [payloadSearch, setPayloadSearch] = useState(filters.payload ?? '');
    const debouncedQueueSearch = useDebouncedValue(queueSearch, 300);
    const debouncedPayloadSearch = useDebouncedValue(payloadSearch, 300);
    const { data, isLoading, error, refetch, dataUpdatedAt, isFetching } = useJobs(filters);
    const { counts, isLoading: countsLoading } = useJobStatusCounts(filters);

    const take = filters.take ?? PAGE_SIZE;
    const currentPage = Math.floor((filters.skip ?? 0) / take) + 1;
    const visibleCount = data?.items.length ?? 0;
    const pageWindow = getJobPageWindow(filters.skip ?? 0, visibleCount, data?.totalCount ?? 0);

    const setPage = (skip: number) => setFilters(f => ({ ...f, skip }));
    const openJob = (jobId: string) => navigate(`${routePrefix}/jobs/${jobId}`);

    useEffect(() => {
        setFilters(f => ({
            ...f,
            skip: 0,
            queue: debouncedQueueSearch || undefined,
            payload: debouncedPayloadSearch || undefined,
        }));
    }, [debouncedPayloadSearch, debouncedQueueSearch]);

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
        setQueueSearch('');
        setPayloadSearch('');
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
                        <div className={ui.toolbarPill}>
                            Updated <RelativeTime value={dataUpdatedAt || null} now={now} />
                        </div>
                        <button
                            onClick={() => refetch()}
                            className={ui.primaryButton}
                            disabled={isFetching}
                        >
                            {isFetching ? 'Refreshing…' : 'Refresh'}
                        </button>
                    </>
                }
            />

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                {isLoading ? (
                    <MetricCardSkeleton />
                ) : (
                    <MetricCard
                        label="Matching jobs"
                        value={formatNumber(data?.totalCount ?? 0, true)}
                        helper={`${formatNumber(visibleCount)} visible on page ${currentPage}`}
                        tone="blue"
                    />
                )}
                {countsLoading ? (
                    <MetricCardSkeleton />
                ) : (
                    <MetricCard
                        label="Pending"
                        value={formatNumber(counts.Pending)}
                        helper="Matching queued backlog"
                        tone="amber"
                    />
                )}
                {countsLoading ? (
                    <MetricCardSkeleton />
                ) : (
                    <MetricCard
                        label="Processing"
                        value={formatNumber(counts.Processing)}
                        helper="Matching leased work"
                        tone="cyan"
                    />
                )}
                {countsLoading ? (
                    <MetricCardSkeleton />
                ) : (
                    <MetricCard
                        label="Failed"
                        value={formatNumber(counts.Failed)}
                        helper="Matching jobs needing attention"
                        tone="red"
                    />
                )}
            </div>

            <Panel>
                <div className={ui.panelHeadingAccent}>
                    <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
                        <div>
                            <h2 className={ui.sectionTitle}>Filters</h2>
                            <p className={ui.sectionDescription}>
                                Narrow the table without exposing noisy full date values.
                            </p>
                        </div>

                        <div className="flex flex-wrap gap-2">
                            {TIME_FILTERS.map(option => (
                                <button
                                    key={option.key}
                                    onClick={() => applyTimeFilter(option.key)}
                                    className={cx(
                                        timeFilter === option.key ? ui.filterChipActive : ui.filterChip,
                                    )}
                                >
                                    {option.label}
                                </button>
                            ))}
                        </div>
                    </div>

                    <div className="mt-5 grid gap-3 lg:grid-cols-[1fr_1fr_auto]">
                        <label className="block">
                            <span className={ui.fieldLabel}>Queue</span>
                            <input
                                type="text"
                                placeholder="default"
                                className={ui.input}
                                value={queueSearch}
                                onChange={e => setQueueSearch(e.target.value)}
                            />
                        </label>

                        <label className="block">
                            <span className={ui.fieldLabel}>Payload type</span>
                            <input
                                type="text"
                                placeholder="Namespace.JobPayload"
                                className={ui.input}
                                value={payloadSearch}
                                onChange={e => setPayloadSearch(e.target.value)}
                            />
                        </label>

                        <button
                            onClick={clearFilters}
                            className={cx(ui.secondaryButton, 'self-end px-4 py-3')}
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
                                    className={active ? ui.filterChipActive : ui.filterChip}
                                >
                                    {status}
                                </button>
                            );
                        })}
                    </div>
                </div>

                {isLoading && <TableSkeleton columns={6} rows={6} />}
                {error && <div className={ui.error}>Error loading jobs.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[900px] text-left text-sm">
                                <thead>
                                    <tr className={ui.tableHeadRow}>
                                        <th className="px-5 py-4 font-semibold">Job</th>
                                        <th className="px-5 py-4 font-semibold">Status</th>
                                        <th className="px-5 py-4 font-semibold">Queue / Partition</th>
                                        <th className="px-5 py-4 font-semibold">Attempts</th>
                                        <th className="px-5 py-4 font-semibold">Timing</th>
                                        <th className="px-5 py-4 font-semibold">Payload</th>
                                    </tr>
                                </thead>
                                <tbody className={ui.tableBody}>
                                    {data.items.map((job: JobDto) => (
                                        <tr
                                            key={job.id}
                                            role="link"
                                            tabIndex={0}
                                            aria-label={`Open job ${job.id}`}
                                            onClick={() => openJob(job.id)}
                                            onKeyDown={event => handleRowKeyDown(event, job.id)}
                                            className={ui.interactiveTableRow}
                                        >
                                            <td className="px-5 py-4">
                                                <div className={cx(ui.strong, 'font-mono text-sm font-semibold')}>
                                                    {job.id.slice(0, 8)}…
                                                </div>
                                                <div className={cx(ui.soft, 'mt-1 text-xs')}>
                                                    Created <RelativeTime value={job.createdAt} now={now} />
                                                </div>
                                            </td>
                                            <td className="px-5 py-4">
                                                <StatusPill status={job.status} />
                                            </td>
                                            <td className="px-5 py-4">
                                                <span className={ui.softChip}>
                                                    {job.queueKey}
                                                </span>
                                                <div className={cx(ui.muted, 'mt-2 flex items-center gap-2 text-xs')}>
                                                    <span className="max-w-36 truncate">
                                                        {job.partitionKey ? `Partition ${job.partitionKey}` : 'Unpartitioned'}
                                                    </span>
                                                    {job.sequenceNumber !== null && (
                                                        <span className={ui.softChipPurple}>
                                                            #{formatNumber(job.sequenceNumber)}
                                                        </span>
                                                    )}
                                                </div>
                                            </td>
                                            <td className={cx(ui.defaultText, 'px-5 py-4')}>{formatNumber(job.attempts)}</td>
                                            <td className={cx(ui.defaultText, 'px-5 py-4')}>
                                                <JobTiming job={job} now={now} />
                                            </td>
                                            <td className="max-w-xs px-5 py-4">
                                                <div className={cx(ui.defaultText, 'truncate')}>{job.payloadTypeName}</div>
                                                <div className={ui.rowAction}>
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

                        <div className={ui.footer}>
                            <span>
                                Showing {pageWindow.label} of {formatNumber(data.totalCount)} matching jobs · page size{' '}
                                {take} · auto-refresh{' '}
                                {jobsRefreshMs / 1000}s
                            </span>
                            <div className="flex gap-2">
                                <button
                                    disabled={filters.skip === 0}
                                    onClick={() => setPage(Math.max(0, (filters.skip ?? 0) - take))}
                                    className={ui.smallButton}
                                >
                                    ← Prev
                                </button>
                                <button
                                    disabled={(filters.skip ?? 0) + take >= data.totalCount}
                                    onClick={() => setPage((filters.skip ?? 0) + take)}
                                    className={ui.smallButton}
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
