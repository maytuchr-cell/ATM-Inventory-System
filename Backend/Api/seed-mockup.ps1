# Seeds a realistic spread of mockup Tickets against a RUNNING backend (default http://localhost:5128).
# Covers every Status/phase so admin-tickets.html, admin-returns.html, and tech.html all have
# something to click through. Safe to re-run -- each ticket uses a unique ExternalTicketNo, and
# /Ticket/sync is a no-op if that number already exists.
#
# ASCII-only on purpose: Windows PowerShell 5.1 misreads UTF-8 script files without a BOM,
# which corrupts non-ASCII text and breaks parsing. Keeping names/addresses in English avoids
# that entirely -- the Status values themselves (still Thai: รอ/เดินทาง/เบิก/คืน) travel fine
# because they only ever appear inside HTTP JSON bodies, not as literals in this .ps1 file.
#
# Usage:  powershell -ExecutionPolicy Bypass -File seed-mockup.ps1
#         powershell -ExecutionPolicy Bypass -File seed-mockup.ps1 -BaseUrl http://172.20.10.9:5128

param(
    [string]$BaseUrl = "http://localhost:5128"
)

$api = "$BaseUrl/api"
$ErrorActionPreference = "Stop"

function Sync-Ticket($extNo, $techName, $techEmail, $techDept) {
    $body = @{ externalTicketNo = $extNo; techName = $techName; techEmail = $techEmail; techDept = $techDept } | ConvertTo-Json
    $res = Invoke-RestMethod -Uri "$api/Ticket/sync" -Method Post -Body $body -ContentType "application/json"
    Write-Host "  synced $extNo -> ticketId $($res.ticket.ticketId)"
    return $res.ticket.ticketId
}
function Submit-Withdraw($id, $lines, $address) {
    $body = @{ lines = $lines; address = $address } | ConvertTo-Json
    Invoke-RestMethod -Uri "$api/Ticket/$id/withdraw" -Method Put -Body $body -ContentType "application/json" | Out-Null
}
function Approve-Ticket($id)  { Invoke-RestMethod -Uri "$api/Ticket/$id/approve" -Method Put | Out-Null }
function Receive-Ticket($id)  { Invoke-RestMethod -Uri "$api/Ticket/$id/receive" -Method Put | Out-Null }
function Submit-Return($id, $lines, $address) {
    $body = @{ lines = $lines; address = $address } | ConvertTo-Json
    Invoke-RestMethod -Uri "$api/Ticket/$id/return" -Method Put -Body $body -ContentType "application/json" | Out-Null
}
function Ship-Ticket($id)         { Invoke-RestMethod -Uri "$api/Ticket/$id/ship" -Method Put | Out-Null }
function Confirm-Return($id)      { Invoke-RestMethod -Uri "$api/Ticket/$id/confirm-return" -Method Put | Out-Null }
function Reject-Ticket($id, $r)   { Invoke-RestMethod -Uri "$api/Ticket/$id/reject" -Method Put -Body (@{reason=$r}|ConvertTo-Json) -ContentType "application/json" | Out-Null }
function Cancel-Ticket($id)       { Invoke-RestMethod -Uri "$api/Ticket/$id/cancel" -Method Put | Out-Null }

# Real Part.PartNo values guaranteed to exist (seeded by Program.cs for the serial-tracking demo).
$P1 = "ATM-001"; $P2 = "ATM-002"; $P3 = "ATM-003"; $P4 = "ATM-004"; $P6 = "ATM-006"; $P8 = "ATM-008"

Write-Host "== 1) MOCK-101 -- just synced, tech hasn't withdrawn yet (status = null) =="
Sync-Ticket "ASV-MOCK-101" "Somchai Jaidee" "tech@atm.com" "Zone 1 - North" | Out-Null

Write-Host "== 2) MOCK-102 -- withdraw submitted, waiting Admin approval (status: waiting) =="
$id = Sync-Ticket "ASV-MOCK-102" "Wipa Saibua" "tech@atm.com" "Zone 2 - Central"
Submit-Withdraw $id @(@{partNo=$P3; quantity=1}) "Ladprao 101 Branch"

