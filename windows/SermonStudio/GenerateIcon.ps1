param([string]$OutputPath)
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(256,256)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(23,40,60))
$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(224,192,126),10)
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
function Line($x1,$y1,$x2,$y2) { $g.DrawLine($pen,[int]$x1,[int]$y1,[int]$x2,[int]$y2) }
Line 128 25 128 89
Line 101 48 155 48
Line 30 99 78 84
Line 78 84 128 106
Line 128 106 178 84
Line 178 84 226 99
Line 226 99 226 205
Line 226 205 178 190
Line 178 190 128 216
Line 128 216 78 190
Line 78 190 30 205
Line 30 205 30 99
Line 128 106 128 216
Line 48 126 78 117
Line 78 117 107 132
Line 149 132 178 117
Line 178 117 208 126
$stream = [System.IO.MemoryStream]::new()
$bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
$png = $stream.ToArray()
$file = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
  $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
  $writer.Write([byte[]]@(0,0,0,0)); $writer.Write([uint16]1); $writer.Write([uint16]32)
  $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png)
} finally { $writer.Dispose(); $stream.Dispose(); $pen.Dispose(); $g.Dispose(); $bitmap.Dispose() }
