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
                <div className="border-b border-slate-200/80 p-5">
                    <h2 className="text-base font-semibold text-slate-950">Schedule table</h2>
                    <p className="mt-1 text-sm text-slate-500">Open a row to see jobs created by that queue and payload type.</p>
                </div>

                {isLoading && <div className="p-8 text-sm text-slate-500">Loading schedules…</div>}
                {error && <div className="p-8 text-sm font-medium text-rose-600">Error loading schedules.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[980px] text-left text-sm">
                                <thead>
                                    <tr className="border-b border-slate-200/80 bg-slate-50/80 text-xs uppercase tracking-[0.16em] text-slate-400">
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
                                <tbody className="divide-y divide-slate-100">
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
                                            className="group cursor-pointer bg-white/70 transition hover:bg-sky-50/70 focus:bg-sky-50/70 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-sky-300"
                                        >
                                            <td className="px-5 py-4">
                                                <div className="font-mono text-xs font-semibold text-slate-900">
                                                    {schedule.jobKey}
                                                </div>
                                                <div className="mt-1 text-xs font-semibold text-sky-600 opacity-0 transition group-hover:opacity-100 group-focus:opacity-100">
                                                    View jobs →
                                                </div>
                                            </td>
                                            <td className="px-5 py-4 font-mono text-xs text-slate-600">{schedule.cron}</td>
                                            <td className="px-5 py-4">
                                                <span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-semibold text-slate-600">
                                                    {schedule.queueKey}
                                                </span>
                                            </td>
                                            <td className="max-w-xs px-5 py-4">
                                                <div className="truncate text-slate-700">{schedule.payloadTypeName}</div>
                                            </td>
                                            <td className="px-5 py-4 font-medium text-slate-700">
                                                <RelativeTime value={schedule.nextRunAt} now={now} fallback="Not planned" />
                                            </td>
                                            <td className="px-5 py-4 text-slate-500">
                                                <RelativeTime value={schedule.lastRunAt} now={now} fallback="Never" />
                                            </td>
                                            <td className="px-5 py-4 text-slate-500">{schedule.misfirePolicy}</td>
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
