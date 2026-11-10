param([switch] $Verification)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$source = Join-Path $PSScriptRoot 'src'
$stage = Join-Path $PSScriptRoot '.build'
$release = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Force -Path $stage, $release | Out-Null
Add-Type -AssemblyName System.Drawing

function New-BellIcon([string] $OutputPath, [bool] $Muted) {
    $entries = foreach ($size in 16, 24, 32, 48, 256) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $drawing = [Drawing.Graphics]::FromImage($bitmap)
        $buffer = [IO.MemoryStream]::new()
        $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($(if ($Muted) { '#635563' } else { '#9F3857' })))
        $bell = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#FFCDAD'))
        $ink = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#492737'), 8)
        $bellPath = [Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $drawing.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $drawing.ScaleTransform($size / 256.0, $size / 256.0)
            $drawing.FillEllipse($background, 6, 6, 244, 244)
            $drawing.FillEllipse($bell, 110, 48, 36, 34)
            $drawing.FillEllipse($bell, 106, 168, 44, 35)
            $bellPath.AddBezier(62, 158, 83, 147, 70, 75, 128, 70)
            $bellPath.AddBezier(128, 70, 186, 75, 173, 147, 194, 158)
            $bellPath.AddBezier(194, 158, 211, 170, 193, 184, 181, 184)
            $bellPath.AddLine(181, 184, 75, 184)
            $bellPath.AddBezier(75, 184, 63, 184, 45, 170, 62, 158)
            $bellPath.CloseFigure()
            $drawing.FillPath($bell, $bellPath)
            $ink.StartCap = $ink.EndCap = [Drawing.Drawing2D.LineCap]::Round
            if ($Muted) {
                $drawing.DrawArc($ink, 96, 113, 19, 13, 0, 180)
                $drawing.DrawArc($ink, 140, 113, 19, 13, 0, 180)
            } else {
                $drawing.DrawLine($ink, 106, 116, 106, 123)
                $drawing.DrawLine($ink, 150, 116, 150, 123)
            }
            $drawing.DrawArc($ink, 119, 132, 18, 13, 0, 180)
            $bitmap.Save($buffer, [Drawing.Imaging.ImageFormat]::Png)
            [pscustomobject]@{ Size = $size; Bytes = $buffer.ToArray() }
        } finally {
            $bellPath.Dispose()
            $ink.Dispose()
            $bell.Dispose()
            $background.Dispose()
            $buffer.Dispose()
            $drawing.Dispose()
            $bitmap.Dispose()
        }
    }

    $writer = [IO.BinaryWriter]::new([IO.File]::Create($OutputPath))
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$entries.Count)
        $offset = 6 + 16 * $entries.Count
        foreach ($entry in $entries) {
            $writer.Write([byte]($entry.Size % 256))
            $writer.Write([byte]($entry.Size % 256))
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$entry.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $entry.Bytes.Length
        }
        foreach ($entry in $entries) { $writer.Write([byte[]]$entry.Bytes) }
    } finally { $writer.Dispose() }
}

$icon = Join-Path $stage 'Bell.ico'
$mutedIcon = Join-Path $stage 'Muted.ico'
New-BellIcon $icon $false
New-BellIcon $mutedIcon $true

$app = Join-Path $release 'SlouchBell.exe'
$installer = Join-Path $release 'SlouchBell-Setup.exe'
$common = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    "/win32manifest:$source\app.manifest", "/win32icon:$icon")
$references = foreach ($assembly in 'WindowsBase', 'PresentationCore', 'PresentationFramework', 'UIAutomationTypes', 'UIAutomationProvider', 'UIAutomationClient') {
    "/reference:$wpf\$assembly.dll"
}
$files = foreach ($file in 'App.cs', 'Core.cs', 'Cozy.cs', 'Dashboard.cs', 'Reminder.cs', 'SelfTest.cs') {
    Join-Path $source $file
}
& $compiler $common "/out:$app" $references '/r:System.Drawing.dll' '/r:System.Windows.Forms.dll' '/r:System.Xaml.dll' '/r:System.Xml.dll' '/r:System.Runtime.Serialization.dll' "/resource:$source\Messages.txt,Messages.txt" "/resource:$mutedIcon,Muted.ico" $files
if ($LASTEXITCODE -ne 0) { throw 'SlouchBell compilation failed.' }
& $compiler $common "/out:$installer" '/r:System.Drawing.dll' '/r:System.Windows.Forms.dll' '/r:Microsoft.CSharp.dll' "/resource:$app,SlouchBell.exe" (Join-Path $source 'Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
if ($Verification) {
    $checker = Join-Path $stage 'SetupCheck.exe'
    & $compiler $common '/define:SETUP_TEST' "/out:$checker" '/r:System.Drawing.dll' '/r:System.Windows.Forms.dll' '/r:Microsoft.CSharp.dll' "/resource:$app,SlouchBell.exe" (Join-Path $source 'Setup.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Installer verification build failed.' }
}
Get-Item -LiteralPath $installer, $app | Select-Object Name, Length, FullName
