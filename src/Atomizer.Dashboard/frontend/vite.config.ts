import { defineConfig, loadEnv, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

interface DashboardDevConfig {
  routePrefix: string;
  title: string;
  statsRefreshMs: string;
  jobsRefreshMs: string;
}

function readEnv(value: string | undefined, fallback: string): string {
  const trimmed = value?.trim();

  return trimmed ? trimmed : fallback;
}

function normalizeRoutePrefix(value: string): string {
  const trimmed = value.trim();
  if (!trimmed || trimmed === '/') return '';

  return `/${trimmed.replace(/^\/+|\/+$/g, '')}`;
}

function dashboardDevConfigPlugin(config: DashboardDevConfig): Plugin {
  return {
    name: 'atomizer-dashboard-dev-config',
    apply: 'serve',
    transformIndexHtml(html) {
      return html
        .replaceAll('{{ROUTE_PREFIX}}', config.routePrefix)
        .replaceAll('{{TITLE}}', config.title)
        .replaceAll('{{STATS_REFRESH_MS}}', config.statsRefreshMs)
        .replaceAll('{{JOBS_REFRESH_MS}}', config.jobsRefreshMs);
    },
  };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'VITE_');
  const routePrefix = normalizeRoutePrefix(readEnv(env.VITE_ATOMIZER_ROUTE_PREFIX, '/atomizer'));
  const serverUrl = readEnv(env.VITE_ATOMIZER_SERVER_URL, 'http://localhost:5052').replace(/\/+$/g, '');
  const title = readEnv(env.VITE_ATOMIZER_DASHBOARD_TITLE, 'Atomizer Dashboard');
  const statsRefreshMs = readEnv(env.VITE_ATOMIZER_STATS_REFRESH_MS, '5000');
  const jobsRefreshMs = readEnv(env.VITE_ATOMIZER_JOBS_REFRESH_MS, '30000');
  const apiPrefix = `${routePrefix}/api`;

  return {
    plugins: [
      dashboardDevConfigPlugin({
        routePrefix,
        title,
        statsRefreshMs,
        jobsRefreshMs,
      }),
      react(),
      tailwindcss(),
    ],
    base: './',
    server: {
      proxy: {
        [apiPrefix]: {
          target: serverUrl,
          changeOrigin: true,
          secure: false,
        },
      },
    },
    build: {
      outDir: 'dist',
      emptyOutDir: true,
    },
  };
});
