# Generates src/DisplayApp/app.ico (16, 24, 32, 48, 256 px PNG frames): a monitor with an on/off badge.
# Run from the repo root:  powershell -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $u = $s / 32.0
    $frame  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 60, 64, 72))
    $screen = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 40, 120, 220))
    $badge  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 46, 180, 90))
    $white  = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.0, 1.6 * $u))

    $g.FillRectangle($frame, 1 * $u, 4 * $u, 30 * $u, 20 * $u)       # bezel
    $g.FillRectangle($screen, 3 * $u, 6 * $u, 26 * $u, 16 * $u)      # screen
    $g.FillRectangle($frame, 13 * $u, 24 * $u, 6 * $u, 3 * $u)       # neck
    $g.FillRectangle($frame, 8 * $u, 27 * $u, 16 * $u, 3 * $u)       # foot
    if ($s -ge 24) {
        $g.FillEllipse($badge, 17 * $u, 13 * $u, 14 * $u, 14 * $u)   # power badge
        $g.DrawArc($white, 20.5 * $u, 16.5 * $u, 7 * $u, 7 * $u, -60, 300)
        $g.DrawLine($white, 24 * $u, 15.5 * $u, 24 * $u, 20 * $u)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 256
$frames = $sizes | ForEach-Object { ,(New-Frame $_) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                  # ICONDIRENTRY
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$frames[$i].Length); $w.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
$path = Join-Path (Get-Location) "src/DisplayApp/app.ico"
[System.IO.File]::WriteAllBytes($path, $out.ToArray())
"Wrote $path ($($out.Length) bytes)"
