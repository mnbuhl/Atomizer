import type { ReactNode } from 'react';
import { formatAbsoluteDateTime, formatRelativeTime } from '../utils/time';

type Tone = 'slate' | 'blue' | 'green' | 'red' | 'amber' | 'purple' | 'cyan';

const toneClasses: Record<Tone, string> = {
    slate: 'bg-slate-100 text-slate-700 ring-slate-200',
    blue: 'bg-sky-100 text-sky-700 ring-sky-200',
    green: 'bg-emerald-100 text-emerald-700 ring-emerald-200',
    red: 'bg-rose-100 text-rose-700 ring-rose-200',
    amber: 'bg-amber-100 text-amber-800 ring-amber-200',
    purple: 'bg-violet-100 text-violet-700 ring-violet-200',
    cyan: 'bg-cyan-100 text-cyan-700 ring-cyan-200',
};

const statusTones: Record<string, Tone> = {
    Pending: 'slate',
    Processing: 'blue',
    Completed: 'green',
    Failed: 'red',
    Enabled: 'green',
    Disabled: 'slate',
    Healthy: 'green',
    Backlog: 'amber',
    Attention: 'red',
    Idle: 'slate',
    Active: 'green',
    Stale: 'red',
};

const numberFormatter = new Intl.NumberFormat(undefined);
const compactNumberFormatter = new Intl.NumberFormat(undefined, {
    notation: 'compact',
    maximumFractionDigits: 1,
});

export function cx(...classes: Array<string | false | null | undefined>): string {
    return classes.filter(Boolean).join(' ');
}

export function formatNumber(value: number, compact = false): string {
    return (compact ? compactNumberFormatter : numberFormatter).format(value);
}

export function PageHeader({
    eyebrow,
    title,
    description,
    actions,
}: {
    eyebrow?: string;
    title: string;
    description?: string;
    actions?: ReactNode;
}) {
    return (
        <div className="relative overflow-hidden rounded-[2rem] border border-white/10 bg-gradient-to-br from-slate-950 via-slate-900 to-sky-950 p-6 text-white shadow-2xl shadow-slate-950/25 ring-1 ring-white/10 md:flex md:items-end md:justify-between md:gap-4">
            <div className="pointer-events-none absolute -right-24 -top-24 h-56 w-56 rounded-full bg-sky-400/20 blur-3xl" />
            <div className="pointer-events-none absolute -bottom-32 left-1/3 h-56 w-56 rounded-full bg-emerald-400/10 blur-3xl" />
            <div className="max-w-3xl">
                {eyebrow && <p className="text-xs font-semibold uppercase tracking-[0.28em] text-sky-300">{eyebrow}</p>}
                <h1 className="mt-2 text-3xl font-semibold tracking-tight text-white md:text-4xl">{title}</h1>
                {description && <p className="mt-3 text-sm leading-6 text-slate-300 md:text-base">{description}</p>}
            </div>
            {actions && <div className="relative mt-5 flex flex-wrap items-center gap-3 md:mt-0">{actions}</div>}
        </div>
    );
}

export function Panel({ children, className }: { children: ReactNode; className?: string }) {
    return (
        <section
            className={cx(
                'overflow-hidden rounded-3xl border border-slate-200/80 bg-white shadow-xl shadow-slate-950/10 ring-1 ring-white/80',
                className,
            )}
        >
            {children}
        </section>
    );
}

export function MetricCard({
    label,
    value,
    helper,
    tone = 'slate',
}: {
    label: string;
    value: ReactNode;
    helper?: ReactNode;
    tone?: Tone;
}) {
    const accentClasses: Record<Tone, string> = {
        slate: 'from-slate-400 to-slate-600',
        blue: 'from-sky-400 to-blue-600',
        green: 'from-emerald-400 to-teal-600',
        red: 'from-rose-400 to-red-600',
        amber: 'from-amber-300 to-orange-500',
        purple: 'from-violet-400 to-fuchsia-600',
        cyan: 'from-cyan-300 to-sky-500',
    };

    return (
        <div className="relative overflow-hidden rounded-3xl border border-slate-200 bg-gradient-to-br from-white via-white to-sky-50/60 p-5 shadow-xl shadow-slate-950/10 ring-1 ring-white">
            <div className={cx('absolute inset-x-0 top-0 h-1.5 bg-gradient-to-r', accentClasses[tone])} />
            <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">{label}</p>
            <div className="mt-3 text-3xl font-semibold tracking-tight text-slate-950">{value}</div>
            {helper && <div className="mt-2 text-sm font-medium text-slate-600">{helper}</div>}
        </div>
    );
}

export function StatusPill({ status, tone, className }: { status: string; tone?: Tone; className?: string }) {
    return (
        <span
            className={cx(
                'inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ring-inset',
                toneClasses[tone ?? statusTones[status] ?? 'slate'],
                className,
            )}
        >
            <span className="mr-1.5 h-1.5 w-1.5 rounded-full bg-current opacity-70" />
            {status}
        </span>
    );
}

export function RelativeTime({
    value,
    now,
    fallback = '—',
    className,
}: {
    value: string | number | Date | null | undefined;
    now?: number;
    fallback?: string;
    className?: string;
}) {
    if (!value) {
        return <span className={className}>{fallback}</span>;
    }

    const timestamp = new Date(value).getTime();
    const dateTime = Number.isNaN(timestamp) ? undefined : new Date(timestamp).toISOString();

    return (
        <time className={className} dateTime={dateTime} title={formatAbsoluteDateTime(value)}>
            {formatRelativeTime(value, now)}
        </time>
    );
}

export function EmptyState({ title, description }: { title: string; description: string }) {
    return (
        <div className="py-16 text-center">
            <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-400">
                —
            </div>
            <h3 className="mt-4 text-sm font-semibold text-slate-900">{title}</h3>
            <p className="mt-1 text-sm text-slate-500">{description}</p>
        </div>
    );
}
