const relativeTimeFormatter = new Intl.RelativeTimeFormat(undefined, {
    numeric: 'auto',
    style: 'short',
});

const absoluteDateTimeFormatter = new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'medium',
});

const units: Array<{ unit: Intl.RelativeTimeFormatUnit; seconds: number }> = [
    { unit: 'year', seconds: 365 * 24 * 60 * 60 },
    { unit: 'month', seconds: 30 * 24 * 60 * 60 },
    { unit: 'week', seconds: 7 * 24 * 60 * 60 },
    { unit: 'day', seconds: 24 * 60 * 60 },
    { unit: 'hour', seconds: 60 * 60 },
    { unit: 'minute', seconds: 60 },
];

export function formatRelativeTime(value: string | number | Date | null | undefined, now = Date.now()): string {
    const timestamp = getTimestamp(value);
    if (timestamp === null) {
        return '—';
    }

    const diffSeconds = Math.round((timestamp - now) / 1000);
    const absSeconds = Math.abs(diffSeconds);

    if (absSeconds < 2) {
        return 'just now';
    }

    if (absSeconds < 60) {
        return diffSeconds < 0 ? `${absSeconds}s ago` : `in ${absSeconds}s`;
    }

    for (const { unit, seconds } of units) {
        if (absSeconds >= seconds || unit === 'minute') {
            return relativeTimeFormatter.format(Math.round(diffSeconds / seconds), unit);
        }
    }

    return 'just now';
}

export function formatAbsoluteDateTime(value: string | number | Date | null | undefined): string {
    const timestamp = getTimestamp(value);
    return timestamp === null ? 'Unknown time' : absoluteDateTimeFormatter.format(new Date(timestamp));
}

export function formatDuration(totalSeconds: number): string {
    if (!Number.isFinite(totalSeconds) || totalSeconds < 0) {
        return '—';
    }

    if (totalSeconds < 60) {
        return `${Math.floor(totalSeconds)}s`;
    }

    const minutes = Math.floor(totalSeconds / 60);
    if (minutes < 60) {
        return `${minutes}m`;
    }

    const hours = Math.floor(minutes / 60);
    const remainingMinutes = minutes % 60;
    if (hours < 24) {
        return remainingMinutes === 0 ? `${hours}h` : `${hours}h ${remainingMinutes}m`;
    }

    const days = Math.floor(hours / 24);
    const remainingHours = hours % 24;
    return remainingHours === 0 ? `${days}d` : `${days}d ${remainingHours}h`;
}

function getTimestamp(value: string | number | Date | null | undefined): number | null {
    if (value === null || value === undefined || value === '') {
        return null;
    }

    const timestamp = value instanceof Date ? value.getTime() : new Date(value).getTime();
    return Number.isNaN(timestamp) ? null : timestamp;
}
