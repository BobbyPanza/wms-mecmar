# Eseguire come Amministratore

$ip        = "192.168.10.127"
$httpPort  = 50080
$httpsPort = 50443
$exportCA  = "C:\wms-root-ca.cer"

# ── Pulizia ─────────────────────────────────────────────────────────────────
Write-Host "`n=== Pulizia certificati precedenti ===" -ForegroundColor Cyan
Get-ChildItem "cert:\LocalMachine\My"   | Where-Object { $_.FriendlyName -like "WMS Mecmar*" } | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem "cert:\LocalMachine\Root" | Where-Object { $_.FriendlyName -like "WMS Mecmar*" } | Remove-Item -Force -ErrorAction SilentlyContinue

# ── 1. Root CA — creato in My, poi spostato in Root ─────────────────────────
Write-Host "`n=== 1. Creazione Root CA ===" -ForegroundColor Cyan
$ca = New-SelfSignedCertificate `
    -Subject           "CN=WMS Mecmar Root CA" `
    -CertStoreLocation "cert:\LocalMachine\My" `
    -KeyUsage          CertSign, CRLSign `
    -NotAfter          (Get-Date).AddYears(10) `
    -FriendlyName      "WMS Mecmar Root CA" `
    -TextExtension     @("2.5.29.19={text}CA=true")

# Copia in Root store (necessario per IIS trust chain + export)
$rootStore = [System.Security.Cryptography.X509Certificates.X509Store]::new("Root","LocalMachine")
$rootStore.Open("ReadWrite")
$rootStore.Add($ca)
$rootStore.Close()
Write-Host "Root CA installato: $($ca.Thumbprint)" -ForegroundColor Green

# ── 2. Cert server firmato dalla Root CA, IP SAN corretto ───────────────────
Write-Host "`n=== 2. Creazione certificato server ===" -ForegroundColor Cyan
$server = New-SelfSignedCertificate `
    -Subject           "CN=$ip" `
    -CertStoreLocation "cert:\LocalMachine\My" `
    -KeyUsage          DigitalSignature, KeyEncipherment `
    -NotAfter          (Get-Date).AddYears(5) `
    -FriendlyName      "WMS Mecmar Server" `
    -Signer            $ca `
    -TextExtension     @(
        "2.5.29.17={text}IPAddress=$ip",
        "2.5.29.37={text}1.3.6.1.5.5.7.3.1"
    )
Write-Host "Server cert: $($server.Thumbprint)" -ForegroundColor Green

# ── 3. Binding HTTPS in IIS ──────────────────────────────────────────────────
Write-Host "`n=== 3. Aggiornamento binding IIS ===" -ForegroundColor Cyan
Import-Module WebAdministration

$site = Get-WebSite | Where-Object {
    $_.Bindings.Collection | Where-Object { $_.bindingInformation -like "*:${httpPort}:*" }
} | Select-Object -First 1

if (-not $site) { Write-Host "ERRORE: sito non trovato su porta $httpPort" -ForegroundColor Red; exit 1 }
Write-Host "Sito: $($site.Name)" -ForegroundColor Green

Get-WebBinding -Name $site.Name -Protocol https -Port $httpsPort -ErrorAction SilentlyContinue |
    Remove-WebBinding -ErrorAction SilentlyContinue

New-WebBinding -Name $site.Name -Protocol https -IPAddress $ip -Port $httpsPort

# Associa cert via netsh (più affidabile di AddSslCertificate su alcuni OS)
$appId = "{$(New-Guid)}"
netsh http delete sslcert ipport="${ip}:${httpsPort}" 2>$null | Out-Null
netsh http add sslcert ipport="${ip}:${httpsPort}" certhash=$($server.Thumbprint) appid=$appId certstorename=My
Write-Host "SSL cert associato a ${ip}:${httpsPort}" -ForegroundColor Green

# ── 4. Esporta Root CA per i palmari ────────────────────────────────────────
Write-Host "`n=== 4. Esportazione Root CA ===" -ForegroundColor Cyan
$caCert = Get-ChildItem "cert:\LocalMachine\Root" | Where-Object { $_.Thumbprint -eq $ca.Thumbprint }
Export-Certificate -Cert $caCert -FilePath $exportCA -Force | Out-Null
Write-Host "Esportato: $exportCA" -ForegroundColor Green

Write-Host "`n=== COMPLETATO ===" -ForegroundColor Yellow
Write-Host "URL HTTPS : https://${ip}:${httpsPort}/login"
Write-Host ""
Write-Host "Passi sul palmare:" -ForegroundColor Yellow
Write-Host "  1. Scarica http://${ip}:${httpPort}/wms-root-ca.cer"
Write-Host "  2. Installa come 'Certificato CA'"
Write-Host "  3. Apri https://${ip}:${httpsPort}/login e reinstalla la PWA"
