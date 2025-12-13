# dndhelper

## Database backup
- Requires `mongodump` from MongoDB Database Tools.
- Command from repo root:
  - Full database: `pwsh Database/backup-mongodb.ps1`
  - Single collection: `pwsh Database/backup-mongodb.ps1 -Collection sessions`
- Backups are written under `backups/<timestamp>/` using connection settings from environment variables or `appsettings*.json`.

## API backup/restore
- Authenticated GET `api/backup/{collectionName}` returns a gzipped mongodump archive (ready for `mongorestore --archive --gzip`).
- Authenticated GET `api/backup/all` returns a zip file containing every collection as individual `.gz` entries.
- Authenticated POST `api/backup/{collectionName}/restore` with a multipart file field (gzip archive) restores that collection (drops existing first).
- Both endpoints respect `MONGODUMP_PATH` / `MONGORESTORE_PATH` if set; otherwise tools must be on PATH (Render image installs them via Dockerfile).
