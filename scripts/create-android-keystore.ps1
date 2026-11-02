# Creates the Android release signing keystore and prints the values to store as GitHub secrets.
# Keep the .keystore file somewhere safe and private. If it is lost, users must uninstall the app to update.
param([string]$Path = "$HOME\wowqueue-release.keystore", [string]$Alias = 'wowqueue')
$ErrorActionPreference = 'Stop'

$keytool = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\keytool.exe' } else { 'keytool' }
$pw = [Convert]::ToBase64String((1..18 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]]) -replace '[^A-Za-z0-9]', 'x'

& $keytool -genkeypair -v -keystore $Path -alias $Alias -keyalg RSA -keysize 2048 -validity 10000 `
    -storepass $pw -keypass $pw -dname 'CN=WoW Queue Monitor'
if ($LASTEXITCODE -ne 0) { throw 'keytool failed' }

Write-Host "`nKeystore: $Path`n"
Write-Host 'Add these in GitHub: Settings > Secrets and variables > Actions > New repository secret'
Write-Host "  ANDROID_KEYSTORE_PASSWORD = $pw"
Write-Host "  ANDROID_KEY_PASSWORD      = $pw"
Write-Host "  ANDROID_KEY_ALIAS         = $Alias"
Write-Host '  ANDROID_KEYSTORE_BASE64   = (copied to clipboard)'
[Convert]::ToBase64String([IO.File]::ReadAllBytes($Path)) | Set-Clipboard
