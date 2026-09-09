#Requires -Version 7.2
# Windows: produce faithful installer images from the supplied game screenshots.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskAssetRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'

function Write-Image([string]$Source, [string]$Destination, [Drawing.Rectangle]$Crop,
    [int]$Width, [int]$Height, [switch]$Contain) {
    $taskSource = [Drawing.Bitmap]::new((Join-Path $taskAssetRoot $Source))
    $taskOutput = [Drawing.Bitmap]::new($Width, $Height)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskOutput)
    try {
        $taskGraphics.Clear($taskSource.GetPixel(0, 0))
        $taskGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $taskGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $taskDraw = [Drawing.Rectangle]::new(0, 0, $Width, $Height)
        if ($Contain) {
            $taskScale = [Math]::Min($Width / $Crop.Width, $Height / $Crop.Height)
            $taskDraw.Width = [int][Math]::Round($Crop.Width * $taskScale)
            $taskDraw.Height = [int][Math]::Round($Crop.Height * $taskScale)
            $taskDraw.X = [int](($Width - $taskDraw.Width) / 2)
            $taskDraw.Y = [int](($Height - $taskDraw.Height) / 2)
        }
        $taskGraphics.DrawImage($taskSource, $taskDraw, $Crop, [Drawing.GraphicsUnit]::Pixel)
        $taskOutput.Save((Join-Path $taskAssetRoot $Destination), [Drawing.Imaging.ImageFormat]::Png)
        Write-Output "$Destination : ${Width}x${Height}"
    }
    finally { $taskGraphics.Dispose(); $taskOutput.Dispose(); $taskSource.Dispose() }
}

Write-Image 'source/icon.png' 'icon.png' ([Drawing.Rectangle]::new(0, 0, 126, 133)) 256 256 -Contain
Write-Image 'source/uncaptured-beast.png' 'screenshots/uncaptured-overview.png' ([Drawing.Rectangle]::new(0, 0, 612, 542)) 429 380
Write-Image 'source/uncaptured-beast.png' 'screenshots/uncaptured-detail.png' ([Drawing.Rectangle]::new(140, 170, 400, 230)) 600 345
