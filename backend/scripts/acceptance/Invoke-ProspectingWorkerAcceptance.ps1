[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$expectedBranch = "kiger-dev"

Push-Location $repoRoot
try {
    $branch = (git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to determine the current Git branch." }
    if ($branch -ne $expectedBranch) {
        throw "Refusing acceptance run on branch '$branch'. Switch to '$expectedBranch' first."
    }

    $changes = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect Git working tree." }
    if ($changes) { throw "Refusing acceptance run with uncommitted changes. Commit or stash them first." }

    $sdkVersion = (dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw "The .NET SDK is unavailable. Install the repository-required .NET 10 SDK." }
    if ($sdkVersion -notmatch '^10\.') {
        throw "Expected .NET 10 SDK for net10.0 projects, found '$sdkVersion'."
    }

    $crmProject = Join-Path $repoRoot "backend\src\modules\XiHan.BasicApp.Crm\XiHan.BasicApp.Crm.csproj"
    $testProject = Join-Path $repoRoot "backend\test\XiHan.BasicApp.Api.Tests\XiHan.BasicApp.Api.Tests.csproj"

    Write-Host "SDK: $sdkVersion"
    Write-Host "Restoring CRM module..."
    dotnet restore $crmProject
    if ($LASTEXITCODE -ne 0) { throw "CRM module restore failed." }

    Write-Host "Building CRM module (Release)..."
    dotnet build $crmProject --no-restore --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "CRM module build failed." }

    Write-Host "Restoring API contract tests..."
    dotnet restore $testProject
    if ($LASTEXITCODE -ne 0) { throw "API test project restore failed." }

    Write-Host "Building API contract tests (Release)..."
    dotnet build $testProject --no-restore --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "API test project build failed." }

    if (-not $SkipTests) {
        Write-Host "Running prospecting contract and SQLite integration tests..."
        dotnet test $testProject --no-build --configuration Release --filter "FullyQualifiedName~Prospecting"
        if ($LASTEXITCODE -ne 0) { throw "Prospecting tests failed." }
    }

    Write-Host "Acceptance script completed. This does not replace live PostgreSQL two-connection tests."
}
finally {
    Pop-Location
}
