@echo off
echo Starting BitcoinFinder Web Server...
echo.
echo Current directory: %CD%
echo.
echo Building project...
dotnet build
echo.
echo Build completed. Starting server...
echo.
echo Server will be available at: http://localhost:5002
echo Press Ctrl+C to stop the server
echo.
dotnet run
pause 