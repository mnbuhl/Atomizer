import { useServers } from '../api/hooks';
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
                    <div className="rounded-2xl bg-slate-100 px-3 py-2 text-xs font-medium text-slate-600">
                        Updated <RelativeTime value={dataUpdatedAt || null} now={now} />
                    </div>
                }
            />

            <div className="grid gap-4 md:grid-cols-3">
                <MetricCard label="Registered" value={formatNumber(servers.length)} helper="Workers seen by dashboard" tone="blue" />
                <MetricCard label="Active" value={formatNumber(active)} helper="Heartbeat within 60s" tone="green" />
                <MetricCard label="Stale" value={formatNumber(stale)} helper="Heartbeat older than 60s" tone="red" />
            </div>

            <Panel>
                <div className="border-b border-slate-200/80 p-5">
                    <h2 className="text-base font-semibold text-slate-950">Server heartbeat table</h2>
                    <p className="mt-1 text-sm text-slate-500">Each worker appears once with its latest heartbeat.</p>
                </div>

                {isLoading && <div className="p-8 text-sm text-slate-500">Loading servers…</div>}
                {error && <div className="p-8 text-sm font-medium text-rose-600">Error loading servers.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[560px] text-left text-sm">
                                <thead>
                                    <tr className="border-b border-slate-200/80 bg-slate-50/80 text-xs uppercase tracking-[0.16em] text-slate-400">
                                        <th className="px-5 py-4 font-semibold">Server identity</th>
                                        <th className="px-5 py-4 font-semibold">State</th>
                                        <th className="px-5 py-4 font-semibold">Last heartbeat</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100">
                                    {servers.map(server => {
                                        const ageSeconds = getAgeSeconds(server.lastHeartbeatAt, now, server.ageSeconds);
                                        const isStale = ageSeconds > 60;
                                        const instanceName = formatInstanceName(server.instanceId);
                                        const hasExpandedIdentity = instanceName !== server.instanceId;

                                        return (
                                            <tr key={server.instanceId} className="bg-white transition hover:bg-sky-50/60">
                                                <td className="px-5 py-4">
                                                    <div className="font-semibold text-slate-950">{instanceName}</div>
                                                    {hasExpandedIdentity && (
                                                        <div className="mt-1 font-mono text-xs text-slate-400">
                                                            {server.instanceId}
                                                        </div>
                                                    )}
                                                </td>
                                                <td className="px-5 py-4">
                                                    <StatusPill status={isStale ? 'Stale' : 'Active'} />
                                                </td>
                                                <td className="px-5 py-4 text-slate-600">
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
