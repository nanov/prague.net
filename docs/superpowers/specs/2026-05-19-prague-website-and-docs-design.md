# Prague website & docs — design

## Goal

Stand up a static marketing site and reference/conceptual docs for **Prague**
(`Prague`), deployable to GitHub Pages from this repo.

## Decisions

| Topic | Choice |
| --- | --- |
| Site structure | Two builds glued at deploy: Vite landing at `/`, DocFX docs at `/docs/` |
| Hosting | GitHub Pages, **project page** on this repo → base path `/internal-be-kafka-cache-lib/` |
| DocFX theme | Stock `modern` template + brand CSS override (`main.css`) |
| Docs content | Auto-generated API reference + small conceptual set (Getting Started, Defining a Cache, Querying & Joins, Kafka Integration) |
| CI/CD | GitHub Actions, **manual trigger only** (`workflow_dispatch`) |
| Landing source | Reuse the Vite/React/Tailwind v4 mockup in `~/Downloads/prague-framework-branding`, stripped of AI Studio scaffolding |

## Layout

```
www/
  site/                       # Vite landing (React 19 + Tailwind v4 + motion + lucide-react)
    package.json
    vite.config.ts            # base: '/internal-be-kafka-cache-lib/'
    index.html
    src/
      main.tsx
      App.tsx                 # cleaned: Docs nav links to /docs/, no view switcher
      index.css               # brand tokens (orange/black/grid-bg, Inter+JetBrains Mono)
      assets/images/          # Prague logos
  docs/                       # DocFX project
    docfx.json
    index.md                  # docs landing
    toc.yml
    articles/
      getting-started.md
      defining-a-cache.md
      querying-and-joins.md
      kafka-integration.md
      toc.yml
    api/                      # generated YAML (gitignored)
    templates/
      prague/
        public/main.css       # brand override on top of modern template
  README.md                   # local build instructions
  .gitignore                  # node_modules, dist, _site, api/*.yml
.github/workflows/
  pages.yml                   # workflow_dispatch only
```

### Cleanup applied to the branding mockup

- Drop `@google/genai`, `express`, `dotenv`, `tsx` deps and the AI Studio README.
- Delete the React `Docs.tsx` mock view; replace the in-app view switcher with an external `<a href="/internal-be-kafka-cache-lib/docs/">Docs</a>` link.
- Switch image imports from absolute `/src/assets/...` to Vite-resolved imports so the project-page base path applies automatically.
- Replace placeholder GitHub link with the actual repo URL.

### DocFX metadata source

`docfx.json` `metadata.src` points at the projects listed in `Prague.Publish.slnf`:

- `Prague`
- `Prague.Core`
- `Prague.Kafka`
- `Prague.Attributes`
- `Prague.Codegen`

`<GenerateDocumentationFile>` is not currently set in `Directory.Build.props`; DocFX reads XML doc comments straight from the `.csproj` outputs, so we'll opt in per-project via DocFX's `src.files` glob (no global property change needed).

### Brand override

`templates/prague/public/main.css` injects:
- Color tokens: `--brand-orange: #F27D26`, `--brand-black: #141414`, `--brand-gray: #E4E3E0`
- Fonts: Inter, JetBrains Mono
- Nav logo (small inverted Prague mark)
- Accent on links, headers, and the active TOC item

Applied on top of `modern` template — no template forking, no Mustache edits.

### CI/CD

`.github/workflows/pages.yml`:

- `on: workflow_dispatch:` (manual only)
- jobs.build: checkout → setup-node 22 → `npm ci && npm run build` in `www/site` → setup-dotnet 9 → `dotnet tool install -g docfx` → `docfx www/docs/docfx.json` → assemble `_site/` (Vite output at root, DocFX output at `/docs/`) → `actions/upload-pages-artifact`
- jobs.deploy: `actions/deploy-pages` with `permissions: pages: write, id-token: write`

GitHub Pages source must be set to "GitHub Actions" in repo settings (one-time, manual).

## Out of scope

- Custom domain / DNS
- Search beyond DocFX's built-in Lunr index
- Versioned docs (multi-release switcher)
- Analytics, comments, edit-on-GitHub buttons (can add later)
- Conceptual docs beyond the four seed articles
