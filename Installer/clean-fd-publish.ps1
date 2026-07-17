param([Parameter(Mandatory)] [string]$PublishDir)

if (-not (Test-Path $PublishDir)) {
    Write-Error "Publish directory not found: $PublishDir"
    exit 1
}

# Remove debug symbols
Get-ChildItem -Recurse $PublishDir -Filter *.pdb | Remove-Item -Force
Write-Host "Removed .pdb files"

# Remove static libraries (not needed at runtime)
Get-ChildItem -Recurse $PublishDir -Filter *.lib | Remove-Item -Force
Write-Host "Removed .lib files"

# Report final size
$total = Get-ChildItem -Recurse $PublishDir | Measure-Object -Property Length -Sum
Write-Host "Clean FD publish: $([math]::Round($total.Sum / 1MB, 1)) MB"
