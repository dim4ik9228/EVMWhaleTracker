# EVMWhaleTracker

## Local Docker Setup

If you want to run the app through `docker compose up --build`, the container cannot read your host user secrets directly.

Use the helper script to generate a local `.env` file from the `Presentation.Telegram` user secrets:

```powershell
.\scripts\Export-DockerEnvFromUserSecrets.ps1
```

Then start the stack:

```powershell
docker compose up --build
```

The generated `.env` is ignored by git and is used only for local development.

Postgres data is stored in `.docker/postgres-data/`, so the database survives container recreation locally.
