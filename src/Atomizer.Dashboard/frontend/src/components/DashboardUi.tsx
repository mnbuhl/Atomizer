import type { ReactNode } from 'react';
import { useEffect } from 'react';
import { formatAbsoluteDateTime, formatRelativeTime } from '../utils/time';

type Tone = 'slate' | 'blue' | 'green' | 'red' | 'amber' | 'purple' | 'cyan' | 'orange';

const toneClasses: Record<Tone, string> = {
    slate: 'status-pill--slate',
    blue: 'status-pill--blue',
    green: 'status-pill--green',
    red: 'status-pill--red',
    amber: 'status-pill--amber',
    purple: 'status-pill--purple',
    cyan: 'status-pill--cyan',
    orange: 'status-pill--orange',
};

const statusTones: Record<string, Tone> = {
    Pending: 'slate',
    Processing: 'blue',
    Completed: 'green',
    Failed: 'red',
    Cancelled: 'orange',
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

export const ui = {
    toolbarPill: 'toolbar-pill rounded-2xl px-3 py-2 text-xs font-medium',
    primaryButton:
        'button-primary rounded-2xl px-4 py-2 text-sm font-semibold transition hover:-translate-y-0.5 disabled:opacity-60',
    secondaryButton: 'button-secondary rounded-2xl border px-4 py-2 text-sm font-semibold transition',
    smallButton: 'button-secondary rounded-xl border px-3 py-2 font-semibold transition disabled:cursor-not-allowed disabled:opacity-40',
    panelHeading: 'panel-heading p-5',
    panelHeadingAccent: 'panel-heading panel-heading-accent p-5',
    sectionTitle: 'section-title text-base font-semibold',
    sectionDescription: 'text-muted mt-1 text-sm',
    muted: 'text-muted',
    soft: 'text-soft',
    strong: 'text-strong',
    defaultText: 'text-default',
    fieldLabel: 'field-label text-xs font-semibold uppercase tracking-[0.18em]',
    input: 'input-field mt-2 w-full rounded-2xl border px-4 py-3 text-sm outline-none transition',
    filterChip: 'filter-chip rounded-full border px-3 py-1.5 text-xs font-semibold transition',
    filterChipActive: 'filter-chip filter-chip-active rounded-full border px-3 py-1.5 text-xs font-semibold transition',
    tableHeadRow: 'table-head-row border-b text-xs uppercase tracking-[0.16em]',
    tableBody: 'table-body divide-y',
    tableRow: 'table-row transition',
    interactiveTableRow:
        'table-row group cursor-pointer transition focus:outline-none focus:ring-2 focus:ring-inset focus:ring-sky-300',
    softChip: 'soft-chip rounded-full px-2.5 py-1 text-xs font-semibold',
    softChipPurple: 'soft-chip soft-chip-purple rounded-full px-2 py-0.5 font-semibold',
    rowAction: 'row-action mt-1 text-xs font-semibold opacity-0 transition group-hover:opacity-100 group-focus:opacity-100',
    footer: 'table-footer flex flex-col gap-3 border-t px-5 py-4 text-sm sm:flex-row sm:items-center sm:justify-between',
    error: 'danger-text p-8 text-sm font-medium',
};

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
        <div className="page-header relative overflow-hidden rounded-[2rem] border p-6 md:flex md:items-end md:justify-between md:gap-4">
            <div className="max-w-3xl">
                {eyebrow && <p className="page-eyebrow text-xs font-semibold uppercase tracking-[0.28em]">{eyebrow}</p>}
                <h1 className="mt-2 text-3xl font-semibold tracking-tight text-white md:text-4xl">{title}</h1>
                {description && <p className="page-description mt-3 text-sm leading-6 md:text-base">{description}</p>}
            </div>
            {actions && (
                <div className="relative mt-5 flex flex-wrap items-center gap-3 sm:justify-end md:ml-6 md:mt-0 md:shrink-0 md:flex-col md:items-end">
                    {actions}
                </div>
            )}
        </div>
    );
}

