import { useLayoutEffect, useState } from 'react';
import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { routePrefix, title } from './config';
import JobsList from './views/JobsList';
import JobDetail from './views/JobDetail';
import SchedulesList from './views/SchedulesList';
import QueueStats from './views/QueueStats';
import Servers from './views/Servers';
import { cx } from './components/DashboardUi';
import { applyTheme, readStoredTheme, type ThemeMode, writeStoredTheme } from './theme';

const queryClient = new QueryClient();
const navItems = [
    { to: `${routePrefix}/jobs`, label: 'Jobs', description: 'Inspect queue work', accent: 'bg-sky-400' },
    { to: `${routePrefix}/schedules`, label: 'Schedules', description: 'Recurring workload', accent: 'bg-violet-400' },
    { to: `${routePrefix}/queues`, label: 'Queues', description: 'Throughput by queue', accent: 'bg-emerald-400' },
    { to: `${routePrefix}/servers`, label: 'Servers', description: 'Heartbeat state', accent: 'bg-amber-400' },
];

function AppShell() {
    const [theme, setTheme] = useState<ThemeMode>(readStoredTheme);

    useLayoutEffect(() => {
        applyTheme(theme);
    }, [theme]);

    const toggleTheme = () => {
        setTheme(currentTheme => {
            const nextTheme = currentTheme === 'dark' ? 'light' : 'dark';
            writeStoredTheme(nextTheme);

            return nextTheme;
        });
    };

    return (
        <div className="app-shell min-h-screen overflow-x-hidden">
            <div className="app-backdrop pointer-events-none fixed inset-0" />
            <div className="app-top-sheen pointer-events-none fixed inset-x-0 top-0 h-64" />

            <div className="relative flex min-h-screen flex-col lg:flex-row">
                <aside className="p-4 lg:w-80 lg:p-6">
                    <nav className="sticky top-6 space-y-4">
                        <div className="brand-card rounded-[2rem] border p-5 backdrop-blur">
                            <div className="flex items-center gap-3">
                                <div className="brand-logo flex h-12 w-12 items-center justify-center rounded-2xl text-xl font-black tracking-tight shadow-lg shadow-sky-950/20">
                                    A
                                </div>
                                <div>
                                    <p className="brand-eyebrow text-xs font-semibold uppercase tracking-[0.3em]">Atomizer</p>
                                    <h1 className="text-lg font-semibold tracking-tight">{title}</h1>
                                </div>
                            </div>
                            <p className="text-muted mt-4 text-sm leading-6">
                                Real-time background job observability with queue, schedule, and worker heartbeat context.
                            </p>
                            <ThemeSwitch theme={theme} onToggle={toggleTheme} />
                        </div>

                        <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-1">
                            {navItems.map(link => (
                                <li key={link.to}>
                                    <NavLink
                                        to={link.to}
                                        className={({ isActive }) =>
                                            cx(
                                                'nav-item group flex items-center gap-3 rounded-2xl border px-4 py-3 text-sm transition-all duration-200',
                                                isActive && 'active',
                                            )
                                        }
                                    >
                                        <span className={cx('h-2.5 w-2.5 rounded-full shadow-sm', link.accent)} />
                                        <span>
                                            <span className="block font-semibold">{link.label}</span>
                                            <span className="nav-description block text-xs">{link.description}</span>
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

function ThemeSwitch({ theme, onToggle }: { theme: ThemeMode; onToggle: () => void }) {
    const isLight = theme === 'light';

    return (
        <button
            type="button"
            role="switch"
            aria-checked={isLight}
            aria-label={`Switch to ${isLight ? 'dark' : 'light'} mode`}
            onClick={onToggle}
            className="theme-switch mt-5 flex w-full items-center justify-between rounded-2xl px-3 py-2 text-sm font-semibold transition"
        >
            <span>{isLight ? 'Light mode' : 'Dark mode'}</span>
            <span className="theme-switch-track relative h-6 w-11 rounded-full p-0.5">
                <span
                    className={cx(
                        'theme-switch-thumb block h-5 w-5 rounded-full transition-transform duration-200',
                        isLight && 'translate-x-5',
                    )}
                />
            </span>
        </button>
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
