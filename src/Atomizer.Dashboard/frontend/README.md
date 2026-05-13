# Atomizer Dashboard frontend

## Local API-backed development

Start the Atomizer server locally with the dashboard mapped, for example at:

```bash
http://localhost:5052/atomizer
```

Then run the Vite dev server from this directory:

```bash
npm run dev
```

Open `http://localhost:5173/atomizer`. During Vite development, `/atomizer/api/*` is proxied to
`http://localhost:5052` by default, so the browser does not need CORS enabled on the Atomizer server.

Override the backend URL or dashboard route prefix when needed:

```bash
VITE_ATOMIZER_SERVER_URL=http://localhost:5052 npm run dev
VITE_ATOMIZER_ROUTE_PREFIX=/custom-atomizer npm run dev
```
