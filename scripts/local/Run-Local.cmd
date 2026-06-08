@echo off
setlocal
cd /d "%~dp0"
title RAG Agent Console
echo ============================================================
echo RAG Agent Console
echo.
echo This window stays open so you can always see the running PID
echo and stop the site cleanly with Ctrl+C.
echo.
echo Default URL: http://localhost:5166
echo ============================================================
echo.
dotnet run --launch-profile http
echo.
echo The local site has stopped.
pause
