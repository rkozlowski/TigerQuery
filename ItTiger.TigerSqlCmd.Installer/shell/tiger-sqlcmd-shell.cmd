@echo off
rem Start Menu > TigerSqlCmd: a command prompt with this installation's tiger-sqlcmd first on its
rem PATH, whether or not the installer added it to the system PATH.
for %%I in ("%~dp0..") do set "PATH=%%~fI;%PATH%"
tiger-sqlcmd --help
cmd /k
