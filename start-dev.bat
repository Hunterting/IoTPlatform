@echo off
cd /d H:\IoTPlatform
if not exist bin\Debug\net8.0\IoTPlatform.dll (
    echo [ERROR] bin\Debug\net8.0\IoTPlatform.dll not found. Run: dotnet build -c Debug
    pause
    exit /b 1
)
start "IoTPlatform-Backend" cmd /k "cd /d H:\IoTPlatform && set ASPNETCORE_ENVIRONMENT=Staging && set ASPNETCORE_URLS=http://localhost:5000 && dotnet bin\Debug\net8.0\IoTPlatform.dll --contentroot H:\IoTPlatform"
cd /d H:\IoTPlatform\Web
start "IoTPlatform-Frontend" cmd /k "cd /d H:\IoTPlatform\Web && npm run dev"
echo.
echo Backend:  http://localhost:5000   (Swagger: /swagger)
echo Frontend: http://localhost:5173
echo Login:    admin@system.com / admin123
echo.
echo NOTE: frontend dev server targets http://localhost:5000 (VITE_API_BASE_URL), so backend is on 5000.
echo Close the popped command windows to stop the services.
pause