export function Panel({ children, className }: { children: ReactNode; className?: string }) {
    return (
        <section
            className={cx(
                'panel-surface overflow-hidden rounded-3xl border',
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
        orange: 'from-orange-300 to-orange-600',
    };

    return (
        <div className="metric-card relative overflow-hidden rounded-3xl border p-5">
            <div className={cx('absolute inset-x-0 top-0 h-1.5 bg-gradient-to-r', accentClasses[tone])} />
            <p className="text-soft text-xs font-bold uppercase tracking-[0.24em]">{label}</p>
            <div className="text-strong mt-3 text-3xl font-semibold tracking-tight">{value}</div>
            {helper && <div className="text-muted mt-2 text-sm font-medium">{helper}</div>}
        </div>
    );
}

export function MetricCardSkeleton() {
    return (
        <div className="metric-card relative overflow-hidden rounded-3xl border p-5">
            <SkeletonBlock className="h-1.5 w-20" />
            <SkeletonBlock className="mt-4 h-9 w-28" />
            <SkeletonBlock className="mt-3 h-4 w-36" />
        </div>
    );
}

export function StatusPill({ status, tone, className }: { status: string; tone?: Tone; className?: string }) {
    return (
        <span
            className={cx(
                'status-pill',
                toneClasses[tone ?? statusTones[status] ?? 'slate'],
                className,
            )}
        >
            <span className="mr-1.5 h-1.5 w-1.5 rounded-full bg-current opacity-70" />
            {status}
        </span>
    );
}

export function ConfirmationDialog({
    open,
    title,
    description,
    confirmLabel,
    onConfirm,
    onCancel,
    confirmTone = 'default',
}: {
    open: boolean;
    title: string;
    description: ReactNode;
    confirmLabel: string;
    onConfirm: () => void;
    onCancel: () => void;
    confirmTone?: 'default' | 'danger';
}) {
    useEffect(() => {
        if (!open) {
            return;
        }

        const handleKeyDown = (event: KeyboardEvent) => {
            if (event.key === 'Escape') {
                onCancel();
            }
        };

        window.addEventListener('keydown', handleKeyDown);
        return () => window.removeEventListener('keydown', handleKeyDown);
    }, [onCancel, open]);

    if (!open) {
        return null;
    }

    return (
        <div
            aria-modal="true"
            className="dialog-backdrop fixed inset-0 z-50 flex items-center justify-center p-4"
            role="dialog"
        >
            <div className="dialog-panel w-full max-w-md rounded-2xl border p-5 shadow-2xl">
                <h2 className="text-strong text-base font-semibold">{title}</h2>
                <div className="text-muted mt-2 text-sm leading-6">{description}</div>
                <div className="mt-5 flex flex-wrap justify-end gap-2">
                    <button type="button" className={ui.secondaryButton} onClick={onCancel}>
                        Keep current state
                    </button>
                    <button
                        type="button"
                        className={confirmTone === 'danger' ? 'danger-button rounded-2xl px-4 py-2 text-sm font-semibold transition' : ui.primaryButton}
                        onClick={onConfirm}
                    >
                        {confirmLabel}
                    </button>
                </div>
            </div>
        </div>
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
            <div className="soft-chip mx-auto flex h-12 w-12 items-center justify-center rounded-2xl text-xl">
                —
            </div>
            <h3 className="text-strong mt-4 text-sm font-semibold">{title}</h3>
            <p className="text-muted mt-1 text-sm">{description}</p>
        </div>
    );
}

export function SkeletonBlock({ className }: { className: string }) {
    return <div aria-hidden="true" className={cx('skeleton-block rounded-full', className)} />;
}

export function TableSkeleton({ columns, rows = 5 }: { columns: number; rows?: number }) {
    return (
        <div className="overflow-x-auto" aria-hidden="true">
            <table className="w-full min-w-[720px] text-left text-sm">
                <thead>
                    <tr className={ui.tableHeadRow}>
                        {Array.from({ length: columns }, (_, index) => (
                            <th key={index} className="px-5 py-4">
                                <SkeletonBlock className="h-3 w-20" />
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody className={ui.tableBody}>
                    {Array.from({ length: rows }, (_, rowIndex) => (
                        <tr key={rowIndex} className={ui.tableRow}>
                            {Array.from({ length: columns }, (_, columnIndex) => (
                                <td key={columnIndex} className="px-5 py-4">
                                    <SkeletonBlock className={columnIndex === 0 ? 'h-4 w-36' : 'h-4 w-24'} />
                                    {columnIndex === 0 && <SkeletonBlock className="mt-2 h-3 w-24" />}
                                </td>
                            ))}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}
