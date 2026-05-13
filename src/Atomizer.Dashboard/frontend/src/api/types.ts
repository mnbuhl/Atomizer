export interface PagedResponse<T> {
    items: T[];
    totalCount: number;
    skip: number;
    take: number;
}

export interface JobDto {
    id: string;
    queueKey: string;
    payloadTypeName: string;
    status: 'Pending' | 'Processing' | 'Completed' | 'Failed';
    attempts: number;
    createdAt: string;
    scheduledAt: string | null;
    completedAt: string | null;
    failedAt: string | null;
    partitionKey: string | null;
    sequenceNumber: number | null;
}

export interface JobDetailDto extends JobDto {
    payload: string | null;
    errors: JobErrorDto[];
}

export interface JobErrorDto {
    attempt: number;
    exceptionType: string;
    message: string;
    stackTrace: string | null;
    occurredAt: string;
    runtimeIdentity: string | null;
}

export interface ScheduleDto {
    id: string;
    jobKey: string;
    queueKey: string;
    payloadTypeName: string;
    cron: string;
    nextRunAt: string | null;
    lastRunAt: string | null;
    enabled: boolean;
    misfirePolicy: string;
}

export interface QueueStatsResponse {
    queues: QueueStatsDto[];
}

export interface QueueStatsDto {
    queueKey: string;
    pending: number;
    processing: number;
    completed: number;
    failed: number;
}

export interface ServerDto {
    instanceId: string;
    lastHeartbeatAt: string;
    ageSeconds: number;
}
