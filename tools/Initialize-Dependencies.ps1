param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$previousSkip = $env:GIT_LFS_SKIP_SMUDGE
try {
    $env:GIT_LFS_SKIP_SMUDGE = '1'
    & git -C $root submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) {
        throw 'Submodule initialization failed. Check Git, network access and the repository URL.'
    }
} finally {
    if ($null -eq $previousSkip) {
        Remove-Item Env:GIT_LFS_SKIP_SMUDGE -ErrorAction SilentlyContinue
    } else {
        $env:GIT_LFS_SKIP_SMUDGE = $previousSkip
    }
}
