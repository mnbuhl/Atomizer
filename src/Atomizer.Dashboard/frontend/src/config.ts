const meta = document.querySelector<HTMLMetaElement>('meta[name="atomizer-config"]');

const templateTokenPattern = /^\{\{[A-Z0-9_]+\}\}$/;

function readString(value: string | undefined, fallback: string): string {
    const trimmed = value?.trim();
    if (!trimmed || templateTokenPattern.test(trimmed)) return fallback;

    return trimmed;
}

function readNumber(value: string | undefined, fallback: number): number {
    const parsed = Number.parseInt(readString(value, fallback.toString()), 10);

    return Number.isFinite(parsed) ? parsed : fallback;
}

function normalizeRoutePrefix(value: string): string {
    const trimmed = value.trim();
    if (!trimmed || trimmed === '/') return '';

    return `/${trimmed.replace(/^\/+|\/+$/g, '')}`;
}

export const routePrefix: string = normalizeRoutePrefix(
    readString(meta?.dataset.routePrefix, import.meta.env.VITE_ATOMIZER_ROUTE_PREFIX ?? '/atomizer'),
);
export const title: string = readString(
    meta?.dataset.title,
    import.meta.env.VITE_ATOMIZER_DASHBOARD_TITLE ?? 'Atomizer Dashboard',
);
export const statsRefreshMs: number = readNumber(meta?.dataset.statsRefreshMs, 5000);
export const jobsRefreshMs: number = readNumber(meta?.dataset.jobsRefreshMs, 30000);
