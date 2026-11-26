#requires -Version 7
# The notes of a release: what changed in the launcher since the release before, one line per commit title, read from
# the git repository in the current folder. Only commits that change something below src\ are listed.
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Repository,
    [string]$Commit = 'HEAD'
)

$ErrorActionPreference = 'Stop'
# git writes UTF-8, and so does this script
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$version = [version]$Tag.Substring(1)
$tags = git tag --list 'v*'
if ($LASTEXITCODE -ne 0) { throw 'The tags could not be read.' }
$before = $tags |
    Where-Object { $_ -cmatch '\Av[0-9]+\.[0-9]+\.[0-9]+\z' -and [version]$_.Substring(1) -lt $version } |
    Sort-Object { [version]$_.Substring(1) } |
    Select-Object -Last 1

$range = if ($before) { "$before..$Commit" } else { $Commit }
# --full-history: with a path, git may otherwise leave out commits of a merged branch
$titles = @(git log --no-merges --full-history --topo-order --reverse --format=%s $range -- src)
if ($LASTEXITCODE -ne 0) { throw "The commits of $range could not be read." }

"## What's new"
''
if ($titles.Count -eq 0) {
    'No changes to the launcher itself.'
}
else {
    # GitHub notifies whoever is named with an @ in release notes
    $titles | ForEach-Object { '- ' + ($_ -replace '(?<![\w`])@[\w/-]+', '`$0`') }
}
if ($before) {
    ''
    "**Full Changelog**: https://github.com/$Repository/compare/$before...$Tag"
}
