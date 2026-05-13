import { useServers } from '../api/hooks';
import {
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

export default function Servers() {
    const { data, isLoading, error, dataUpdatedAt } = useServers();
    const now = useNow(1_000);
    const servers = data ?? [];
    const stale = servers.filter(server => getAgeSeconds(server.lastHeartbeatAt, now, server.ageSeconds) > 60).length;
    const active = servers.length - stale;

    return (
        <div className="space-y-6">
            <PageHeader
                eyebrow="Runtime presence"
                title="Servers"
                description="Worker identities with latest heartbeat state and stale highlighting."
                actions={
                    <div className={ui.toolbarPill}>
                        Updated <RelativeTime value={dataUpdatedAt || null} now={now} />
                    </div>
                }
            />

            {isLoading ? (
                <div className="grid gap-4 md:grid-cols-3">
                    <MetricCardSkeleton />
                    <MetricCardSkeleton />
                    <MetricCardSkeleton />
                </div>
            ) : (
                <div className="grid gap-4 md:grid-cols-3">
                    <MetricCard label="Registered" value={formatNumber(servers.length)} helper="Workers seen by dashboard" tone="blue" />
                    <MetricCard label="Active" value={formatNumber(active)} helper="Heartbeat within 60s" tone="green" />
                    <MetricCard label="Stale" value={formatNumber(stale)} helper="Heartbeat older than 60s" tone="red" />
                </div>
            )}

            <Panel>
                <div className={ui.panelHeading}>
                    <h2 className={ui.sectionTitle}>Server heartbeat table</h2>
                    <p className={ui.sectionDescription}>Each worker appears once with its latest heartbeat.</p>
                </div>

                {isLoading && <TableSkeleton columns={3} rows={4} />}
                {error && <div className={ui.error}>Error loading servers.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[560px] text-left text-sm">
                                <thead>
                                    <tr className={ui.tableHeadRow}>
                                        <th className="px-5 py-4 font-semibold">Server identity</th>
                                        <th className="px-5 py-4 font-semibold">State</th>
                                        <th className="px-5 py-4 font-semibold">Last heartbeat</th>
                                    </tr>
                                </thead>
                                <tbody className={ui.tableBody}>
                                    {servers.map(server => {
                                        const ageSeconds = getAgeSeconds(server.lastHeartbeatAt, now, server.ageSeconds);
                                        const isStale = ageSeconds > 60;
                                        const instanceName = formatInstanceName(server.instanceId);
                                        const hasExpandedIdentity = instanceName !== server.instanceId;

                                        return (
                                            <tr key={server.instanceId} className={ui.tableRow}>
                                                <td className="px-5 py-4">
                                                    <div className={cx(ui.strong, 'font-semibold')}>{instanceName}</div>
                                                    {hasExpandedIdentity && (
                                                        <div className={cx(ui.soft, 'mt-1 font-mono text-xs')}>
                                                            {server.instanceId}
                                                        </div>
                                                    )}
                                                </td>
                                                <td className="px-5 py-4">
                                                    <StatusPill status={isStale ? 'Stale' : 'Active'} />
                                                </td>
                                                <td className={cx(ui.defaultText, 'px-5 py-4')}>
                                                    <RelativeTime value={server.lastHeartbeatAt} now={now} />
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>

                        {servers.length === 0 && (
                            <EmptyState
                                title="No active servers"
                                description="Workers will appear here after they report heartbeats."
                            />
                        )}
                    </>
                )}
            </Panel>
        </div>
    );
}

function getAgeSeconds(value: string, now: number, fallbackAgeSeconds: number): number {
    const timestamp = new Date(value).getTime();
    return Number.isNaN(timestamp) ? fallbackAgeSeconds : Math.max(0, Math.floor((now - timestamp) / 1000));
}

function formatInstanceName(instanceId: string): string {
    const [firstSegment] = instanceId.split(':');
    return firstSegment || instanceId;
}
