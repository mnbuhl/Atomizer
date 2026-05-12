const meta = document.querySelector<HTMLMetaElement>('meta[name="atomizer-config"]');

export const routePrefix: string = meta?.dataset.routePrefix ?? '/atomizer';
export const title: string = meta?.dataset.title ?? 'Atomizer Dashboard';
export const statsRefreshMs: number = parseInt(meta?.dataset.statsRefreshMs ?? '5000', 10);
export const jobsRefreshMs: number = parseInt(meta?.dataset.jobsRefreshMs ?? '30000', 10);
