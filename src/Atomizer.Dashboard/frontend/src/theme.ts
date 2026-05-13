export type ThemeMode = 'dark' | 'light';

export const THEME_STORAGE_KEY = 'atomizer-dashboard-theme';

export function readStoredTheme(): ThemeMode {
    try {
        return window.localStorage.getItem(THEME_STORAGE_KEY) === 'light' ? 'light' : 'dark';
    } catch {
        return 'dark';
    }
}

export function applyTheme(theme: ThemeMode): void {
    document.documentElement.dataset.theme = theme;
}

export function writeStoredTheme(theme: ThemeMode): void {
    try {
        window.localStorage.setItem(THEME_STORAGE_KEY, theme);
    } catch {
        // Storage can be unavailable in private or embedded contexts. The in-memory theme still applies.
    }
}
