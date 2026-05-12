import { useSchedules } from '../api/hooks';

export default function SchedulesList() {
    const { data, isLoading, error } = useSchedules();

    return (
        <div>
            <h2 className="text-lg font-semibold text-gray-900 mb-4">Schedules</h2>
            {isLoading && <p className="text-sm text-gray-400">Loading…</p>}
            {error && <p className="text-sm text-red-500">Error loading schedules.</p>}
            {data && (
                <table className="w-full text-sm border-collapse">
                    <thead>
                        <tr className="border-b border-gray-200 text-left text-gray-500 text-xs uppercase tracking-wide">
                            <th className="py-2 pr-4">Job Key</th>
                            <th className="py-2 pr-4">Cron</th>
                            <th className="py-2 pr-4">Queue</th>
                            <th className="py-2 pr-4">Next Run</th>
                            <th className="py-2 pr-4">Last Run</th>
                            <th className="py-2">Enabled</th>
                        </tr>
                    </thead>
                    <tbody>
                        {data.map(s => (
                            <tr key={s.id} className="border-b border-gray-100 hover:bg-gray-50">
                                <td className="py-2 pr-4 font-mono text-xs">{s.jobKey}</td>
                                <td className="py-2 pr-4 font-mono text-xs text-gray-500">{s.cron}</td>
                                <td className="py-2 pr-4 text-gray-500">{s.queueKey}</td>
                                <td className="py-2 pr-4 text-gray-500 text-xs">
                                    {s.nextRunAt ? new Date(s.nextRunAt).toLocaleString() : '—'}
                                </td>
                                <td className="py-2 pr-4 text-gray-500 text-xs">
                                    {s.lastRunAt ? new Date(s.lastRunAt).toLocaleString() : 'Never'}
                                </td>
                                <td className="py-2">
                                    <span
                                        className={`px-2 py-0.5 rounded-full text-xs font-medium ${
                                            s.enabled ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'
                                        }`}
                                    >
                                        {s.enabled ? 'Enabled' : 'Disabled'}
                                    </span>
                                </td>
                            </tr>
                        ))}
                        {data.length === 0 && (
                            <tr>
                                <td colSpan={6} className="py-8 text-center text-gray-400 text-sm">
                                    No schedules registered.
                                </td>
                            </tr>
                        )}
                    </tbody>
                </table>
            )}
        </div>
    );
}
