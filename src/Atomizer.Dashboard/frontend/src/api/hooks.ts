import { useQueries, useQuery } from '@tanstack/react-query';
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

const jobStatuses = ['Pending', 'Processing', 'Completed', 'Failed'] as const;
export type JobStatus = (typeof jobStatuses)[number];

export function useJobs(filters: JobFilters = {}) {
    return useQuery<PagedResponse<JobDto>>({
        queryKey: ['jobs', filters],
        queryFn: () => api.getJobs(filters as Record<string, string | string[] | number | undefined>),
        refetchInterval: jobsRefreshMs,
    });
}

export function useJobStatusCounts(filters: JobFilters = {}) {
    const { skip: _skip, take: _take, status: _status, ...countFilters } = filters;

    return useQueries({
        queries: jobStatuses.map(status => ({
            queryKey: ['jobs', 'count', status, countFilters],
            queryFn: () =>
                api.getJobs({
                    ...countFilters,
                    status: [status],
                    skip: 0,
                    take: 0,
            }),
            staleTime: 5_000,
            refetchInterval: jobsRefreshMs,
        })),
        combine: results => ({
            counts: Object.fromEntries(
                jobStatuses.map((status, index) => [status, results[index]?.data?.totalCount ?? 0]),
            ) as Record<JobStatus, number>,
            isLoading: results.some(result => result.isLoading),
            isFetching: results.some(result => result.isFetching),
            error: results.find(result => result.error)?.error,
        }),
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
