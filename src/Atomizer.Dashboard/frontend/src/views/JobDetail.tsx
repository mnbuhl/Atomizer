import { Link, useParams } from 'react-router-dom';
import { useJob } from '../api/hooks';
import { routePrefix } from '../config';
import {
    cx,
    MetricCard,
    MetricCardSkeleton,
    PageHeader,
    Panel,
    RelativeTime,
    SkeletonBlock,
    StatusPill,
    ui,
} from '../components/DashboardUi';
import { useNow } from '../hooks/useNow';

export default function JobDetail() {
    const { id } = useParams<{ id: string }>();
    const now = useNow(15_000);
    const { data: job, isLoading, error } = useJob(id!);

    if (isLoading) return <JobDetailSkeleton />;
    if (error || !job)
        return (
            <Panel className="p-8">
                <p className="danger-text text-sm font-medium">Job not found.</p>
                <Link to={`${routePrefix}/jobs`} className="row-action mt-3 inline-block text-sm font-semibold opacity-100">
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
                            className={ui.secondaryButton}
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
                <div className={ui.panelHeadingAccent}>
                    <h2 className={ui.sectionTitle}>Timeline</h2>
                    <p className={ui.sectionDescription}>Relative job lifecycle timestamps.</p>
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
                    <div className={ui.panelHeadingAccent}>
                        <h2 className={ui.sectionTitle}>Payload</h2>
                        <p className={ui.sectionDescription}>Formatted job payload for quick inspection.</p>
                    </div>
                    <pre className="code-block max-h-[34rem] overflow-auto p-5 text-xs leading-6">
                        {formattedPayload}
                    </pre>
                </Panel>
            )}

            {job.errors.length > 0 && (
                <Panel>
                    <div className="danger-heading border-b p-5">
                        <h2 className="danger-title text-base font-semibold">Error history</h2>
                        <p className="danger-text mt-1 text-sm">
                            {job.errors.length} recorded failure{job.errors.length === 1 ? '' : 's'} across attempts.
                        </p>
                    </div>
                    <div className="space-y-3 p-5">
                        {job.errors.map(err => (
                            <details
                                key={err.attempt}
                                className="danger-card group overflow-hidden rounded-2xl border"
                            >
                                <summary className="danger-title flex cursor-pointer list-none flex-col gap-2 px-4 py-3 text-sm font-semibold transition sm:flex-row sm:items-center sm:justify-between">
                                    <span>
                                        Attempt {err.attempt} · {err.exceptionType}
                                    </span>
                                    <span className="danger-text text-xs font-medium">
                                        <RelativeTime value={err.occurredAt} now={now} />
                                    </span>
                                </summary>
                                <div className="space-y-3 border-t border-[var(--danger-border)] px-4 pb-4 pt-3 text-sm">
                                    <p className="danger-text">{err.message}</p>
                                    {err.runtimeIdentity && (
                                        <p className={cx(ui.muted, 'text-xs font-medium')}>Runtime: {err.runtimeIdentity}</p>
                                    )}
                                    {err.stackTrace && (
                                        <pre className="code-block max-h-80 overflow-auto rounded-2xl border border-[var(--border-soft)] p-4 text-xs leading-5">
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

function JobDetailSkeleton() {
    return (
        <div className="space-y-6">
            <div className="page-header relative overflow-hidden rounded-[2rem] border p-6">
                <SkeletonBlock className="h-3 w-28" />
                <SkeletonBlock className="mt-4 h-10 w-48" />
                <SkeletonBlock className="mt-4 h-4 w-80 max-w-full" />
            </div>

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                <MetricCardSkeleton />
                <MetricCardSkeleton />
                <MetricCardSkeleton />
                <MetricCardSkeleton />
                <MetricCardSkeleton />
            </div>

            <Panel>
                <div className={ui.panelHeadingAccent}>
                    <SkeletonBlock className="h-5 w-24" />
                    <SkeletonBlock className="mt-3 h-4 w-56" />
                </div>
                <div className="grid gap-3 p-5 md:grid-cols-2 xl:grid-cols-4">
                    <SkeletonBlock className="h-20 rounded-2xl" />
                    <SkeletonBlock className="h-20 rounded-2xl" />
                    <SkeletonBlock className="h-20 rounded-2xl" />
                    <SkeletonBlock className="h-20 rounded-2xl" />
                </div>
            </Panel>
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
        <div className="rounded-2xl border border-[var(--border-soft)] bg-[var(--chip)] p-4">
            <p className={cx(ui.soft, 'text-xs font-semibold uppercase tracking-[0.18em]')}>{label}</p>
            <p className={cx(ui.strong, 'mt-2 text-sm font-semibold')}>
                <RelativeTime value={value} now={now} fallback={empty} />
            </p>
        </div>
    );
}
