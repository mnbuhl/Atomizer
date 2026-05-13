import type { KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueueStats } from '../api/hooks';
import { statsRefreshMs, routePrefix } from '../config';
import {
    cx,
    EmptyState,
    formatNumber,
    MetricCard,
    PageHeader,
    Panel,
    StatusPill,
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
        }),
        { pending: 0, processing: 0, completed: 0, failed: 0 },
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
                    <div className="rounded-2xl bg-slate-100 px-3 py-2 text-xs font-medium text-slate-600">
                        Refresh every {statsRefreshMs / 1000}s
                    </div>
                }
            />

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                <MetricCard label="Queues" value={formatNumber(queues.length)} helper="Configured queues with data" tone="blue" />
                <MetricCard label="Pending" value={formatNumber(totals.pending)} helper="Total backlog" tone="amber" />
                <MetricCard label="Processing" value={formatNumber(totals.processing)} helper="Active leases" tone="cyan" />
                <MetricCard label="Failed" value={formatNumber(totals.failed)} helper="Completed with failure" tone="red" />
            </div>

            <Panel>
                <div className="border-b border-slate-200/80 p-5">
                    <h2 className="text-base font-semibold text-slate-950">Queue workload table</h2>
                    <p className="mt-1 text-sm text-slate-500">Counts are grouped by queue and job status.</p>
                </div>

                {isLoading && <div className="p-8 text-sm text-slate-500">Loading queue stats…</div>}
                {error && <div className="p-8 text-sm font-medium text-rose-600">Error loading queue stats.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[780px] text-left text-sm">
                                <thead>
                                    <tr className="border-b border-slate-200/80 bg-slate-50/80 text-xs uppercase tracking-[0.16em] text-slate-400">
                                        <th className="px-5 py-4 font-semibold">Queue</th>
                                        <th className="px-5 py-4 font-semibold">State</th>
                                        <th className="px-5 py-4 font-semibold">Pending</th>
                                        <th className="px-5 py-4 font-semibold">Processing</th>
                                        <th className="px-5 py-4 font-semibold">Completed</th>
                                        <th className="px-5 py-4 font-semibold">Failed</th>
                                        <th className="px-5 py-4 font-semibold">Work mix</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100">
                                    {queues.map(queue => {
                                        const total = queue.pending + queue.processing + queue.completed + queue.failed;
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
                                                className="group cursor-pointer bg-white/70 transition hover:bg-sky-50/70 focus:bg-sky-50/70 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-sky-300"
                                            >
                                                <td className="px-5 py-4">
                                                    <div className="font-semibold text-slate-950">{queue.queueKey}</div>
                                                    <div className="mt-1 text-xs font-semibold text-sky-600 opacity-0 transition group-hover:opacity-100 group-focus:opacity-100">
                                                        View jobs →
                                                    </div>
                                                </td>
                                                <td className="px-5 py-4">
                                                    <StatusPill
                                                        status={state}
                                                        tone={hasFailures ? 'red' : hasBacklog ? 'amber' : queue.processing > 0 ? 'green' : 'slate'}
                                                    />
                                                </td>
                                                <CountCell value={queue.pending} tone="text-amber-600" />
                                                <CountCell value={queue.processing} tone="text-sky-600" />
                                                <CountCell value={queue.completed} tone="text-emerald-600" />
                                                <CountCell value={queue.failed} tone="text-rose-600" />
                                                <td className="px-5 py-4">
                                                    <div className="flex h-2 w-40 overflow-hidden rounded-full bg-slate-100">
                                                        <WorkMixSegment value={queue.pending} total={total} className="bg-amber-400" />
                                                        <WorkMixSegment value={queue.processing} total={total} className="bg-sky-400" />
                                                        <WorkMixSegment value={queue.failed} total={total} className="bg-rose-400" />
                                                        <WorkMixSegment value={queue.completed} total={total} className="bg-emerald-400" />
                                                    </div>
                                                    <div className="mt-1 text-xs text-slate-400">{formatNumber(total)} total</div>
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
    return <td className={cx('px-5 py-4 font-semibold tabular-nums', value > 0 ? tone : 'text-slate-400')}>{formatNumber(value)}</td>;
}

function WorkMixSegment({ value, total, className }: { value: number; total: number; className: string }) {
    if (value === 0 || total === 0) {
        return null;
    }

    return <div className={className} style={{ width: `${Math.max(4, (value / total) * 100)}%` }} />;
}
