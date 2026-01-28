@echo off
setlocal EnableDelayedExpansion
set ROOT=%~dp0
set GD=%ROOT%GameData

del "%ROOT%*.zip" 2>nul
rmdir /s /q "%ROOT%_tmp" 2>nul

REM ====================================================
REM Build CORE zip
REM ====================================================
call :MAKE_CORE
tar -a -c -f "%ROOT%Kerbal_Powers_Core.zip" -C "%ROOT%_tmp" GameData

REM ====================================================
REM Interstellar bundle
REM ====================================================
call :MAKE_CORE
call :ADD KP Interstellar
call :ADD TURD TU_KP_Interstellar_Recolour
tar -a -c -f "%ROOT%Kerbal_Powers_Interstellar.zip" -C "%ROOT%_tmp" GameData

REM ====================================================
REM Naval bundle
REM ====================================================
call :MAKE_CORE
call :ADD KP Naval
call :ADD KP Electrics
call :ADD TURD TU_KP_Naval_Recolour
tar -a -c -f "%ROOT%Kerbal_Powers_Naval.zip" -C "%ROOT%_tmp" GameData

REM ====================================================
REM Electrics (no TURD)
REM ====================================================
call :MAKE_CORE
call :ADD KP Electrics
tar -a -c -f "%ROOT%electrics.zip" -C "%ROOT%_tmp" GameData

REM ====================================================
REM Interstellar (no TURD)
REM ====================================================
call :MAKE_CORE
call :ADD KP Interstellar
tar -a -c -f "%ROOT%interstellar.zip" -C "%ROOT%_tmp" GameData

REM ====================================================
REM Naval (no TURD)
REM ====================================================
call :MAKE_CORE
call :ADD KP Naval
tar -a -c -f "%ROOT%naval.zip" -C "%ROOT%_tmp" GameData

REM ====================================================
REM Paint (TURD only)
REM ====================================================
call :MAKE_CORE
call :ADD TURD TU_KP_Interstellar_Recolour
call :ADD TURD TU_KP_Naval_Recolour
tar -a -c -f "%ROOT%paint.zip" -C "%ROOT%_tmp" GameData

rmdir /s /q "%ROOT%_tmp"
echo All packages built.
pause
exit /b

REM ====================================================
REM Core builder
REM ====================================================
:MAKE_CORE
rmdir /s /q "%ROOT%_tmp" 2>nul
mkdir "%ROOT%_tmp\GameData\KerbalPowers"

xcopy "%GD%\KerbalPowers\Agencies" "%ROOT%_tmp\GameData\KerbalPowers\Agencies\" /e /i /q
copy "%GD%\KerbalPowers\LicensesAndCredits.txt" "%ROOT%_tmp\GameData\KerbalPowers\" >nul
exit /b

REM ====================================================
REM Add folder to staging
REM %1 = KP or TURD
REM %2 = subfolder
REM ====================================================
:ADD
if "%1"=="KP" (
    xcopy "%GD%\KerbalPowers\%2" "%ROOT%_tmp\GameData\KerbalPowers\%2\" /e /i /q
) else (
    mkdir "%ROOT%_tmp\GameData\TURD" 2>nul
    xcopy "%GD%\TURD\%2" "%ROOT%_tmp\GameData\TURD\%2\" /e /i /q
)
exit /b
