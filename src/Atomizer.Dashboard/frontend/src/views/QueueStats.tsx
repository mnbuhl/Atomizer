import type { KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueueStats } from '../api/hooks';
import { statsRefreshMs, routePrefix } from '../config';
import {
    cx,
    EmptyState,
    formatNumber,
    MetricCard,
    MetricCardSkeleton,
    PageHeader,
    Panel,
    TableSkeleton,
    StatusPill,
    ui,
} from '../components/DashboardUi';

export default function QueueStats() {
    const { data, isLoading, error } = useQueueStats();
    const navigate = useNavigate();
    const queues = data?.queues ?? [];
    const totals = queues.reduce(
        (acc, queue) => ({
            pending: acc.pending + queue.pending,
            processing: acc.processing + queue.processing,
            completed: acc.completed + queue.completed,
            failed: acc.failed + queue.failed,
            cancelled: acc.cancelled + queue.cancelled,
        }),
        { pending: 0, processing: 0, completed: 0, failed: 0, cancelled: 0 },
    );

    const openQueueJobs = (queueKey: string) =>
        navigate(`${routePrefix}/jobs?queue=${encodeURIComponent(queueKey)}`);

    const handleRowKeyDown = (event: KeyboardEvent<HTMLTableRowElement>, queueKey: string) => {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            openQueueJobs(queueKey);
        }
    };

    return (
        <div className="space-y-6">
            <PageHeader
                eyebrow="Queue health"
                title="Queues"
                description="Compare backlog, active work, and failures across queues. Open a row to inspect jobs for that queue."
                actions={
                    <div className={ui.toolbarPill}>
                        Refreshes every {statsRefreshMs / 1000}s
                    </div>
                }
            />

            {isLoading ? (
                <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                    <MetricCardSkeleton />
                    <MetricCardSkeleton />
                    <MetricCardSkeleton />
                    <MetricCardSkeleton />
                </div>
            ) : (
                <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                    <MetricCard label="Queues" value={formatNumber(queues.length)} helper="Configured queues with data" tone="blue" />
                    <MetricCard label="Pending" value={formatNumber(totals.pending)} helper="Total backlog" tone="amber" />
                    <MetricCard label="Processing" value={formatNumber(totals.processing)} helper="Active leases" tone="cyan" />
                    <MetricCard label="Failed" value={formatNumber(totals.failed)} helper="Completed with failure" tone="red" />
                </div>
            )}

            <Panel>
                <div className={ui.panelHeading}>
                    <h2 className={ui.sectionTitle}>Queue workload table</h2>
                    <p className={ui.sectionDescription}>Counts are grouped by queue and job status.</p>
                </div>

                {isLoading && <TableSkeleton columns={8} rows={5} />}
                {error && <div className={ui.error}>Error loading queue stats.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[780px] text-left text-sm">
                                <thead>
                                    <tr className={ui.tableHeadRow}>
                                        <th className="px-5 py-4 font-semibold">Queue</th>
                                        <th className="px-5 py-4 font-semibold">State</th>
                                        <th className="px-5 py-4 font-semibold">Pending</th>
                                        <th className="px-5 py-4 font-semibold">Processing</th>
                                        <th className="px-5 py-4 font-semibold">Completed</th>
                                        <th className="px-5 py-4 font-semibold">Failed</th>
                                        <th className="px-5 py-4 font-semibold">Cancelled</th>
                                        <th className="px-5 py-4 font-semibold">Work mix</th>
                                    </tr>
                                </thead>
                                <tbody className={ui.tableBody}>
                                    {queues.map(queue => {
                                        const total =
                                            queue.pending +
                                            queue.processing +
                                            queue.completed +
                                            queue.failed +
                                            queue.cancelled;
                                        const hasBacklog = queue.pending > 0;
                                        const hasFailures = queue.failed > 0;
                                        const state = hasFailures ? 'Attention' : hasBacklog ? 'Backlog' : queue.processing > 0 ? 'Active' : 'Idle';

                                        return (
                                            <tr
                                                key={queue.queueKey}
                                                role="link"
                                                tabIndex={0}
                                                aria-label={`Open jobs for queue ${queue.queueKey}`}
                                                onClick={() => openQueueJobs(queue.queueKey)}
                                                onKeyDown={event => handleRowKeyDown(event, queue.queueKey)}
                                                className={ui.interactiveTableRow}
                                            >
                                                <td className="px-5 py-4">
                                                    <div className={cx(ui.strong, 'font-semibold')}>{queue.queueKey}</div>
                                                    <div className={ui.rowAction}>
                                                        View jobs →
                                                    </div>
                                                </td>
                                                <td className="px-5 py-4">
                                                    <StatusPill
                                                        status={state}
                                                        tone={hasFailures ? 'red' : hasBacklog ? 'amber' : queue.processing > 0 ? 'green' : 'slate'}
                                                    />
                                                </td>
                                                <CountCell value={queue.pending} tone="count-amber" />
                                                <CountCell value={queue.processing} tone="count-sky" />
                                                <CountCell value={queue.completed} tone="count-emerald" />
                                                <CountCell value={queue.failed} tone="count-rose" />
                                                <CountCell value={queue.cancelled} tone="count-orange" />
                                                <td className="px-5 py-4">
                                                    <div className="flex h-2 w-40 overflow-hidden rounded-full bg-[var(--chip)]">
                                                        <WorkMixSegment value={queue.pending} total={total} className="bg-amber-400" />
                                                        <WorkMixSegment value={queue.processing} total={total} className="bg-sky-400" />
                                                        <WorkMixSegment value={queue.failed} total={total} className="bg-rose-400" />
                                                        <WorkMixSegment value={queue.cancelled} total={total} className="bg-orange-400" />
                                                        <WorkMixSegment value={queue.completed} total={total} className="bg-emerald-400" />
                                                    </div>
                                                    <div className={cx(ui.soft, 'mt-1 text-xs')}>{formatNumber(total)} total</div>
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>

                        {queues.length === 0 && (
                            <EmptyState
                                title="No queue data available"
                                description="Queue statistics will appear once jobs have been recorded."
                            />
                        )}
                    </>
                )}
            </Panel>
        </div>
    );
}

function CountCell({ value, tone }: { value: number; tone: string }) {
    return <td className={cx('px-5 py-4 font-semibold tabular-nums', value > 0 ? tone : ui.soft)}>{formatNumber(value)}</td>;
}

function WorkMixSegment({ value, total, className }: { value: number; total: number; className: string }) {
    if (value === 0 || total === 0) {
        return null;
    }

    return <div className={className} style={{ width: `${Math.max(4, (value / total) * 100)}%` }} />;
}
