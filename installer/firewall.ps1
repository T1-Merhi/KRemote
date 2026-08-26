<#
.SYNOPSIS
    Adds or removes the Windows Firewall rule KRemote needs to be discoverable.

.DESCRIPTION
    KRemote listens on TCP 5555 so other PCs can find it. Without an inbound
    rule, Windows silently drops those probes and scans come back empty.

    The installer calls this with -Action Add and the uninstaller with
    -Action Remove. Firewall changes require elevation, so the script
    re-launches itself through UAC when it is not already running as
    administrator; declining that prompt is not fatal, it just means Windows
    will ask about the app itself on first launch instead.

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

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    # Re-run elevated. Each argument is quoted individually because the install
    # directory can sit under a user profile whose name contains spaces.
    $argumentList = @(
        '-NoProfile'
        '-ExecutionPolicy', 'Bypass'
        '-File', ('"{0}"' -f $PSCommandPath)
        '-Action', $Action
        '-Port', $Port
        '-RuleName', ('"{0}"' -f $RuleName)
    )
    if ($ExePath) { $argumentList += @('-ExePath', ('"{0}"' -f $ExePath)) }

    try {
        $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden `
                              -ArgumentList $argumentList -Wait -PassThru
        exit $proc.ExitCode
    }
    catch {
        Write-Warning "Firewall rule skipped: $($_.Exception.Message)"
        exit 0    # Never fail the install over this.
    }
}

# --- elevated from here ------------------------------------------------------

$useCmdlets = $null -ne (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)

# Always clear the old rule first: re-installing to a new path would otherwise
# leave a stale rule pointing at an executable that no longer exists.
if ($useCmdlets) {
    Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
} else {
    & netsh advfirewall firewall delete rule name="$RuleName" | Out-Null
}

if ($Action -eq 'Remove') {
    Write-Output "Removed firewall rule '$RuleName'."
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
        protocol=TCP localport=$Port profile=private,domain enable=yes $program | Out-Null
}

Write-Output "Added firewall rule '$RuleName' for TCP $Port."
exit 0
