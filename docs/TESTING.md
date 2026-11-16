# Testing

Backend:

```bash
dotnet test
```

`SqlServerContainerTests` starts SQL Server with Testcontainers. It runs when `docker info` succeeds and is skipped otherwise. GitHub Actions provides Docker.

Frontend, from `web`:

```bash
npm run lint
npm run typecheck
npm test
```
