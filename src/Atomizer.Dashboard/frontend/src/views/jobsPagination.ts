const pageWindowFormatter = new Intl.NumberFormat(undefined);

export interface JobPageWindow {
    start: number;
    end: number;
    label: string;
}

export function getJobPageWindow(skip: number, visibleCount: number, totalCount: number): JobPageWindow {
    const normalizedSkip = normalizeCount(skip);
    const normalizedVisibleCount = normalizeCount(visibleCount);
    const normalizedTotalCount = normalizeCount(totalCount);

    if (
        normalizedTotalCount === 0 ||
        normalizedVisibleCount === 0 ||
        normalizedSkip >= normalizedTotalCount
    ) {
        return {
            start: 0,
            end: 0,
            label: '0',
        };
    }

    const start = normalizedSkip + 1;
    const end = Math.min(normalizedSkip + normalizedVisibleCount, normalizedTotalCount);

    return {
        start,
        end,
        label:
            start === end
                ? pageWindowFormatter.format(start)
                : `${pageWindowFormatter.format(start)}–${pageWindowFormatter.format(end)}`,
    };
}

function normalizeCount(value: number): number {
    return Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;
}
