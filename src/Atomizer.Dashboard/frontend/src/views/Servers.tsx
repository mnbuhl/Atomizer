import { useState, useEffect } from 'react';
import { useServers } from '../api/hooks';

export default function Servers() {
    const { data, isLoading, error } = useServers();
    const [, setTick] = useState(0);

    useEffect(() => {
        const timer = setInterval(() => setTick(t => t + 1), 1000);
        return () => clearInterval(timer);
    }, []);

    return (
        <div>
            <h2 className="text-lg font-semibold text-gray-900 mb-4">Registered Servers</h2>
            {isLoading && <p className="text-sm text-gray-400">Loading…</p>}
            {error && <p className="text-sm text-red-500">Error loading servers.</p>}
            {data && (
                <table className="w-full text-sm border-collapse">
                    <thead>
                        <tr className="border-b border-gray-200 text-left text-gray-500 text-xs uppercase tracking-wide">
                            <th className="py-2 pr-4">Instance</th>
                            <th className="py-2 pr-4">Last Heartbeat</th>
                            <th className="py-2">Age</th>
                        </tr>
                    </thead>
                    <tbody>
                        {data.map(s => {
                            const ageSeconds = Math.floor(
                                (Date.now() - new Date(s.lastHeartbeatAt).getTime()) / 1000,
                            );
                            return (
                                <tr key={s.id} className="border-b border-gray-100 hover:bg-gray-50">
                                    <td className="py-2 pr-4 font-mono text-xs">{s.machineName}</td>
                                    <td className="py-2 pr-4 text-gray-500 text-xs">
                                        {new Date(s.lastHeartbeatAt).toLocaleString()}
                                    </td>
                                    <td className="py-2 text-xs">
                                        <span className={ageSeconds > 60 ? 'text-red-500' : 'text-gray-500'}>
                                            {ageSeconds}s ago
                                        </span>
                                    </td>
                                </tr>
                            );
                        })}
                        {data.length === 0 && (
                            <tr>
                                <td colSpan={3} className="py-8 text-center text-gray-400 text-sm">
                                    No active servers.
                                </td>
                            </tr>
                        )}
                    </tbody>
                </table>
            )}
        </div>
    );
}
