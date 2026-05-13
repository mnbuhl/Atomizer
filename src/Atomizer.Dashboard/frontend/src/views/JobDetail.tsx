import { Link, useParams } from 'react-router-dom';
import { useJob } from '../api/hooks';
import { routePrefix } from '../config';
import { MetricCard, PageHeader, Panel, RelativeTime, StatusPill } from '../components/DashboardUi';
import { useNow } from '../hooks/useNow';

export default function JobDetail() {
    const { id } = useParams<{ id: string }>();
    const now = useNow(15_000);
    const { data: job, isLoading, error } = useJob(id!);

    if (isLoading) return <div className="rounded-3xl bg-white/90 p-8 text-sm text-slate-500">Loading job…</div>;
    if (error || !job)
        return (
            <Panel className="p-8">
                <p className="text-sm font-medium text-rose-600">Job not found.</p>
                <Link to={`${routePrefix}/jobs`} className="mt-3 inline-block text-sm font-semibold text-sky-600 hover:text-sky-700">
                    ← Back to jobs
                </Link>
            </Panel>
        );

    let formattedPayload = job.payload ?? '';
    try {
        if (job.payload) formattedPayload = JSON.stringify(JSON.parse(job.payload), null, 2);
    } catch {
        /* not valid JSON, show raw */
    }

    const finalTimestamp = job.failedAt ?? job.completedAt ?? job.scheduledAt ?? job.createdAt;
    const finalLabel = job.failedAt ? 'Failed' : job.completedAt ? 'Completed' : job.scheduledAt ? 'Scheduled' : 'Created';

    return (
        <div className="space-y-6">
            <PageHeader
                eyebrow="Job detail"
                title={job.id.slice(0, 13)}
                description={job.payloadTypeName}
                actions={
                    <>
                        <StatusPill status={job.status} className="px-3 py-1.5 text-sm" />
                        <Link
                            to={`${routePrefix}/jobs`}
                            className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-slate-600 transition hover:border-slate-300 hover:text-slate-950"
                        >
                            ← Jobs
                        </Link>
                    </>
                }
            />

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                <MetricCard label="Queue" value={job.queueKey} helper="Assigned queue" tone="blue" />
                <MetricCard label="Attempts" value={job.attempts} helper="Execution attempts" tone={job.attempts > 0 ? 'amber' : 'slate'} />
                <MetricCard
                    label="Partition"
                    value={job.partitionKey ?? 'None'}
                    helper={job.sequenceNumber === null ? 'No FIFO partition' : `FIFO sequence #${job.sequenceNumber}`}
                    tone={job.partitionKey ? 'purple' : 'slate'}
                />
                <MetricCard
                    label="Created"
                    value={<RelativeTime value={job.createdAt} now={now} />}
                    helper="Relative creation time"
                    tone="cyan"
                />
                <MetricCard
                    label={finalLabel}
                    value={<RelativeTime value={finalTimestamp} now={now} />}
                    helper="Most relevant transition"
                    tone={job.status === 'Failed' ? 'red' : job.status === 'Completed' ? 'green' : 'purple'}
                />
            </div>

            <Panel>
                <div className="border-b border-slate-200/80 bg-gradient-to-r from-white via-cyan-50/70 to-violet-50/60 p-5">
                    <h2 className="text-base font-semibold text-slate-950">Timeline</h2>
                    <p className="mt-1 text-sm text-slate-500">Relative job lifecycle timestamps.</p>
                </div>
                <div className="grid gap-3 p-5 md:grid-cols-2 xl:grid-cols-4">
                    <TimelineItem label="Created" value={job.createdAt} now={now} />
                    <TimelineItem label="Scheduled" value={job.scheduledAt} now={now} empty="Not scheduled" />
                    <TimelineItem label="Completed" value={job.completedAt} now={now} empty="Not completed" />
                    <TimelineItem label="Failed" value={job.failedAt} now={now} empty="No failure recorded" />
                </div>
            </Panel>

            {formattedPayload && (
                <Panel>
                    <div className="border-b border-slate-200/80 bg-gradient-to-r from-white via-slate-50 to-sky-50/70 p-5">
                        <h2 className="text-base font-semibold text-slate-950">Payload</h2>
                        <p className="mt-1 text-sm text-slate-500">Formatted job payload for quick inspection.</p>
                    </div>
                    <pre className="max-h-[34rem] overflow-auto bg-slate-950 p-5 text-xs leading-6 text-slate-100">
                        {formattedPayload}
                    </pre>
                </Panel>
            )}

            {job.errors.length > 0 && (
                <Panel>
                    <div className="border-b border-rose-100 bg-rose-50/80 p-5">
                        <h2 className="text-base font-semibold text-rose-950">Error history</h2>
                        <p className="mt-1 text-sm text-rose-700">
                            {job.errors.length} recorded failure{job.errors.length === 1 ? '' : 's'} across attempts.
                        </p>
                    </div>
                    <div className="space-y-3 p-5">
                        {job.errors.map(err => (
                            <details
                                key={err.attempt}
                                className="group overflow-hidden rounded-2xl border border-rose-100 bg-white shadow-sm shadow-rose-950/5"
                            >
                                <summary className="flex cursor-pointer list-none flex-col gap-2 px-4 py-3 text-sm font-semibold text-rose-900 transition hover:bg-rose-50 sm:flex-row sm:items-center sm:justify-between">
                                    <span>
                                        Attempt {err.attempt} · {err.exceptionType}
                                    </span>
                                    <span className="text-xs font-medium text-rose-500">
                                        <RelativeTime value={err.occurredAt} now={now} />
                                    </span>
                                </summary>
                                <div className="space-y-3 border-t border-rose-100 px-4 pb-4 pt-3 text-sm">
                                    <p className="text-rose-700">{err.message}</p>
                                    {err.runtimeIdentity && (
                                        <p className="text-xs font-medium text-slate-500">Runtime: {err.runtimeIdentity}</p>
                                    )}
                                    {err.stackTrace && (
                                        <pre className="max-h-80 overflow-auto rounded-2xl border border-slate-200 bg-slate-950 p-4 text-xs leading-5 text-slate-100">
                                            {err.stackTrace}
                                        </pre>
                                    )}
                                </div>
                            </details>
                        ))}
                    </div>
                </Panel>
            )}
        </div>
    );
}

function TimelineItem({
    label,
    value,
    now,
    empty = '—',
}: {
    label: string;
    value: string | null;
    now: number;
    empty?: string;
}) {
    return (
        <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">{label}</p>
            <p className="mt-2 text-sm font-semibold text-slate-900">
                <RelativeTime value={value} now={now} fallback={empty} />
            </p>
        </div>
    );
}
