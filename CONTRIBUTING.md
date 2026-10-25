# Contributing

1. Fork and create a feature branch.
2. Keep changes small and covered by tests. Business rules need unit tests; anything touching SQL behaviour (locking, constraints, concurrency) needs an integration test against a real SQL Server (`ORDERFLOW_TEST_SQL`, or LocalDB on Windows).
3. Add an EF migration for every model change (`dotnet ef migrations add ...`); never edit an applied migration.
4. Before opening a PR:
   ```bash
   dotnet format OrderFlow.sln --exclude src/OrderFlow.Infrastructure/Persistence/Migrations
   dotnet test OrderFlow.sln -c Release
   ```
5. Never commit secrets; configuration comes from environment variables / user-secrets (see `.env.example`).
