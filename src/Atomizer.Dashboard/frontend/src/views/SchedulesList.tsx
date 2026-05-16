import type { KeyboardEvent, MouseEvent } from 'react';
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useSchedules } from '../api/hooks';
import { routePrefix, statsRefreshMs } from '../config';
import type { ScheduleDto } from '../api/types';
import {
    ConfirmationDialog,
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
    cx,
} from '../components/DashboardUi';
import { useNow } from '../hooks/useNow';

type ScheduleConfirmation =
    | { action: 'toggle'; schedule: ScheduleDto }
    | { action: 'runNow'; schedule: ScheduleDto };

export default function SchedulesList() {
    const { data, isLoading, error, dataUpdatedAt } = useSchedules();
    const navigate = useNavigate();
    const queryClient = useQueryClient();
    const now = useNow(1_000);
    const [confirmation, setConfirmation] = useState<ScheduleConfirmation | null>(null);
    const invalidateScheduleActions = () => {
        queryClient.invalidateQueries({ queryKey: ['schedules'] });
        queryClient.invalidateQueries({ queryKey: ['jobs'] });
        queryClient.invalidateQueries({ queryKey: ['queueStats'] });
    };
    const toggleMutation = useMutation({
        mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => api.setScheduleEnabled(id, enabled),
        onSuccess: invalidateScheduleActions,
    });
    const runNowMutation = useMutation({
        mutationFn: api.runScheduleNow,
        onSuccess: invalidateScheduleActions,
    });
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

    const stopRowAction = (event: MouseEvent<HTMLButtonElement>) => event.stopPropagation();
    const stopRowKeyboardAction = (event: KeyboardEvent<HTMLButtonElement>) => event.stopPropagation();

    return (
        <div className="space-y-6">
            <PageHeader
                eyebrow="Recurring work"
                title="Schedules"
                description="Cron-driven jobs with next run, last run, queue, and payload context in one operator table."
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
                    <MetricCard label="Schedules" value={formatNumber(schedules.length)} helper="Registered recurring jobs" tone="purple" />
                    <MetricCard label="Enabled" value={formatNumber(enabled)} helper="Allowed to enqueue work" tone="green" />
                    <MetricCard label="Paused" value={formatNumber(paused)} helper="Currently disabled" tone="slate" />
                    <MetricCard label="Due soon" value={formatNumber(dueSoon)} helper="Next hour" tone="amber" />
                </div>
            )}

            <Panel>
                <div className={ui.panelHeading}>
                    <h2 className={ui.sectionTitle}>Schedule table</h2>
                    <p className={ui.sectionDescription}>Open a row to see jobs created by that queue and payload type.</p>
                </div>

                {isLoading && <TableSkeleton columns={9} rows={5} />}
                {error && <div className={ui.error}>Error loading schedules.</div>}
                {(toggleMutation.error || runNowMutation.error) && (
                    <div className="danger-text border-t border-[var(--border-soft)] px-5 py-3 text-sm font-medium">
                        {(toggleMutation.error ?? runNowMutation.error)?.message}
                    </div>
                )}
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
                                        <th className="px-5 py-4 font-semibold">Actions</th>
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
                                                <RelativeTime
                                                    value={getLastRunDisplayValue(schedule.lastRunAt, dataUpdatedAt || now)}
                                                    now={now}
                                                    fallback="Never"
                                                />
                                            </td>
                                            <td className={cx(ui.muted, 'px-5 py-4')}>{schedule.misfirePolicy}</td>
                                            <td className="px-5 py-4">
                                                <StatusPill status={schedule.enabled ? 'Enabled' : 'Disabled'} />
                                            </td>
                                            <td className="px-5 py-4">
                                                <div className="flex flex-wrap gap-2">
                                                    <button
                                                        type="button"
                                                        className={ui.smallButton}
                                                        disabled={toggleMutation.isPending}
                                                        onKeyDown={stopRowKeyboardAction}
                                                        onClick={event => {
                                                            stopRowAction(event);
                                                            setConfirmation({ action: 'toggle', schedule });
                                                        }}
                                                    >
                                                        {schedule.enabled ? 'Disable' : 'Enable'}
                                                    </button>
                                                    <button
                                                        type="button"
                                                        className={ui.smallButton}
                                                        disabled={runNowMutation.isPending}
                                                        onKeyDown={stopRowKeyboardAction}
                                                        onClick={event => {
                                                            stopRowAction(event);
                                                            setConfirmation({ action: 'runNow', schedule });
                                                        }}
                                                    >
                                                        Run now
                                                    </button>
                                                </div>
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

            <ConfirmationDialog
                open={confirmation !== null}
                title={getScheduleConfirmationTitle(confirmation)}
                description={getScheduleConfirmationDescription(confirmation)}
                confirmLabel={getScheduleConfirmationLabel(confirmation)}
                confirmTone={
                    confirmation?.action === 'toggle' && confirmation.schedule.enabled ? 'danger' : 'default'
                }
                onCancel={() => setConfirmation(null)}
                onConfirm={() => {
                    if (!confirmation) {
                        return;
                    }

                    if (confirmation.action === 'toggle') {
                        toggleMutation.mutate({
                            id: confirmation.schedule.id,
                            enabled: !confirmation.schedule.enabled,
                        });
                    } else {
                        runNowMutation.mutate(confirmation.schedule.id);
                    }

                    setConfirmation(null);
                }}
            />
        </div>
    );
}

function getScheduleConfirmationTitle(confirmation: ScheduleConfirmation | null): string {
    if (confirmation?.action === 'runNow') {
        return 'Run schedule now?';
    }

    return confirmation?.schedule.enabled ? 'Disable schedule?' : 'Enable schedule?';
}

function getScheduleConfirmationDescription(confirmation: ScheduleConfirmation | null): string {
    if (confirmation?.action === 'runNow') {
        return `Create a job for ${confirmation.schedule.jobKey} immediately.`;
    }

    if (confirmation?.action === 'toggle' && confirmation.schedule.enabled) {
        return `Disable ${confirmation.schedule.jobKey}. Future runs will stop until it is enabled again.`;
    }

    if (confirmation?.action === 'toggle') {
        return `Enable ${confirmation.schedule.jobKey}. Future runs can be enqueued again.`;
    }

    return '';
}

function getScheduleConfirmationLabel(confirmation: ScheduleConfirmation | null): string {
    if (confirmation?.action === 'runNow') {
        return 'Run now';
    }

    return confirmation?.schedule.enabled ? 'Disable schedule' : 'Enable schedule';
}

function getLastRunDisplayValue(lastRunAt: string | null, latestKnownRefreshAt: number): string | number | null {
    if (!lastRunAt) {
        return null;
    }

    const timestamp = new Date(lastRunAt).getTime();
    if (Number.isNaN(timestamp)) {
        return lastRunAt;
    }

    return Math.min(timestamp, latestKnownRefreshAt);
}
