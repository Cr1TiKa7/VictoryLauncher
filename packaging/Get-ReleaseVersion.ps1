#requires -Version 7
# The rules of the release action. Writes "version=X.Y.Z" and "tag=vX.Y.Z" for a release, and
# "version=0.0.0" with an empty tag for a build that is not released. Anything not allowed stops with an error.
param(
    [Parameter(Mandatory)][string]$EventName,
    [Parameter(Mandatory)][string]$Ref,
    [string]$Requested = '',
    [string]$ExistingTags = ''
)

$ErrorActionPreference = 'Stop'
$part = '(0|[1-9][0-9]{0,4})'

# Three numbers as the launcher reads them (ReleaseVersion.Parse), or $null.
function ConvertTo-ReleaseVersion([string]$Text) {
    if ($Text -cnotmatch "\Av?$part\.$part\.$part\z") { return $null }
    $numbers = [int[]]@($Matches[1], $Matches[2], $Matches[3])
    # the compiler refuses a larger part
    if ($numbers | Where-Object { $_ -gt 65534 }) { return $null }
    [version]::new($numbers[0], $numbers[1], $numbers[2])
}

function Stop-Run([string]$Reason) {
    [Console]::Error.WriteLine($Reason)
    exit 1
}

$local = [version]'0.0.0'

if ($EventName -eq 'workflow_dispatch') {
    if ($Ref -cne 'refs/heads/main') { Stop-Run "Releases are built from main only, not from $Ref." }
    $version = ConvertTo-ReleaseVersion $Requested.Trim()
    if ($null -eq $version -or $version -eq $local) { Stop-Run "'$Requested' is not a version like 1.2.0." }
    $released = @($ExistingTags -split '\s+' | Where-Object { $_ -clike 'v*' } |
        ForEach-Object { ConvertTo-ReleaseVersion $_ } | Where-Object { $null -ne $_ } | Sort-Object)
    if ($released.Count -gt 0 -and $version -le $released[-1]) {
        Stop-Run "$version is not higher than v$($released[-1]). The launcher would not offer it."
    }
}
elseif ($Ref -clike 'refs/tags/*') {
    $name = $Ref.Substring('refs/tags/'.Length)
    $version = ConvertTo-ReleaseVersion $name
    if ($null -eq $version -or $version -eq $local -or $name -cne "v$version") {
        Stop-Run "The tag '$name' is not a release tag like v1.2.0."
    }
}
else {
    "version=$local"
    'tag='
    exit 0
}

"version=$version"
"tag=v$version"
