import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { routePrefix, title } from './config';
import JobsList from './views/JobsList';
import JobDetail from './views/JobDetail';
import SchedulesList from './views/SchedulesList';
import QueueStats from './views/QueueStats';
import Servers from './views/Servers';

const queryClient = new QueryClient();

function AppShell() {
    return (
        <div className="flex h-screen bg-gray-50">
            <nav className="w-56 bg-white border-r border-gray-200 flex flex-col">
                <div className="p-4 border-b border-gray-200">
                    <h1 className="text-sm font-semibold text-gray-900">{title}</h1>
                </div>
                <ul className="flex-1 p-2 space-y-1">
                    {[
                        { to: `${routePrefix}/jobs`, label: 'Jobs' },
                        { to: `${routePrefix}/schedules`, label: 'Schedules' },
                        { to: `${routePrefix}/queues`, label: 'Queues' },
                        { to: `${routePrefix}/servers`, label: 'Servers' },
                    ].map(link => (
                        <li key={link.to}>
                            <NavLink
                                to={link.to}
                                className={({ isActive }) =>
                                    `block px-3 py-2 rounded text-sm ${isActive ? 'bg-blue-50 text-blue-700 font-medium' : 'text-gray-600 hover:bg-gray-100'}`
                                }
                            >
                                {link.label}
                            </NavLink>
                        </li>
                    ))}
                </ul>
            </nav>

            <main className="flex-1 overflow-auto p-6">
                <Routes>
                    <Route path={`${routePrefix}/jobs/:id`} element={<JobDetail />} />
                    <Route path={`${routePrefix}/jobs`} element={<JobsList />} />
                    <Route path={`${routePrefix}/schedules`} element={<SchedulesList />} />
                    <Route path={`${routePrefix}/queues`} element={<QueueStats />} />
                    <Route path={`${routePrefix}/servers`} element={<Servers />} />
                    <Route path={routePrefix} element={<JobsList />} />
                </Routes>
            </main>
        </div>
    );
}

export default function App() {
    return (
        <QueryClientProvider client={queryClient}>
            <BrowserRouter>
                <AppShell />
            </BrowserRouter>
        </QueryClientProvider>
    );
}
