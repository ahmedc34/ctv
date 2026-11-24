@echo off
cd /d "%~dp0"

echo [1/3] Paketler indiriliyor ve ilk derleme yapiliyor...
if exist tmp rmdir /s /q tmp
if exist dist rmdir /s /q dist
if exist libvlc.zip del /q libvlc.zip
dotnet publish -c Release -r win-x64 --self-contained -o tmp || (echo HATA: ilk derleme & if not defined CI pause & exit /b 1)

echo [2/3] LibVLC paketleniyor...
powershell -NoProfile -Command "$f = Get-ChildItem -Path tmp -Recurse -Filter libvlc.dll | Select-Object -First 1; if (-not $f) { exit 1 }; Compress-Archive -Path ($f.Directory.FullName + '\*') -DestinationPath libvlc.zip -Force" || (echo HATA: libvlc paketlenemedi & if not defined CI pause & exit /b 1)

echo [3/3] Tek exe uretiliyor...
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist || (echo HATA: tek exe uretilemedi & if not defined CI pause & exit /b 1)

if exist dist\libvlc rmdir /s /q dist\libvlc
if exist dist\*.pdb del /q dist\*.pdb
rmdir /s /q tmp
del /q libvlc.zip

echo.
echo TAMAM: dist\CtvViewer.exe
if not defined CI pause
exit /b 0
