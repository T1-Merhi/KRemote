<#
.SYNOPSIS
    Adds or removes the Windows Firewall rule KRemote needs to be discoverable.

.DESCRIPTION
    KRemote listens on TCP 5555 so other PCs can find it. Without an inbound
    rule, Windows silently drops those probes and scans come back empty.

    The installer calls this with -Action Add and the uninstaller with
    -Action Remove. Firewall changes require elevation, so the script
    re-launches itself elevated when it is not already running as
    administrator. Declining that elevation is not fatal: the install carries
    on, and Windows asks about the app itself on first launch instead.

    The elevated relaunch goes through -EncodedCommand rather than a quoted
    argument string. Passing -File with quoted paths through
    Start-Process -Verb RunAs silently produced a child that did nothing while
    still reporting success, which is the worst possible outcome for a step
    whose failure is invisible until a scan comes back empty.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File firewall.ps1 -Action Add -ExePath "C:\...\KRemote.exe"
#>
[CmdletBinding()]
param(
    [ValidateSet('Add', 'Remove')]
    [string]$Action = 'Add',

    [string]$ExePath,

    [int]$Port = 5555,

    [string]$RuleName = 'KRemote'
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $env:TEMP 'KRemote-firewall.log'

function Write-Log([string]$message) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $message
    try { Add-Content -Path $logPath -Value $line -ErrorAction SilentlyContinue } catch { }
    Write-Output $message
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-RuleExists([string]$name) {
    if (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue) {
        return [bool](Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)
    }
    $output = & netsh advfirewall firewall show rule name="$name" 2>&1 | Out-String
    return $output -notmatch 'No rules match'
}

# --- not elevated: re-launch through UAC and then verify the result ----------

if (-not (Test-Administrator)) {
    # Single-quoted literals inside the command, with embedded quotes doubled,
    # so a path containing an apostrophe cannot break the child.
    function Quote([string]$value) { "'" + ($value -replace "'", "''") + "'" }

    $command = "& $(Quote $PSCommandPath) -Action $(Quote $Action) -Port $Port -RuleName $(Quote $RuleName)"
    if ($ExePath) { $command += " -ExePath $(Quote $ExePath)" }
    $command += '; exit $LASTEXITCODE'

    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))

    try {
        $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -Wait -PassThru `
                              -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    }
    catch {
        # Most often: the user dismissed the UAC prompt.
        Write-Warning "Firewall rule skipped -- elevation was refused or failed: $($_.Exception.Message)"
        Write-Warning "Add it later with:  powershell -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Action $Action"
        exit 0
    }

    # The child's exit code is the only trustworthy verdict. Re-checking the
    # rule from here would always say "missing": Get-NetFirewallRule throws
    # "Access is denied" for a non-elevated caller, which is indistinguishable
    # from an absent rule once the error is suppressed.
    $childExit = if ($null -ne $proc) { $proc.ExitCode } else { 0 }
    if ($childExit -eq 0) {
        Write-Log "Elevated helper reported success for '$RuleName' ($Action)."
        exit 0
    }

    Write-Warning "Firewall rule '$RuleName' was NOT $(if ($Action -eq 'Add') { 'created' } else { 'removed' }) (helper exit $childExit). See $logPath."
    Write-Warning "Run this from an elevated PowerShell to do it by hand:"
    Write-Warning "  powershell -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Action $Action"
    exit 0   # Never fail the install over the firewall.
}

# --- elevated from here ------------------------------------------------------

Write-Log "Running elevated: Action=$Action Port=$Port RuleName='$RuleName' ExePath='$ExePath'"

$useCmdlets = $null -ne (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)

# Always clear the old rule first: re-installing to a new path would otherwise
# leave a stale rule pointing at an executable that no longer exists.
if ($useCmdlets) {
    Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
} else {
    & netsh advfirewall firewall delete rule name="$RuleName" 2>&1 | Out-Null
}

if ($Action -eq 'Remove') {
    Write-Log "Removed firewall rule '$RuleName'."
    exit 0
}

if ($useCmdlets) {
    $params = @{
        DisplayName = $RuleName
        Description = 'Lets other PCs on the local network discover this KRemote instance.'
        Direction   = 'Inbound'
        Protocol    = 'TCP'
        LocalPort   = $Port
        Action      = 'Allow'
        Profile     = 'Private', 'Domain'
        Enabled     = 'True'
    }
    if ($ExePath -and (Test-Path $ExePath)) { $params.Program = $ExePath }
    New-NetFirewallRule @params | Out-Null
} else {
    $program = if ($ExePath -and (Test-Path $ExePath)) { "program=`"$ExePath`"" } else { '' }
    & netsh advfirewall firewall add rule name="$RuleName" dir=in action=allow `
        protocol=TCP localport=$Port profile=private,domain enable=yes $program 2>&1 | Out-Null
}

if (-not (Test-RuleExists $RuleName)) {
    Write-Log "FAILED to create firewall rule '$RuleName'."
    exit 1
}

Write-Log "Added firewall rule '$RuleName' for TCP $Port."
exit 0
