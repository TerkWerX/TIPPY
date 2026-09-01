param(
    [Parameter(Mandatory = $true)] [string] $ManifestPath,
    [Parameter(Mandatory = $true)] [string] $PrivateKeyPath,
    [string] $PublisherId = "terkwerx-official-2026"
)

$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$resolvedKey = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
$manifest.publisher_id = $PublisherId
$manifest.signature_algorithm = "RSA-SHA256"

$builder = [System.Text.StringBuilder]::new()
[void]$builder.Append($manifest.pack_id.Trim()).Append("`n").Append($manifest.version.Trim()).Append("`n").Append($PublisherId.Trim()).Append("`n")
foreach ($file in $manifest.files | Sort-Object path) {
    $path = $file.path.Replace('\', '/')
    $hash = $file.sha256.Replace('sha256:', '', [System.StringComparison]::OrdinalIgnoreCase).Trim()
    [void]$builder.Append($path).Append(':').Append($hash).Append("`n")
}

$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([System.IO.File]::ReadAllText($resolvedKey))
    $signature = $rsa.SignData(
        [System.Text.Encoding]::UTF8.GetBytes($builder.ToString()),
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $manifest.signature = [Convert]::ToBase64String($signature)
    [System.IO.File]::WriteAllText(
        $resolvedManifest,
        ($manifest | ConvertTo-Json -Depth 20),
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "Signed $resolvedManifest as $PublisherId"
}
finally {
    $rsa.Dispose()
}
