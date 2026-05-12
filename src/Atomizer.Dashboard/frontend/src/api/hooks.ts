import { useQuery } from '@tanstack/react-query';
import { api } from './client';
import { statsRefreshMs, jobsRefreshMs } from '../config';
import type { JobDto, JobDetailDto, ScheduleDto, QueueStatsResponse, ServerDto, PagedResponse } from './types';

export interface JobFilters {
    status?: string[];
    queue?: string;
    payload?: string;
    from?: string;
    to?: string;
    skip?: number;
    take?: number;
}

export function useJobs(filters: JobFilters = {}) {
    return useQuery<PagedResponse<JobDto>>({
        queryKey: ['jobs', filters],
        queryFn: () => api.getJobs(filters as Record<string, string | string[] | number | undefined>),
        refetchInterval: jobsRefreshMs,
    });
}

export function useJob(id: string) {
    return useQuery<JobDetailDto>({
        queryKey: ['job', id],
        queryFn: () => api.getJob(id),
        enabled: !!id,
    });
}

export function useSchedules() {
    return useQuery<ScheduleDto[]>({
        queryKey: ['schedules'],
        queryFn: api.getSchedules,
    });
}

export function useQueueStats() {
    return useQuery<QueueStatsResponse>({
        queryKey: ['queueStats'],
        queryFn: api.getQueueStats,
        refetchInterval: statsRefreshMs,
    });
}

export function useServers() {
    return useQuery<ServerDto[]>({
        queryKey: ['servers'],
        queryFn: api.getServers,
        refetchInterval: 5000,
    });
}
