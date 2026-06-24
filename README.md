# CardExporter

![MTGO](https://img.shields.io/badge/dynamic/json.svg?logo=data:image/png;base64,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&labelColor=3f4551&color=da460e&label=MTGO&query=$.version&url=https://raw.githubusercontent.com/videre-project/CardExporter/main/manifests/mtgo-version.json)

CardExporter keeps Videre's MTGO card database and CDN assets in sync with Magic: The Gathering Online by extracting the MTGO client's local card data into PostgreSQL, card/product renders, and generated files for Cloudflare R2.

## Overview

MTGO distributes the data CardExporter needs across several sources:

- card XML in the ClickOnce data directory;
- runtime client models used to fill gaps in set metadata;
- validation-rule files that describe format legalities; and
- WPF resources and client assemblies that contain symbols and other image assets.

CardExporter turns those sources into three forms of operating state:

1. **PostgreSQL records** for cards, sets, products, faces, and legalities.
2. **Pending image work** for new or changed card and product catalog IDs.
3. **Manifest files** that track MTGO source inputs and generated CDN objects.

Each scheduled import first fetches Videre's MTGO version manifest and compares its `codebase` field with `manifests/mtgo-version.json`. When the codebase is unchanged, the run returns before checking local MTGO files, PostgreSQL, image work, or client assets. When it is new, changed, or temporarily unavailable, CardExporter falls back to the local manifests and database checks below.

The normal flow is:

```text
MTGO source files ──> import ──> PostgreSQL
                          │             │
                          │             └─ new or changed image catalog IDs
                          └─ source-file manifest

catalog IDs ──> sync-images ──> Cloudflare R2 ──> CDN manifest

client assemblies ──> sync-assets ──> Cloudflare R2 ──> CDN manifest
```

`manifests/mtgo-source-files.xml` controls incremental import work:

- card-data changes trigger card, set, and product imports;
- validation-rule changes update legalities without requiring a full card import; and
- unchanged tracked inputs do not rewrite the database.

After an import identifies new or changed catalog IDs, image synchronization renders those IDs through the MTGO client. Successful uploads update CDN tracking. Failed uploads do not roll back the database import; the affected image work remains pending for a later `sync-images` run.

## Requirements

- Docker with Docker Compose
- A valid MTGO account for commands that log into the client, render images, or read live runtime metadata
- Cloudflare R2 credentials for commands that upload files or list bucket contents

The container provides Wine, Xvfb, and the Windows .NET runtime required by the MTGO and WPF code paths. Linux `dotnet` can build the solution, but commands that load WPF resources or interact with MTGO should run through the image's `wine-run` alias.

## Quick start

### 1. Configure the environment

Create `.env` in the repository root using the settings described in [Configuration](#configuration).

### 2. Build the solution

```sh
docker compose --profile import run --rm cardexporter \
  dotnet build Project.slnx
```

### 3. Preview the scheduled run

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj import --sync-images --dry-run
```

The dry run checks the tracked inputs and reports planned work without changing PostgreSQL, R2, or either manifest.

### 4. Run the scheduled import and synchronization flow

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj import --sync-images
```

## Configuration

Create a `.env` file in the repository root.

### Credentials and service endpoints

```env
# MTGO login. Required only by commands that log into the client.
USERNAME=your_mtgo_username
PASSWORD=your_mtgo_password

# PostgreSQL
CARDEXPORTER_DATABASE_URL=Host=carddata-postgres;Port=5432;Database=cardexporter;Username=cardexporter;Password=cardexporter

# Cloudflare R2. Required by upload and bucket-reconciliation commands.
CF_S3_Access_Key_ID=...
CF_S3_Secret_Access_Key=...
R2_BUCKET_NAME=mtgo-cdn
R2_ENDPOINT_URL=https://<account-id>.r2.cloudflarestorage.com
R2_PUBLIC_BASE_URL=https://r2.videreproject.com
```

### Optional path and output overrides

The Docker setup already supplies the default paths. Override them only when running against a different filesystem layout.

```env
CARDEXPORTER_SOURCE_MANIFEST_ROOT=/workspace/manifests
CARDEXPORTER_MTGO_VERSION_MANIFEST_URL=https://api.videreproject.com/mtgo/manifest
CARDEXPORTER_CDN_MANIFEST=/workspace/manifests/mtgo-cdn.csv
CARDEXPORTER_MTGO_APP_DIR=/path/to/MTGO/app
CARDEXPORTER_MTGO_CACHE_ROOT=/path/to/MTGO/card/cache

EXPORT_OUTPUT_ROOT=/workspace/output
EXPORT_CARD_HEIGHT=300
CARDEXPORTER_ASSET_OUTPUT_ROOT=/workspace/output/assets
```

## Commands

| Command | Use it for | Primary side effects |
|---|---|---|
| `import` | Incremental card, set, product, and legality imports | PostgreSQL and source tracking; optional image/CDN work with `--sync-images` |
| `sync-images` | Rendering and uploading missing or pending card/product images | Local render output, R2, CDN tracking, and image-work state |
| `export-images` | Rendering selected card/product images for inspection | Local files only |
| `r2-manifest` | Rebuilding the local CDN manifest from the bucket | `manifests/mtgo-cdn.csv` |
| `r2-upload` | Uploading already-rendered local PNGs | R2 and CDN tracking |
| `sync-assets` | Extracting and publishing MTGO client assets | Generated asset files, R2, and CDN tracking |
| `export-mtgo-assets` | Extracting MTGO client assets without publishing them | Local files only |
| `inspect` | Investigating MTGO source data and parser behavior | Diagnostic output |

### `import`

Import changed MTGO data into PostgreSQL.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj import
```

The source-file manifest determines which import stages need to run. Add `--sync-images` when the same invocation should also process pending card/product image work and client-asset synchronization.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj import --sync-images
```

Add `--dry-run` to report detected changes and planned work without mutating PostgreSQL, R2, or the manifest files.

### `sync-images`

Render and upload card or product catalog IDs that PostgreSQL expects but the CDN manifest does not yet track.

Use this command for:

- an initial image backfill;
- retrying uploads after a partial failure; or
- processing image work independently from a card-data import.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj sync-images
```

Add `--dry-run` to list the missing IDs without rendering or uploading them.

### `export-images`

Render card or product images to disk without uploading them. This is useful for checking output quality before a sync.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj export-images --catalog-ids 57140
```

Common filters and overrides:

```text
--catalog-ids 57140
--set SOS
--card-height 300
--output-root output
```

### `r2-manifest`

List the R2 bucket and rebuild `manifests/mtgo-cdn.csv` from object metadata.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj r2-manifest
```

This is a reconciliation and recovery command. Use it after a manual bucket migration or when the local CDN manifest must be reconstructed from R2. It does not render or upload files.

### `r2-upload`

Upload local PNGs from `--output-root`. The CDN manifest is updated only for successful uploads.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj r2-upload --output-root output
```

Use `r2-upload` when files have already been rendered locally and should be published without running the full image-synchronization flow.

### `sync-assets`

Extract MTGO client assets and upload generated files whose SHA-256 differs from CDN tracking.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj sync-assets
```

The command publishes these prefixes:

```text
card-counters/
mana-symbols/
player-counters/
set-symbols/
```

### `export-mtgo-assets`

Extract MTGO symbols and client image assets to disk without uploading them.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj export-mtgo-assets
```

Files are written under `output/assets/` unless `--output-root` or `CARDEXPORTER_ASSET_OUTPUT_ROOT` selects another directory.

### `inspect`

Inspect the MTGO data directory while developing parsers or investigating individual source records.

```sh
docker compose --profile import run --rm cardexporter \
  wine-run CardExporter/CardExporter.csproj inspect --find-catalog-id 57140
```

## State and recovery

CardExporter uses separate state for source detection, database work, and CDN publication.

### Source-file manifest

`manifests/mtgo-source-files.xml` records tracked MTGO inputs by size, timestamp, and SHA-256. It is used to determine which import or asset stages need to run.

`manifests/mtgo-version.json` is the first import gate. It mirrors Videre's public MTGO version manifest, while the skip decision uses the manifest's `codebase` field. The file is written only after a successful run, so a failed import does not mark a changed MTGO codebase as handled.

### Database synchronization

The database records card and product catalog IDs added or changed by an import. R2 uploads for their respective renders are handled outside the database transaction, so a successful database import can be followed by a failed upload. In that case, the image remains pending and can be retried with `sync-images` or `r2-upload` in a separate invocation.

### CDN manifest

`manifests/mtgo-cdn.csv` stores CDN object metadata used by synchronization and upload commands, including object keys, public URLs, timestamps, content types, byte counts, and SHA-256 hashes.

Use `r2-manifest` when this local file must be rebuilt from the actual bucket contents.

### Art overrides

`manifests/mtgo-art-overrides.xml` describes known MTGO art-cache repairs that must be applied before rendering affected card images. Most of these substitute artIDs from their reprints, with a handful of cards using scanned art from the original paper printings.

### Generated output

Files under `output/` are staging artifacts. Export and synchronization commands recreate the files they need, so the directory can be discarded between runs unless its contents are awaiting a manual `r2-upload`.

## CDN layout

The default public R2 base URL is:

```text
https://r2.videreproject.com
```

Object keys follow these conventions:

```text
cards/{catalogId}-300px.png
products/{catalogId}-300px.png
card-counters/{slug}.svg
player-counters/{slug}.png
set-symbols/{setCode}-{rarity}.png
mana-symbols/{symbol}.svg
```

Cards and products can share an MTGO catalog ID, so they use separate CDN prefixes.

## Project layout

```text
CardExporter/
├── CardExporter/
│   ├── src/
│   │   ├── CLI/             # Command dispatch and command implementations
│   │   ├── Database/
│   │   │   ├── Postgres/    # Schema import, copy writers, merge logic, image-work queries
│   │   │   └── R2/          # R2 client, CDN manifest, and object-key helpers
│   │   └── MTGO/
│   │       ├── Files/       # Source-file indexing and manifests
│   │       ├── Parsing/     # MTGO XML parsing and SDK normalization adapters
│   │       ├── Records/     # Card, set, face, legality, and product records
│   │       └── Rendering/   # Card rendering and client-asset extraction
│   └── CardExporter.csproj
├── manifests/               # Source, CDN, and art-override manifests
├── postgres/                # PostgreSQL schema
├── docker-compose.yml       # PostgreSQL and Wine/MTGO runtime services
├── wine-entrypoint.sh       # Wine audio and runtime setup
└── Project.slnx
```

## License

Licensed under [Apache-2.0](LICENSE).

---

This project is not affiliated with Wizards of the Coast or Daybreak Games.
