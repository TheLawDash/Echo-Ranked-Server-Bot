<#
.SYNOPSIS
    Shows failures of the Echo Ranked bot since its most recent deployment.

.DESCRIPTION
    Finds the ReplicaSet of the deployment's current revision and, for each of its pods, reports
    restarts, why the last container died, and every fail/crit log entry. Logs of containers that
    crashed and were restarted are included, which plain `kubectl logs` does not show. Warning
    events for the pods since the deployment are listed last.

.EXAMPLE
    .\Find-BotFailures.ps1

.EXAMPLE
    .\Find-BotFailures.ps1 -IncludeWarnings -Context 3
#>
param(
    [string]$KubeContext = "echo-ranked",
    [string]$Namespace = "law-general-bots",
    [string]$Deployment = "echo-ranked-server-bot",
    [string]$Container = "bot",
    # Also report warn: entries, not only fail: and crit:
    [switch]$IncludeWarnings,
    # Extra lines to print after each entry, for stack traces
    [int]$Context = 1
)

$ErrorActionPreference = "Stop"

function Invoke-Kubectl {
    $output = & kubectl --context $KubeContext -n $Namespace @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($args -join ' ') failed: $output" }
    return $output
}

# ConvertFrom-Json already turns RFC3339 strings into DateTime; parsing its string form again
# would treat UTC as local time
function ConvertTo-Utc($Value) {
    if ($Value -is [DateTime]) { return $Value.ToUniversalTime() }
    return [DateTimeOffset]::Parse($Value).UtcDateTime
}

function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host "== $Title" -ForegroundColor Cyan
}

# The .NET console logger writes "fail: Category[0]" followed by indented message lines.
# kubectl --timestamps prefixes every line with an RFC3339 time.
function Get-LogFailures([string[]]$Lines, [string]$Label) {
    $levels = if ($IncludeWarnings) { "fail|crit|warn" } else { "fail|crit" }
    $pattern = "^(\S+)\s+(($levels):\s|Unhandled exception|An unhandled exception)"
    $found = 0

    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -notmatch $pattern) { continue }
        $found++

        $time = $Matches[1]
        $entry = $Lines[$i].Substring($time.Length).Trim()
        $color = if ($entry -match "^(crit|Unhandled|An unhandled)") { "Red" } elseif ($entry -match "^fail") { "Magenta" } else { "Yellow" }
        Write-Host "[$Label] $time  $entry" -ForegroundColor $color

        # Message lines are indented; print them, then up to $Context more for stack traces
        $extra = 0
        for ($j = $i + 1; $j -lt $Lines.Count; $j++) {
            $text = $Lines[$j] -replace "^\S+\s", ""
            $isMessage = $text -match "^\s{6}\S"
            if (-not $isMessage -and $extra -ge $Context) { break }
            if ($text -match "^(info|warn|fail|crit|dbug|trce):\s") { break }
            Write-Host "        $($text.Trim())"
            if (-not $isMessage) { $extra++ }
        }
    }

    return $found
}

$deploy = Invoke-Kubectl get deployment $Deployment -o json | ConvertFrom-Json
$revision = $deploy.metadata.annotations.'deployment.kubernetes.io/revision'

$replicaSets = (Invoke-Kubectl get replicaset -o json | ConvertFrom-Json).items |
    Where-Object {
        $_.metadata.ownerReferences.name -contains $Deployment -and
        $_.metadata.annotations.'deployment.kubernetes.io/revision' -eq $revision
    }
$rs = $replicaSets | Select-Object -First 1
if (-not $rs) { throw "No ReplicaSet found for $Deployment revision $revision" }

$deployedAt = ConvertTo-Utc $rs.metadata.creationTimestamp
$image = $rs.spec.template.spec.containers | Where-Object name -eq $Container | Select-Object -ExpandProperty image

Write-Section "Current deployment"
Write-Host "Revision $revision, deployed $($deployedAt.ToString('u')) ($([int]((Get-Date).ToUniversalTime() - $deployedAt).TotalMinutes) min ago)"
Write-Host "Image    $image"

$pods = (Invoke-Kubectl get pods -o json | ConvertFrom-Json).items |
    Where-Object { $_.metadata.ownerReferences.name -contains $rs.metadata.name }
if (-not $pods) {
    Write-Host "No pods are running for this revision." -ForegroundColor Red
    exit 1
}

$totalFailures = 0
foreach ($pod in $pods) {
    $name = $pod.metadata.name
    $status = $pod.status.containerStatuses | Where-Object name -eq $Container

    Write-Section "Pod $name"
    $restartColor = if ($status.restartCount -gt 0) { "Red" } else { "Green" }
    Write-Host "Phase $($pod.status.phase), restarts: $($status.restartCount)" -ForegroundColor $restartColor

    $last = $status.lastState.terminated
    if ($last) {
        Write-Host "Last crash: $($last.reason), exit code $($last.exitCode), at $($last.finishedAt)" -ForegroundColor Red
    }

    if ($status.restartCount -gt 0) {
        $previous = Invoke-Kubectl logs $name -c $Container --previous --timestamps
        $totalFailures += Get-LogFailures -Lines $previous -Label "crashed container"
    }

    $current = Invoke-Kubectl logs $name -c $Container --timestamps
    $totalFailures += Get-LogFailures -Lines $current -Label "current container"
}

Write-Section "Warning events since the deployment"
$podNames = $pods | ForEach-Object { $_.metadata.name }
$events = (Invoke-Kubectl get events --field-selector type=Warning -o json | ConvertFrom-Json).items |
    Where-Object {
        $podNames -contains $_.involvedObject.name -and
        (ConvertTo-Utc $(if ($_.lastTimestamp) { $_.lastTimestamp } else { $_.eventTime })) -ge $deployedAt
    }
if ($events) {
    foreach ($e in $events) {
        Write-Host "$($e.lastTimestamp)  $($e.reason) x$($e.count): $($e.message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "None"
}

Write-Section "Summary"
$restarts = ($pods | ForEach-Object { ($_.status.containerStatuses | Where-Object name -eq $Container).restartCount } | Measure-Object -Sum).Sum
$summaryColor = if ($totalFailures -gt 0 -or $restarts -gt 0) { "Red" } else { "Green" }
Write-Host "$totalFailures flagged log entries and $restarts restarts since revision $revision was deployed" -ForegroundColor $summaryColor
