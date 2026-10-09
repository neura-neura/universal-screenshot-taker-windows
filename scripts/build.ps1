$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskDist = Join-Path $taskRoot 'dist'
$taskPublish = Join-Path $taskDist 'portable-x64'
Push-Location $taskRoot
try {
    & "$PSScriptRoot/prepare.ps1"
    dotnet publish src/UniversalCapture -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $taskPublish
    if ($LASTEXITCODE -ne 0) { throw 'Publication failed' }
    foreach ($taskObsolete in @('BrowserExtension/panel.js','BrowserExtension/firefox/panel.js')) {
        $taskObsoletePath = Join-Path $taskPublish $taskObsolete
        if (Test-Path -LiteralPath $taskObsoletePath) { Remove-Item -LiteralPath $taskObsoletePath }
    }
    foreach ($taskResource in @('App.xbf','MainWindow.xbf','UniversalCapture.pri')) {
        if (!(Test-Path -LiteralPath (Join-Path $taskPublish $taskResource))) { throw "Missing published WinUI resource: $taskResource" }
    }
    # App-local VC runtime: use the redistributable files shipped with Visual Studio.
    $taskCrt = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/*/*/VC/Redist/MSVC/*/x64/Microsoft.VC*.CRT' -Directory -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if (!$taskCrt) { throw 'Visual C++ x64 redistributable files were not found. Install Visual Studio C++ build tools for app-local packaging.' }
    Copy-Item (Join-Path $taskCrt.FullName '*.dll') -Destination $taskPublish -Force
    $taskLicenses = Join-Path $taskPublish 'ThirdPartyLicenses'
    New-Item -ItemType Directory -Path $taskLicenses -Force | Out-Null
    $taskNuget = Join-Path $env:USERPROFILE '.nuget/packages'
    foreach ($taskPackage in @('tesseract/5.2.0','pdfsharp/6.2.4','microsoft.windowsappsdk/2.5.1','microsoft.windowsappsdk.runtime/2.5.1')) {
        $taskPackageDir = Join-Path $taskNuget $taskPackage
        foreach ($taskLicense in (Get-ChildItem $taskPackageDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'license|notice' })) {
            Copy-Item -LiteralPath $taskLicense.FullName -Destination (Join-Path $taskLicenses ($taskPackage.Replace('/','-') + '-' + $taskLicense.Name)) -Force
        }
    }
    $taskCompiler = Join-Path $taskRoot '.tools/Inno/ISCC.exe'
    if (!(Test-Path -LiteralPath $taskCompiler)) {
        $taskTools = Join-Path $taskRoot '.tools'
        New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
        $taskInstaller = Join-Path $taskTools 'inno-setup.exe'
        $taskDeps = Get-Content "$PSScriptRoot/dependencies.json" -Raw | ConvertFrom-Json
        Invoke-WebRequest $taskDeps.innoUrl -OutFile $taskInstaller
        $taskSignature = Get-AuthenticodeSignature -LiteralPath $taskInstaller
        if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notmatch 'Pyrsys B.V.') { throw 'Inno Setup signature could not be verified' }
        $taskProcess = Start-Process -FilePath $taskInstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',('/DIR="' + (Join-Path $taskTools 'Inno') + '"')) -Wait -PassThru -WindowStyle Hidden
        if ($taskProcess.ExitCode -ne 0) { throw 'Inno Setup compiler installation failed' }
    }
    & $taskCompiler "/DSourceDir=$taskPublish" "/DOutputDir=$taskDist" "$PSScriptRoot/installer.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
    Compress-Archive -Path (Join-Path $taskPublish '*') -DestinationPath (Join-Path $taskDist 'UniversalScreenshotTaker-Portable-x64.zip') -Force
    Get-FileHash (Join-Path $taskDist '*.exe'),(Join-Path $taskDist '*.zip') -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content (Join-Path $taskDist 'SHA256SUMS.txt')
} finally { Pop-Location }