Write-Host "== 3) MOCK-103 -- approved, in transit to tech (status: in-transit) =="
$id = Sync-Ticket "ASV-MOCK-103" "Prayuth Mankong" "tech@atm.com" "Zone 1 - North"
Submit-Withdraw $id @(@{partNo=$P6; quantity=1}) "Bang Na 55 Branch"
Approve-Ticket $id

Write-Host "== 4) MOCK-104 -- received by tech, multi-part (status: withdrawn) =="
$id = Sync-Ticket "ASV-MOCK-104" "Arun Saengthong" "tech@atm.com" "Zone 3 - South"
Submit-Withdraw $id @(@{partNo=$P1; quantity=1}, @{partNo=$P4; quantity=2}) "Silom 12 Branch"
Approve-Ticket $id
Receive-Ticket $id

Write-Host "== 5) MOCK-105 -- return submitted, waiting tech to ship (status: waiting/return) =="
$id = Sync-Ticket "ASV-MOCK-105" "Kamon Srisuk" "tech@atm.com" "Zone 2 - Central"
Submit-Withdraw $id @(@{partNo=$P2; quantity=1}) "Ramkhamhaeng 88 Branch"
Approve-Ticket $id
Receive-Ticket $id
Submit-Return $id @(@{partNo=$P2; quantity=1}) "DHL Hub Ramkhamhaeng"

Write-Host "== 6) MOCK-106 -- return shipped, in transit to DHL (status: in-transit/return) =="
$id = Sync-Ticket "ASV-MOCK-106" "Napa Pinthong" "tech@atm.com" "Zone 1 - North"
Submit-Withdraw $id @(@{partNo=$P8; quantity=1}) "On Nut 20 Branch"
Approve-Ticket $id
Receive-Ticket $id
Submit-Return $id @(@{partNo=$P8; quantity=1}) "DHL Hub On Nut"
Ship-Ticket $id

Write-Host "== 7) MOCK-107 -- fully returned, PARTIAL (withdrew 3, returned 2) (status: returned) =="
$id = Sync-Ticket "ASV-MOCK-107" "Supattra Jaidee" "tech@atm.com" "Zone 3 - South"
Submit-Withdraw $id @(@{partNo=$P1; quantity=3}) "Rama 9 Branch"
Approve-Ticket $id
Receive-Ticket $id
Submit-Return $id @(@{partNo=$P1; quantity=2}) "DHL Hub Rama 9"
Ship-Ticket $id
Confirm-Return $id

Write-Host "== 8) MOCK-108 -- rejected by Admin (status: rejected) =="
$id = Sync-Ticket "ASV-MOCK-108" "Malee Rungrueang" "tech@atm.com" "Zone 2 - Central"
Submit-Withdraw $id @(@{partNo=$P4; quantity=1}) "Central World Branch"
Reject-Ticket $id "Selected part does not match the ticket's machine model. Please re-select."

Write-Host "== 9) MOCK-109 -- cancelled by Admin (status: cancelled) =="
$id = Sync-Ticket "ASV-MOCK-109" "Thana Saksit" "tech@atm.com" "Zone 1 - North"
Submit-Withdraw $id @(@{partNo=$P3; quantity=1}) "Asoke Branch"
Cancel-Ticket $id

Write-Host "== 10) MOCK-110 -- another waiting-approval ticket, multi-part, different tech =="
$id = Sync-Ticket "ASV-MOCK-110" "Rattana Thongdee" "tech@atm.com" "Zone 3 - South"
Submit-Withdraw $id @(@{partNo=$P2; quantity=1}, @{partNo=$P6; quantity=1}) "Bang Kapi Branch"

Write-Host ""
Write-Host "Done. 10 mockup tickets created against $BaseUrl covering every Status/phase."
Write-Host "View them at admin-tickets.html / admin-returns.html / tech.html (login as tech@atm.com to see them under 'My Tickets')."
