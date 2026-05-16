import { apiRequestHeaders, routePrefix } from '../config';
import type {
    PagedResponse,
    JobDto,
    JobDetailDto,
    ScheduleDto,
    QueueStatsResponse,
    ServerDto,
    JobTypeOption,
    TriggerJobRequest,
    JobActionResponse,
    ScheduleActionResponse,
} from './types';

const baseUrl = `${window.location.origin}${routePrefix}/api`;

function buildHeaders(initHeaders?: HeadersInit): Headers {
    const headers = new Headers({ Accept: 'application/json' });

    Object.entries(apiRequestHeaders).forEach(([name, value]) => headers.set(name, value));
    new Headers(initHeaders).forEach((value, name) => headers.set(name, value));

    return headers;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
    const res = await fetch(`${baseUrl}${path}`, {
        ...init,
        credentials: init?.credentials ?? 'same-origin',
        headers: buildHeaders(init?.headers),
    });
    if (!res.ok) throw new Error(await readErrorMessage(res, path));
    return res.json() as Promise<T>;
}

async function readErrorMessage(res: Response, path: string): Promise<string> {
    const fallback = `${res.status} ${res.statusText} — ${path}`;
    try {
        const contentType = res.headers.get('content-type') ?? '';
        if (contentType.includes('application/json')) {
            const body = (await res.json()) as { error?: unknown };
            return typeof body.error === 'string' && body.error.length > 0 ? body.error : fallback;
        }

        const text = await res.text();
        return text.trim().length > 0 ? text.trim() : fallback;
    } catch {
        return fallback;
    }
}

function buildQuery(params: Record<string, string | string[] | number | undefined>): string {
    const parts: string[] = [];
    for (const [key, value] of Object.entries(params)) {
        if (value === undefined) continue;
        if (Array.isArray(value)) {
            value.forEach(v => parts.push(`${key}=${encodeURIComponent(v)}`));
        } else {
            parts.push(`${key}=${encodeURIComponent(value)}`);
        }
    }
    return parts.join('&');
}

export const api = {
    getJobs: (params: Record<string, string | string[] | number | undefined>) => {
        const qs = buildQuery(params);
        return request<PagedResponse<JobDto>>(`/jobs${qs ? `?${qs}` : ''}`);
    },
    getJob: (id: string) => request<JobDetailDto>(`/jobs/${id}`),
    getJobTypes: () => request<JobTypeOption[]>('/job-types'),
    triggerJob: (body: TriggerJobRequest) =>
        request<JobActionResponse>('/jobs/trigger', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        }),
    retryJob: (id: string) => request<JobActionResponse>(`/jobs/${id}/retry`, { method: 'POST' }),
    cancelJob: (id: string) => request<JobActionResponse>(`/jobs/${id}/cancel`, { method: 'POST' }),
    getSchedules: () => request<ScheduleDto[]>('/schedules'),
    setScheduleEnabled: (id: string, enabled: boolean) =>
        request<ScheduleActionResponse>(`/schedules/${id}/enabled`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ enabled }),
        }),
    runScheduleNow: (id: string) => request<JobActionResponse>(`/schedules/${id}/run-now`, { method: 'POST' }),
    getQueueStats: () => request<QueueStatsResponse>('/queues/stats'),
    getServers: () => request<ServerDto[]>('/servers'),
};
