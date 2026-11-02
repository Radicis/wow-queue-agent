param([string]$Version = '0.0.0-dev')
$ErrorActionPreference = 'Stop'
dotnet publish "$PSScriptRoot\WowQueueAgent" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o "$PSScriptRoot\..\dist\windows"
