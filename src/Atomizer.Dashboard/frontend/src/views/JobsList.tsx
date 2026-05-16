import type { FormEvent, KeyboardEvent, MouseEvent } from 'react';
import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { useJobs, useJobTypes, type JobFilters } from '../api/hooks';
import { routePrefix, jobsRefreshMs } from '../config';
import type { JobDto, JobTypeOption } from '../api/types';
import {
    ConfirmationDialog,
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

const STATUS_OPTIONS = ['Pending', 'Processing', 'Completed', 'Failed', 'Cancelled'] as const;
const PAGE_SIZE = 20;
const QueueKeyDefault = 'default';
const TIME_FILTERS = [
    { key: 'all', label: 'All time', getFrom: () => undefined },
    { key: '15m', label: '15m', getFrom: () => new Date(Date.now() - 15 * 60 * 1000).toISOString() },
    { key: '1h', label: '1h', getFrom: () => new Date(Date.now() - 60 * 60 * 1000).toISOString() },
    { key: '24h', label: '24h', getFrom: () => new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString() },
    { key: '7d', label: '7d', getFrom: () => new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString() },
] as const;

type JobsConfirmation =
    | { action: 'trigger'; payloadTypeId: string; payloadTypeName: string; queueKey: string; payload: string }
    | { action: 'retry'; job: JobDto }
    | { action: 'cancel'; job: JobDto };

export default function JobsList() {
    const [searchParams] = useSearchParams();
    const navigate = useNavigate();
    const queryClient = useQueryClient();
    const now = useNow(1_000);
    const [isTriggerOpen, setIsTriggerOpen] = useState(false);
    const [triggerPayloadTypeId, setTriggerPayloadTypeId] = useState('');
    const [triggerQueueKey, setTriggerQueueKey] = useState(QueueKeyDefault);
    const [triggerPayload, setTriggerPayload] = useState('');
    const [confirmation, setConfirmation] = useState<JobsConfirmation | null>(null);
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
    const { data: jobTypes = [], isLoading: isLoadingJobTypes, error: jobTypesError } = useJobTypes(isTriggerOpen);
    const counts = data?.statusCounts ?? { pending: 0, processing: 0, completed: 0, failed: 0, cancelled: 0 };
    const retryMutation = useMutation({
        mutationFn: api.retryJob,
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['jobs'] }),
    });
    const cancelMutation = useMutation({
        mutationFn: api.cancelJob,
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['jobs'] }),
    });
    const triggerMutation = useMutation({
        mutationFn: api.triggerJob,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: ['jobs'] });
            setIsTriggerOpen(false);
        },
    });

    const take = filters.take ?? PAGE_SIZE;
    const currentPage = Math.floor((filters.skip ?? 0) / take) + 1;
    const visibleCount = data?.items.length ?? 0;
    const pageWindow = getJobPageWindow(filters.skip ?? 0, visibleCount, data?.totalCount ?? 0);

    const setPage = (skip: number) => setFilters(f => ({ ...f, skip }));
    const openJob = (jobId: string) => navigate(`${routePrefix}/jobs/${jobId}`);
    const selectTriggerPayloadType = (option: JobTypeOption) => {
        setTriggerPayloadTypeId(option.id);
        setTriggerPayload(option.examplePayload);
    };

    useEffect(() => {
        setFilters(f => ({
            ...f,
            skip: 0,
            queue: debouncedQueueSearch || undefined,
            payload: debouncedPayloadSearch || undefined,
        }));
    }, [debouncedPayloadSearch, debouncedQueueSearch]);

    useEffect(() => {
        if (isTriggerOpen && !triggerPayloadTypeId && jobTypes.length > 0) {
            selectTriggerPayloadType(jobTypes[0]);
        }
    }, [isTriggerOpen, jobTypes, triggerPayloadTypeId]);

    const handleRowKeyDown = (event: KeyboardEvent<HTMLTableRowElement>, jobId: string) => {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            openJob(jobId);
        }
    };

    const stopRowAction = (event: MouseEvent<HTMLButtonElement>) => event.stopPropagation();
    const stopRowKeyboardAction = (event: KeyboardEvent<HTMLButtonElement>) => event.stopPropagation();

    const submitTriggerJob = (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        const selectedJobType = jobTypes.find(option => option.id === triggerPayloadTypeId);
        setConfirmation({
            action: 'trigger',
            payloadTypeId: triggerPayloadTypeId,
            payloadTypeName: selectedJobType?.payloadTypeName ?? 'selected payload type',
            queueKey: triggerQueueKey,
            payload: triggerPayload,
        });
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
                        <button
                            onClick={() => setIsTriggerOpen(open => !open)}
                            className={ui.secondaryButton}
                            aria-expanded={isTriggerOpen}
                        >
                            Trigger job
                        </button>
                    </>
                }
            />

            {isTriggerOpen && (
                <Panel>
                    <form onSubmit={submitTriggerJob} className="grid gap-4 p-5 xl:grid-cols-[minmax(0,1fr)_12rem]">
                        <label className="block">
                            <span className={ui.fieldLabel}>Payload type</span>
                            <select
                                className={ui.input}
                                value={triggerPayloadTypeId}
                                disabled={isLoadingJobTypes || jobTypes.length === 0}
                                onChange={event => {
                                    const selected = jobTypes.find(option => option.id === event.target.value);
                                    if (selected) {
                                        selectTriggerPayloadType(selected);
                                    }
                                }}
                            >
                                {isLoadingJobTypes && <option value="">Loading payload types</option>}
                                {!isLoadingJobTypes && jobTypes.length === 0 && (
                                    <option value="">No registered payload types</option>
                                )}
                                {jobTypes.map(option => (
                                    <option key={option.id} value={option.id}>
                                        {option.payloadTypeFullName}
                                    </option>
                                ))}
                            </select>
                        </label>

                        <label className="block">
                            <span className={ui.fieldLabel}>Queue</span>
                            <input
                                type="text"
                                className={ui.input}
                                value={triggerQueueKey}
                                onChange={event => setTriggerQueueKey(event.target.value)}
                            />
                        </label>

                        <label className="block xl:col-span-2">
                            <span className={ui.fieldLabel}>Payload JSON</span>
                            <textarea
                                className="input-field mt-2 h-44 w-full rounded-2xl border px-4 py-3 font-mono text-sm outline-none transition"
                                value={triggerPayload}
                                disabled={isLoadingJobTypes || jobTypes.length === 0}
                                onChange={event => setTriggerPayload(event.target.value)}
                            />
                        </label>

                        {jobTypesError && (
                            <div className="danger-text text-sm font-medium xl:col-span-2">
                                Failed to load payload types.
                            </div>
                        )}

                        {triggerMutation.error && (
                            <div className="danger-text text-sm font-medium xl:col-span-2">
                                {triggerMutation.error.message}
                            </div>
                        )}

                        <div className="flex flex-wrap gap-2 xl:col-span-2">
                            <button
                                type="submit"
                                className={ui.primaryButton}
                                disabled={triggerMutation.isPending || isLoadingJobTypes || !triggerPayloadTypeId}
                            >
                                {triggerMutation.isPending ? 'Triggering…' : 'Trigger'}
                            </button>
                            <button
                                type="button"
                                className={ui.secondaryButton}
                                onClick={() => setIsTriggerOpen(false)}
                            >
                                Cancel
                            </button>
                        </div>
                    </form>
                </Panel>
            )}

            <ConfirmationDialog
                open={confirmation !== null}
                title={getJobsConfirmationTitle(confirmation)}
                description={getJobsConfirmationDescription(confirmation)}
                confirmLabel={getJobsConfirmationLabel(confirmation)}
                confirmTone={confirmation?.action === 'cancel' ? 'danger' : 'default'}
                onCancel={() => setConfirmation(null)}
                onConfirm={() => {
                    if (!confirmation) {
                        return;
                    }

                    if (confirmation.action === 'trigger') {
                        triggerMutation.mutate({
                            payloadTypeId: confirmation.payloadTypeId,
                            queueKey: confirmation.queueKey,
                            payload: confirmation.payload,
                        });
                    } else if (confirmation.action === 'retry') {
                        retryMutation.mutate(confirmation.job.id);
                    } else {
                        cancelMutation.mutate(confirmation.job.id);
                    }

                    setConfirmation(null);
                }}
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
                {isLoading ? (
                    <MetricCardSkeleton />
                ) : (
                    <MetricCard
                        label="Pending"
                        value={formatNumber(counts.pending)}
                        helper="Matching queued backlog"
                        tone="amber"
                    />
                )}
                {isLoading ? (
                    <MetricCardSkeleton />
                ) : (
                    <MetricCard
                        label="Processing"
                        value={formatNumber(counts.processing)}
                        helper="Matching leased work"
                        tone="cyan"
                    />
                )}
                {isLoading ? (
                    <MetricCardSkeleton />
                ) : (
                    <MetricCard
                        label="Failed"
                        value={formatNumber(counts.failed)}
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

                {isLoading && <TableSkeleton columns={7} rows={6} />}
                {error && <div className={ui.error}>Error loading jobs.</div>}
                {(retryMutation.error || cancelMutation.error) && (
                    <div className="danger-text border-t border-[var(--border-soft)] px-5 py-3 text-sm font-medium">
                        {(retryMutation.error ?? cancelMutation.error)?.message}
                    </div>
                )}
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
                                        <th className="px-5 py-4 font-semibold">Actions</th>
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
                                            <td className="px-5 py-4">
                                                <div className="flex flex-wrap gap-2">
                                                    {job.status === 'Failed' && (
                                                        <button
                                                            type="button"
                                                            className={ui.smallButton}
                                                            disabled={retryMutation.isPending}
                                                            onKeyDown={stopRowKeyboardAction}
                                                            onClick={event => {
                                                                stopRowAction(event);
                                                                setConfirmation({ action: 'retry', job });
                                                            }}
                                                        >
                                                            Retry
                                                        </button>
                                                    )}
                                                    {job.status === 'Pending' && (
                                                        <button
                                                            type="button"
                                                            className={ui.smallButton}
                                                            disabled={cancelMutation.isPending}
                                                            onKeyDown={stopRowKeyboardAction}
                                                            onClick={event => {
                                                                stopRowAction(event);
                                                                setConfirmation({ action: 'cancel', job });
                                                            }}
                                                        >
                                                            Cancel
                                                        </button>
                                                    )}
                                                    {job.status !== 'Failed' && job.status !== 'Pending' && (
                                                        <span className={cx(ui.muted, 'text-xs')}>No action</span>
                                                    )}
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

function getJobsConfirmationTitle(confirmation: JobsConfirmation | null): string {
    if (confirmation?.action === 'trigger') {
        return 'Trigger job?';
    }

    if (confirmation?.action === 'retry') {
        return 'Retry job?';
    }

    return 'Cancel job?';
}

function getJobsConfirmationDescription(confirmation: JobsConfirmation | null): string {
    if (confirmation?.action === 'trigger') {
        return `Trigger ${confirmation.payloadTypeName} on queue ${confirmation.queueKey}.`;
    }

    if (confirmation?.action === 'retry') {
        return `Retry failed job ${confirmation.job.id.slice(0, 8)} by enqueueing a replacement job.`;
    }

    if (confirmation?.action === 'cancel') {
        return `Cancel pending job ${confirmation.job.id.slice(0, 8)}. This moves it out of the queue.`;
    }

    return '';
}

function getJobsConfirmationLabel(confirmation: JobsConfirmation | null): string {
    if (confirmation?.action === 'trigger') {
        return 'Trigger job';
    }

    if (confirmation?.action === 'retry') {
        return 'Retry job';
    }

    return 'Cancel job';
}

function JobTiming({ job, now }: { job: JobDto; now: number }) {
    if (job.status === 'Cancelled') {
        return (
            <span>
                Cancelled <RelativeTime value={job.updatedAt} now={now} />
            </span>
        );
    }

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
