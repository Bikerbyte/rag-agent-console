@echo off
setlocal
echo Stopping local listeners on ports 5166 and 7212...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5166" ^| findstr "LISTENING"') do taskkill /PID %%p /F
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":7212" ^| findstr "LISTENING"') do taskkill /PID %%p /F
echo Done.
pause
