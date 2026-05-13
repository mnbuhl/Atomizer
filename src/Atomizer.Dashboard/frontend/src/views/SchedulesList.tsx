import type { KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useSchedules } from '../api/hooks';
import { routePrefix } from '../config';
import {
    EmptyState,
    formatNumber,
    MetricCard,
    PageHeader,
    Panel,
    RelativeTime,
    StatusPill,
    ui,
    cx,
} from '../components/DashboardUi';
import { useNow } from '../hooks/useNow';

export default function SchedulesList() {
    const { data, isLoading, error } = useSchedules();
    const navigate = useNavigate();
    const now = useNow(30_000);
    const schedules = data ?? [];
    const enabled = schedules.filter(schedule => schedule.enabled).length;
    const paused = schedules.length - enabled;
    const dueSoon = schedules.filter(schedule => {
        if (!schedule.nextRunAt) return false;
        const nextRun = new Date(schedule.nextRunAt).getTime();
        return nextRun >= now && nextRun - now <= 60 * 60 * 1000;
    }).length;

    const openScheduleJobs = (queueKey: string, payloadTypeName: string) =>
        navigate(
            `${routePrefix}/jobs?queue=${encodeURIComponent(queueKey)}&payload=${encodeURIComponent(payloadTypeName)}`,
        );

    const handleRowKeyDown = (
        event: KeyboardEvent<HTMLTableRowElement>,
        queueKey: string,
        payloadTypeName: string,
    ) => {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            openScheduleJobs(queueKey, payloadTypeName);
        }
    };

    return (
        <div className="space-y-6">
            <PageHeader
                eyebrow="Recurring work"
                title="Schedules"
                description="Cron-driven jobs with next run, last run, queue, and payload context in one operator table."
            />

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                <MetricCard label="Schedules" value={formatNumber(schedules.length)} helper="Registered recurring jobs" tone="purple" />
                <MetricCard label="Enabled" value={formatNumber(enabled)} helper="Allowed to enqueue work" tone="green" />
                <MetricCard label="Paused" value={formatNumber(paused)} helper="Currently disabled" tone="slate" />
                <MetricCard label="Due soon" value={formatNumber(dueSoon)} helper="Next hour" tone="amber" />
            </div>

            <Panel>
                <div className={ui.panelHeading}>
                    <h2 className={ui.sectionTitle}>Schedule table</h2>
                    <p className={ui.sectionDescription}>Open a row to see jobs created by that queue and payload type.</p>
                </div>

                {isLoading && <div className={ui.loading}>Loading schedules…</div>}
                {error && <div className={ui.error}>Error loading schedules.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[980px] text-left text-sm">
                                <thead>
                                    <tr className={ui.tableHeadRow}>
                                        <th className="px-5 py-4 font-semibold">Job</th>
                                        <th className="px-5 py-4 font-semibold">Cron</th>
                                        <th className="px-5 py-4 font-semibold">Queue</th>
                                        <th className="px-5 py-4 font-semibold">Payload</th>
                                        <th className="px-5 py-4 font-semibold">Next run</th>
                                        <th className="px-5 py-4 font-semibold">Last run</th>
                                        <th className="px-5 py-4 font-semibold">Misfire</th>
                                        <th className="px-5 py-4 font-semibold">State</th>
                                    </tr>
                                </thead>
                                <tbody className={ui.tableBody}>
                                    {schedules.map(schedule => (
                                        <tr
                                            key={schedule.id}
                                            role="link"
                                            tabIndex={0}
                                            aria-label={`Open jobs for schedule ${schedule.jobKey}`}
                                            onClick={() => openScheduleJobs(schedule.queueKey, schedule.payloadTypeName)}
                                            onKeyDown={event =>
                                                handleRowKeyDown(event, schedule.queueKey, schedule.payloadTypeName)
                                            }
                                            className={ui.interactiveTableRow}
                                        >
                                            <td className="px-5 py-4">
                                                <div className={cx(ui.strong, 'font-mono text-xs font-semibold')}>
                                                    {schedule.jobKey}
                                                </div>
                                                <div className={ui.rowAction}>
                                                    View jobs →
                                                </div>
                                            </td>
                                            <td className={cx(ui.defaultText, 'px-5 py-4 font-mono text-xs')}>{schedule.cron}</td>
                                            <td className="px-5 py-4">
                                                <span className={ui.softChip}>
                                                    {schedule.queueKey}
                                                </span>
                                            </td>
                                            <td className="max-w-xs px-5 py-4">
                                                <div className={cx(ui.defaultText, 'truncate')}>{schedule.payloadTypeName}</div>
                                            </td>
                                            <td className={cx(ui.defaultText, 'px-5 py-4 font-medium')}>
                                                <RelativeTime value={schedule.nextRunAt} now={now} fallback="Not planned" />
                                            </td>
                                            <td className={cx(ui.muted, 'px-5 py-4')}>
                                                <RelativeTime value={schedule.lastRunAt} now={now} fallback="Never" />
                                            </td>
                                            <td className={cx(ui.muted, 'px-5 py-4')}>{schedule.misfirePolicy}</td>
                                            <td className="px-5 py-4">
                                                <StatusPill status={schedule.enabled ? 'Enabled' : 'Disabled'} />
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>

                        {schedules.length === 0 && (
                            <EmptyState
                                title="No schedules registered"
                                description="Recurring schedule definitions will appear here when configured."
                            />
                        )}
                    </>
                )}
            </Panel>
        </div>
    );
}
