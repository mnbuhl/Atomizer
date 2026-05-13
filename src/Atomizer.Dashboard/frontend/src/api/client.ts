import { routePrefix } from '../config';
import type { PagedResponse, JobDto, JobDetailDto, ScheduleDto, QueueStatsResponse, ServerDto } from './types';

const baseUrl = `${window.location.origin}${routePrefix}/api`;

async function request<T>(path: string, init?: RequestInit): Promise<T> {
    const res = await fetch(`${baseUrl}${path}`, {
        headers: { Accept: 'application/json', ...init?.headers },
        ...init,
    });
    if (!res.ok) throw new Error(`${res.status} ${res.statusText} — ${path}`);
    return res.json() as Promise<T>;
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
    getSchedules: () => request<ScheduleDto[]>('/schedules'),
    getQueueStats: () => request<QueueStatsResponse>('/queues/stats'),
    getServers: () => request<ServerDto[]>('/servers'),
};
