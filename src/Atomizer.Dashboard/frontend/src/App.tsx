import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { routePrefix, title } from './config';
import JobsList from './views/JobsList';
import JobDetail from './views/JobDetail';
import SchedulesList from './views/SchedulesList';
import QueueStats from './views/QueueStats';
import Servers from './views/Servers';
import { cx } from './components/DashboardUi';

const queryClient = new QueryClient();
const navItems = [
    { to: `${routePrefix}/jobs`, label: 'Jobs', description: 'Inspect queue work', accent: 'bg-sky-400' },
    { to: `${routePrefix}/schedules`, label: 'Schedules', description: 'Recurring workload', accent: 'bg-violet-400' },
    { to: `${routePrefix}/queues`, label: 'Queues', description: 'Throughput by queue', accent: 'bg-emerald-400' },
    { to: `${routePrefix}/servers`, label: 'Servers', description: 'Heartbeat state', accent: 'bg-amber-400' },
];

function AppShell() {
    return (
        <div className="min-h-screen overflow-x-hidden bg-slate-950 text-slate-950">
            <div className="pointer-events-none fixed inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(56,189,248,0.22),_transparent_34rem),radial-gradient(circle_at_bottom_right,_rgba(16,185,129,0.16),_transparent_30rem)]" />
            <div className="pointer-events-none fixed inset-x-0 top-0 h-64 bg-gradient-to-b from-white/10 to-transparent" />

            <div className="relative flex min-h-screen flex-col lg:flex-row">
                <aside className="p-4 lg:w-80 lg:p-6">
                    <nav className="sticky top-6 space-y-4">
                        <div className="rounded-[2rem] border border-white/15 bg-white/10 p-5 text-white shadow-2xl shadow-slate-950/20 backdrop-blur">
                            <div className="flex items-center gap-3">
                                <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-white text-xl font-black tracking-tight text-slate-950 shadow-lg shadow-sky-950/20">
                                    A
                                </div>
                                <div>
                                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-sky-200">Atomizer</p>
                                    <h1 className="text-lg font-semibold tracking-tight">{title}</h1>
                                </div>
                            </div>
                            <p className="mt-4 text-sm leading-6 text-slate-300">
                                Real-time background job observability with queue, schedule, and worker heartbeat context.
                            </p>
                        </div>

                        <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-1">
                            {navItems.map(link => (
                                <li key={link.to}>
                                    <NavLink
                                        to={link.to}
                                        className={({ isActive }) =>
                                            cx(
                                                'group flex items-center gap-3 rounded-2xl border px-4 py-3 text-sm transition-all duration-200',
                                                isActive
                                                    ? 'active border-white/80 bg-white text-slate-950 shadow-xl shadow-slate-950/20'
                                                    : 'border-white/10 bg-white/5 text-slate-300 hover:border-white/30 hover:bg-white/10 hover:text-white',
                                            )
                                        }
                                    >
                                        <span className={cx('h-2.5 w-2.5 rounded-full shadow-sm', link.accent)} />
                                        <span>
                                            <span className="block font-semibold">{link.label}</span>
                                            <span className="block text-xs text-slate-400 group-[.active]:text-slate-500">
                                                {link.description}
                                            </span>
                                        </span>
                                    </NavLink>
                                </li>
                            ))}
                        </ul>
                    </nav>
                </aside>

                <main className="flex-1 p-4 pt-0 sm:p-6 lg:p-8 lg:pl-2">
                    <div className="mx-auto max-w-7xl space-y-6">
                        <Routes>
                            <Route path={`${routePrefix}/jobs/:id`} element={<JobDetail />} />
                            <Route path={`${routePrefix}/jobs`} element={<JobsList />} />
                            <Route path={`${routePrefix}/schedules`} element={<SchedulesList />} />
                            <Route path={`${routePrefix}/queues`} element={<QueueStats />} />
                            <Route path={`${routePrefix}/servers`} element={<Servers />} />
                            <Route path={routePrefix} element={<JobsList />} />
                        </Routes>
                    </div>
                </main>
            </div>
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
