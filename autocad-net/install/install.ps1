<#
Cai CivilEngi AcadBridge (plugin AutoCAD 2023) + MCP server "autocad-net".
Chay trong PowerShell tu thu muc repo:   powershell -ExecutionPolicy Bypass -File autocad-net\install\install.ps1
Tuy chon:
  -SkipBuild        dung DLL da build san (plugin\CivilEngi.AcadBridge\bin\Release\CivilEngi.AcadBridge.dll)
  -SkipMcp          khong dang ky MCP voi Claude Code
  -Python <exe>     python dung cho MCP server (mac dinh: python tren PATH)
Viec script lam:
  1. dotnet build -c Release (can .NET SDK 6+; tu tai goi AutoCAD.NET 24.2 tu nuget.org)
  2. Copy DLL vao %APPDATA%\Autodesk\ApplicationPlugins\CivilEngiAcadBridge.bundle (AutoCAD tu nap khi mo)
  3. pip install mcp (neu chua co)
  4. claude mcp add --scope user autocad-net -- <python> <repo>\autocad-net\mcp-server\server.py
Khong dung toi server COM cu ("autocad"), co the dung song song.
#>
param([switch]$SkipBuild, [switch]$SkipMcp, [string]$Python = "python")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot            # ...\autocad-net
$proj = Join-Path $root "plugin\CivilEngi.AcadBridge"
$dll  = Join-Path $proj "bin\Release\CivilEngi.AcadBridge.dll"

Write-Host "== 1. Build plugin" -ForegroundColor Cyan
if (-not $SkipBuild) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "Chua co .NET SDK. Cai: winget install Microsoft.DotNet.SDK.8  (roi mo lai PowerShell)"
    }
    & dotnet build $proj -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build loi" }
}
if (-not (Test-Path $dll)) { throw "Khong thay $dll" }

Write-Host "== 2. Cai bundle AutoCAD" -ForegroundColor Cyan
$bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\CivilEngiAcadBridge.bundle"
$contents = Join-Path $bundle "Contents"
New-Item -ItemType Directory -Force -Path $contents | Out-Null
Copy-Item (Join-Path $root "plugin\bundle\PackageContents.xml") $bundle -Force
try {
    Copy-Item $dll $contents -Force
} catch {
    Write-Warning "Khong ghi de duoc DLL (AutoCAD dang mo va da nap plugin?). Dong AutoCAD roi chay lai script."
    throw
}
Write-Host "   -> $bundle"

if (-not $SkipMcp) {
    Write-Host "== 3. Python + mcp" -ForegroundColor Cyan
    & $Python -c "import mcp" 2>$null
    if ($LASTEXITCODE -ne 0) { & $Python -m pip install --user "mcp>=1.2"; if ($LASTEXITCODE -ne 0) { throw "pip install mcp loi" } }
    $pyExe = (& $Python -c "import sys;print(sys.executable)").Trim()
    $server = Join-Path $root "mcp-server\server.py"

    Write-Host "== 4. Dang ky MCP 'autocad-net' cho Claude Code (scope user)" -ForegroundColor Cyan
    if (Get-Command claude -ErrorAction SilentlyContinue) {
        & claude mcp remove --scope user autocad-net 2>$null | Out-Null
        & claude mcp add --scope user autocad-net -- "$pyExe" "$server"
    } else {
        Write-Warning "Khong thay lenh 'claude'. Tu them vao ~/.claude.json muc mcpServers:"
        Write-Host ('  "autocad-net": { "command": "' + $pyExe.Replace('\','\\') + '", "args": ["' + $server.Replace('\','\\') + '"] }')
    }
}

Write-Host ""
Write-Host "XONG. Mo (hoac mo lai) AutoCAD 2023 -> dong lenh hien 'CivilEngi AcadBridge ... san sang'. Go CEBRIDGE de kiem tra." -ForegroundColor Green
Write-Host "Neu AutoCAD dang mo: NETLOAD $contents\CivilEngi.AcadBridge.dll de dung ngay (khong can mo lai)."
Write-Host "Thu nhanh: $Python -m acad_bridge status   (chay trong thu muc $root\mcp-server)"
