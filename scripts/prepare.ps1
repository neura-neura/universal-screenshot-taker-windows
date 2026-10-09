$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskDependencies = Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
$taskData = Join-Path $taskRoot 'src/UniversalCapture/tessdata'
New-Item -ItemType Directory -Path $taskData -Force | Out-Null
foreach ($taskModel in $taskDependencies.models.PSObject.Properties) {
    $taskFile = Join-Path $taskData ($taskModel.Name + '.traineddata')
    if (!(Test-Path -LiteralPath $taskFile)) {
        Invoke-WebRequest ('https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/' + $taskDependencies.tessdataRevision + '/' + $taskModel.Name + '.traineddata') -OutFile $taskFile
    }
    if ((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ne $taskModel.Value) { throw "Model checksum mismatch: $($taskModel.Name)" }
}
if (!(Test-Path -LiteralPath (Join-Path $taskData 'LICENSE.txt'))) {
    Invoke-WebRequest ('https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/' + $taskDependencies.tessdataRevision + '/LICENSE') -OutFile (Join-Path $taskData 'LICENSE.txt')
}
$taskFirefox = Join-Path $taskRoot 'src/UniversalCapture/BrowserExtension/firefox'
 $taskTranslations = Get-Content (Join-Path $taskRoot 'src/UniversalCapture.Core/Translations.json') -Raw
 $taskI18n = 'globalThis.__ustTranslations=' + $taskTranslations + ';globalThis.__ustText=function(text){const lang=String(globalThis.__ustLanguage||navigator.language||"en").toLowerCase();const values=globalThis.__ustTranslations[text];return lang.startsWith("es")||!values?text:values[lang.startsWith("zh")?1:0];};'
 $taskI18n | Set-Content (Join-Path $taskRoot 'src/UniversalCapture/BrowserExtension/i18n.js') -Encoding utf8
New-Item -ItemType Directory -Path $taskFirefox -Force | Out-Null
$taskObsolete = Join-Path $taskFirefox 'panel.js'
if (Test-Path -LiteralPath $taskObsolete) { Remove-Item -LiteralPath $taskObsolete }
foreach ($taskName in @('background.js','panel.html','controller.js','page.js','popup.js','i18n.js','offscreen.js','offscreen.html')) { Copy-Item -LiteralPath (Join-Path $taskRoot ('src/UniversalCapture/BrowserExtension/' + $taskName)) -Destination $taskFirefox -Force }
$taskManifest = Get-Content (Join-Path $taskRoot 'src/UniversalCapture/BrowserExtension/manifest.json') -Raw | ConvertFrom-Json
$taskManifest.manifest_version = 2
$taskManifest.permissions = @($taskManifest.permissions | Where-Object { $_ -ne 'offscreen' }) + @($taskManifest.host_permissions)
$taskManifest | Add-Member -NotePropertyName 'browser_action' -NotePropertyValue $taskManifest.action -Force
$taskManifest.PSObject.Properties.Remove('action')
$taskManifest.PSObject.Properties.Remove('host_permissions')
$taskManifest.PSObject.Properties.Remove('minimum_chrome_version')
$taskManifest.background = @{ page = 'offscreen.html'; persistent = $true }
$taskFirefoxHtml = Get-Content (Join-Path $taskFirefox 'offscreen.html') -Raw
$taskFirefoxHtml.Replace('<script src="i18n.js">','<script src="background.js"></script><script src="i18n.js">') | Set-Content (Join-Path $taskFirefox 'offscreen.html') -Encoding utf8
$taskManifest | Add-Member -NotePropertyName 'browser_specific_settings' -NotePropertyValue @{ gecko = @{ id = 'ust-selector@neura-neura'; strict_min_version = '121.0' } } -Force
$taskManifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $taskFirefox 'manifest.json') -Encoding utf8
