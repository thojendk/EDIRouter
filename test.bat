@echo off
setlocal

set INPUT=%~dp0test\input
set OUTPUT=%~dp0test\output

echo === EDIRouter Test ===
echo.

:: Clean up previous run
if exist "%INPUT%"  rd /s /q "%INPUT%"
if exist "%OUTPUT%" rd /s /q "%OUTPUT%"
mkdir "%INPUT%"
mkdir "%OUTPUT%"

:: Production interchange (ACMECORP, no test indicator)
echo UNA:+.? 'UNB+UNOA:2+SENDER123:1+ACMECORP:1+260526:1200+1'UNH+1+ORDERS:D:96A:UN'UNZ+1+1'> "%INPUT%\order_prod.edi"

:: Test interchange (GLOBEX, test indicator = 1 at field 11)
echo UNB+UNOA:2+SENDER123:1+GLOBEX:1+260526:1201+2++++++1'UNH+1+ORDERS:D:96A:UN'UNZ+1+2'> "%INPUT%\order_test.edi"

:: Second prod file for ACMECORP (duplicate name test)
echo UNA:+.? 'UNB+UNOA:2+SENDER123:1+ACMECORP:1+260526:1202+3'UNH+1+INVOIC:D:96A:UN'UNZ+1+3'> "%INPUT%\order_prod.edi.copy"
ren "%INPUT%\order_prod.edi.copy" "order_prod.edi.2"
copy "%INPUT%\order_prod.edi.2" "%INPUT%\order_prod.edi" /y >nul 2>&1
del "%INPUT%\order_prod.edi.2"

echo Created test files:
dir /b "%INPUT%"
echo.

:: Build
echo --- Building ---
dotnet build "%~dp0EDIRouter\EDIRouter.csproj" -c Release -v quiet
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )
echo.

:: Run
echo --- Running EDIRouter ---
dotnet run --project "%~dp0EDIRouter\EDIRouter.csproj" --configuration Release --no-build -- "%INPUT%" "%OUTPUT%"
echo.

:: Show results
echo --- Output structure ---
dir /s /b "%OUTPUT%"
echo.

echo --- Log ---
for /f "delims=" %%f in ('dir /b /o-d "%OUTPUT%\logs\edirouter-*.log" 2^>nul') do (
    type "%OUTPUT%\logs\%%f"
    goto :done
)
:done

endlocal
