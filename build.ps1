param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $BuildArgs
)

$ErrorActionPreference = 'Stop'
$buildFile = Join-Path $PSScriptRoot 'build.cs'

Push-Location $PSScriptRoot
try {
    dotnet run --file $buildFile -- @BuildArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
