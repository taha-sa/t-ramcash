$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baseline = $null
$formats = @{}
foreach ($name in @('Strings.resx', 'Strings.tr.resx', 'Strings.de.resx')) {
    [xml]$resource = [IO.File]::ReadAllText((Join-Path $root $name))
    $keys = @($resource.root.data | ForEach-Object { $_.name } | Sort-Object)
    if ($keys.Count -ne @($keys | Select-Object -Unique).Count) { throw "$name contains duplicate keys." }
    if ($null -eq $baseline) { $baseline = $keys }
    elseif (Compare-Object $baseline $keys) { throw "$name has a different key set." }
    foreach ($entry in $resource.root.data) {
        if ([string]::IsNullOrWhiteSpace($entry.value)) { throw "$name has an empty value: $($entry.name)" }
        $placeholders = ([regex]::Matches($entry.value, '\{\d+(?:[^}]*)\}') | ForEach-Object { $_.Value } | Sort-Object) -join '|'
        if ($name -eq 'Strings.resx') { $formats[$entry.name] = $placeholders }
        elseif ($formats[$entry.name] -cne $placeholders) { throw "$name has mismatched format placeholders: $($entry.name)" }
    }
    Write-Output "$name : $($keys.Count) keys"
}
