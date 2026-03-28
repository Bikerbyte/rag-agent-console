@echo off
setlocal
echo Checking listeners on the default local ports...
echo.
netstat -ano | findstr ":5166"
echo.
netstat -ano | findstr ":7212"
echo.
echo If you see a PID above, you can stop it with Stop-Local.cmd.
pause
