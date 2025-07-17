param([switch] $Clean)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot/..
try
{
    $submodule = 'modules/sentry-native'
    $sourceDir = 'src/Sentry/Platforms/Native'
    $outDir = "$sourceDir/sentry-native"
    $buildDir = "$submodule/build"
    $actualBuildDir = "$buildDir/sentry-native"

    $libPrefix = 'lib'
    $libExtension = '.a'
    if ($IsMacOS)
    {
        $outDir += '/osx'
    }
    elseif ($IsWindows)
    {
        $actualBuildDir = "$buildDir/RelWithDebInfo"
        $libPrefix = ''
        $libExtension = '.lib'

        if ("Arm64".Equals([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()))
        {
            $outDir += '/win-arm64'
        }
        else
        {
            $outDir += '/win-x64'
        }
    }
    elseif ($IsLinux)
    {
        $musl = (ldd --version 2>&1) -match 'musl'
        $arm64 = ("Arm64".Equals([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()))
        if ($musl -and $arm64)
        {
            $outDir += '/linux-musl-arm64'
        }
        elseif ($arm64)
        {
            $outDir += '/linux-arm64'
        }
        elseif ($musl)
        {
            $outDir += '/linux-musl-x64'
        }
        else
        {
            $outDir += '/linux-x64'
        }
    }
    else
    {
        throw "Unsupported platform"
    }

    git submodule update --init --recursive $submodule

    if ($Clean)
    {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $buildDir
    }

    cmake `
        -S $sourceDir `
        -B $buildDir `
        -D CMAKE_BUILD_TYPE=RelWithDebInfo

    cmake `
        --build $buildDir `
        --target sentry `
        --config RelWithDebInfo `
        --parallel

    $srcFile = "$actualBuildDir/${libPrefix}sentry$libExtension"
    $outFile = "$outDir/${libPrefix}sentry-native$libExtension"

    # New-Item creates the directory if it doesn't exist.
    New-Item -ItemType File -Path $outFile -Force | Out-Null

    Write-Host "Copying $srcFile to $outFile"
    Copy-Item -Force -Path $srcFile -Destination $outFile

    # Touch the file to mark it as up-to-date for MSBuild
    (Get-Item $outFile).LastWriteTime = Get-Date
}
finally
{
    Pop-Location
}
