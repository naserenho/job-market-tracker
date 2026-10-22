@echo off
rem Starts the local dashboard: Postgres (Compose), then the Aspire AppHost with the API and the Vite app.
rem Dashboard: http://localhost:5190 - the Aspire dashboard login link is printed below. Ctrl+C stops everything.
setlocal
cd /d "%~dp0.."
docker compose up -d --wait postgres
if errorlevel 1 (
  echo Postgres did not start. Is Docker Desktop running?
  exit /b 1
)
dotnet run --project src\AppHost --launch-profile http
