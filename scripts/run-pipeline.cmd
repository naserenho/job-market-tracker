@echo off
rem Collect, then process. The scheduled task appends this script's output to data\pipeline.log.
rem The task result is the processor's exit code if it failed, otherwise the archiver's, so neither failure is hidden.
setlocal
cd /d "%~dp0.."
echo ==== %DATE% %TIME% archiver
".artifacts\archiver\Archiver.exe"
set ARCHIVER_EXIT=%ERRORLEVEL%
echo archiver exit code %ARCHIVER_EXIT%
echo ==== %DATE% %TIME% database
docker compose up -d --wait postgres
if errorlevel 1 echo Docker is not available or Postgres did not become healthy; the processor will report the database as unavailable.
echo ==== %DATE% %TIME% processor
".artifacts\processor\Processor.exe"
set PROCESSOR_EXIT=%ERRORLEVEL%
echo processor exit code %PROCESSOR_EXIT%
if not "%PROCESSOR_EXIT%"=="0" (
  endlocal & exit /b %PROCESSOR_EXIT%
)
endlocal & exit /b %ARCHIVER_EXIT%
