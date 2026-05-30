# Prague website & docs

Static site for Prague (`Prague`), built from two pieces and glued together at deploy time:

| Path | Stack | Output |
| --- | --- | --- |
| `www/site/` | Vite + React 19 + Tailwind v4 + motion + lucide-react | `www/site/dist/` |
| `www/docs/` | DocFX (modern template + brand CSS override) | `www/docs/_site/` |

Deployed to GitHub Pages from `.github/workflows/pages.yml`, **manually triggered** (`workflow_dispatch`).

The Pages URL is `https://<owner>.github.io/internal-be-kafka-cache-lib/` — the landing lives at the root, the docs at `/docs/`. The Vite app sets its `base` to `/internal-be-kafka-cache-lib/` at build time so all asset paths resolve correctly under the project-page subpath.

## Build locally

```bash
# Landing
cd www/site
npm install
npm run dev       # http://localhost:5173

# Docs (requires .NET 9 SDK)
dotnet tool install -g docfx     # one-time
cd www/docs
docfx docfx.json --serve         # http://localhost:8080
```

## Build everything (mirrors the CI pipeline)

```bash
(cd www/site && npm ci && npm run build)
(cd www/docs && docfx docfx.json)
rm -rf www/_dist && mkdir -p www/_dist/docs
cp -R www/site/dist/. www/_dist/
cp -R www/docs/_site/. www/_dist/docs/
```

Open `www/_dist/index.html` to preview the assembled output.

## Deploy

1. Make sure **Settings → Pages → Source** is set to **"GitHub Actions"** (one-time).
2. Go to **Actions → Build and deploy Prague website → Run workflow**.

## Brand assets

Logo PNGs live in `www/site/src/assets/images/`. The DocFX brand override (`www/docs/templates/prague/public/main.css`) re-uses the same color tokens (`--brand-orange #F27D26`, `--brand-black #141414`) and fonts so the two halves of the site feel like one.

## Adding a doc article

1. Create the Markdown file in `www/docs/articles/`.
2. Add an entry to `www/docs/articles/toc.yml`.
3. Rebuild (`docfx docfx.json --serve`).

## Editing the landing

Edit `www/site/src/App.tsx`. `import.meta.env.BASE_URL` resolves to `/` in dev and `/internal-be-kafka-cache-lib/` in production builds — use it for any cross-page links (e.g. `${BASE_URL}docs/`).
