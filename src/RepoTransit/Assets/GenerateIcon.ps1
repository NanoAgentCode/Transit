Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform([single]($size / 256.0), [single]($size / 256.0))
    $blue = [System.Drawing.ColorTranslator]::FromHtml('#0067C0')
    $pale = [System.Drawing.ColorTranslator]::FromHtml('#B9DDF6')
    $background = [System.Drawing.SolidBrush]::new($blue)
    $paper = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $fold = [System.Drawing.SolidBrush]::new($pale)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(16, 16, 96, 96, 180, 90)
    $path.AddArc(144, 16, 96, 96, 270, 90)
    $path.AddArc(144, 144, 96, 96, 0, 90)
    $path.AddArc(16, 144, 96, 96, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($background, $path)
    $graphics.FillPolygon($paper, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(74, 50), [System.Drawing.PointF]::new(158, 50),
        [System.Drawing.PointF]::new(186, 78), [System.Drawing.PointF]::new(186, 204),
        [System.Drawing.PointF]::new(74, 204)))
    $graphics.FillPolygon($fold, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(158, 50), [System.Drawing.PointF]::new(158, 78),
        [System.Drawing.PointF]::new(186, 78)))
    $arrow = [System.Drawing.Pen]::new($blue, 16)
    $arrow.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arrow.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arrow.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLines($arrow, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(96, 157), [System.Drawing.PointF]::new(153, 104)))
    $graphics.DrawLines($arrow, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(126, 103), [System.Drawing.PointF]::new(154, 104),
        [System.Drawing.PointF]::new(150, 133)))
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    $stream.Dispose()
    $arrow.Dispose()
    $path.Dispose()
    $fold.Dispose()
    $paper.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$destination = Join-Path $PSScriptRoot 'RepoTransit.ico'
$output = [System.IO.File]::Create($destination)
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    for ($i = 0; $i -lt $images.Count; $i++) {
        $edge = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$edge)
        $writer.Write([byte]$edge)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally {
    $writer.Dispose()
    $output.Dispose()
}
