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
import { formatDuration } from '../utils/time';

export default function Servers() {
    const { data, isLoading, error } = useServers();
    const now = useNow(1_000);
    const servers = data ?? [];
    const stale = servers.filter(server => getAgeSeconds(server.lastHeartbeatAt, now) > 60).length;
    const active = servers.length - stale;
    const newestHeartbeat = servers
        .map(server => new Date(server.lastHeartbeatAt).getTime())
        .filter(timestamp => !Number.isNaN(timestamp))
        .sort((left, right) => right - left)[0];

    return (
        <div className="space-y-6">
            <PageHeader
                eyebrow="Runtime presence"
                title="Servers"
                description="Worker heartbeats with relative freshness and stale-state highlighting."
                actions={
                    <div className="rounded-2xl bg-slate-100 px-3 py-2 text-xs font-medium text-slate-600">
                        Latest <RelativeTime value={newestHeartbeat ?? null} now={now} />
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
                    <p className="mt-1 text-sm text-slate-500">Freshness is shown as relative time instead of full timestamps.</p>
                </div>

                {isLoading && <div className="p-8 text-sm text-slate-500">Loading servers…</div>}
                {error && <div className="p-8 text-sm font-medium text-rose-600">Error loading servers.</div>}
                {data && (
                    <>
                        <div className="overflow-x-auto">
                            <table className="w-full min-w-[720px] text-left text-sm">
                                <thead>
                                    <tr className="border-b border-slate-200/80 bg-slate-50/80 text-xs uppercase tracking-[0.16em] text-slate-400">
                                        <th className="px-5 py-4 font-semibold">Server</th>
                                        <th className="px-5 py-4 font-semibold">State</th>
                                        <th className="px-5 py-4 font-semibold">Last heartbeat</th>
                                        <th className="px-5 py-4 font-semibold">Age</th>
                                        <th className="px-5 py-4 font-semibold">Identity</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100">
                                    {servers.map(server => {
                                        const ageSeconds = getAgeSeconds(server.lastHeartbeatAt, now);
                                        const isStale = ageSeconds > 60;
                                        return (
                                            <tr key={server.id} className="bg-white/70 transition hover:bg-slate-50">
                                                <td className="px-5 py-4 font-semibold text-slate-950">{server.machineName}</td>
                                                <td className="px-5 py-4">
                                                    <StatusPill status={isStale ? 'Stale' : 'Active'} />
                                                </td>
                                                <td className="px-5 py-4 text-slate-600">
                                                    <RelativeTime value={server.lastHeartbeatAt} now={now} />
                                                </td>
                                                <td className="px-5 py-4 font-mono text-xs text-slate-500">
                                                    {formatDuration(ageSeconds)}
                                                </td>
                                                <td className="px-5 py-4 font-mono text-xs text-slate-400">
                                                    {server.id.slice(0, 12)}…
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

function getAgeSeconds(value: string, now: number): number {
    const timestamp = new Date(value).getTime();
    return Number.isNaN(timestamp) ? Number.POSITIVE_INFINITY : Math.max(0, Math.floor((now - timestamp) / 1000));
}
