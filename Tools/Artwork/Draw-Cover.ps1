param(
    [string]$Output = (Join-Path $PSScriptRoot '../../Docs/Images/RimUnity-Cover.png'),
    [string]$PreviewOutput = (Join-Path $PSScriptRoot '../../About/Preview.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

#使用矢量轮廓与程序纹理绘制封面，不需要外部图片或字体。
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class RimUnityCover
{
    static readonly Random Random = new Random(9413);
    static readonly Color Dark = Color.FromArgb(8, 14, 22);

    //将坐标对转换为闭合轮廓。
    static void Polygon(GraphicsPath path, params float[] xy)
    {
        PointF[] points = new PointF[xy.Length / 2];
        for (int i = 0; i < points.Length; i++) points[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
        path.AddPolygon(points);
    }

    //为标题绘制宽体几何字形。
    static GraphicsPath Letter(char value)
    {
        var p = new GraphicsPath(FillMode.Alternate);
        switch (value)
        {
            case 'R':
                p.StartFigure(); p.AddLine(0, 170, 0, 0); p.AddLine(0, 0, 71, 0);
                p.AddBezier(71, 0, 134, 0, 141, 82, 86, 101);
                p.AddLine(86, 101, 132, 176); p.AddLine(132, 176, 94, 176);
                p.AddLine(94, 176, 48, 105); p.AddLine(48, 105, 32, 105);
                p.AddLine(32, 105, 32, 170); p.CloseFigure();
                p.StartFigure(); p.AddLine(32, 30, 69, 30);
                p.AddBezier(69, 30, 103, 30, 103, 76, 69, 76);
                p.AddLine(69, 76, 32, 76); p.CloseFigure(); break;
            case 'I': Polygon(p, 0,0, 33,0, 33,170, 16,170, 0,154); break;
            case 'M': Polygon(p, 0,170, 0,-3, 75,77, 150,-3, 150,170, 117,170, 117,75, 75,121, 33,75, 33,170); break;
            case 'U':
                p.StartFigure(); p.AddLine(0, 0, 33, 0); p.AddLine(33, 0, 33, 110);
                p.AddBezier(33,110, 33,154, 101,154, 101,110);
                p.AddLine(101,110, 101,0); p.AddLine(101,0, 134,0); p.AddLine(134,0, 134,111);
                p.AddBezier(134,111, 134,192, 0,192, 0,111); p.CloseFigure(); break;
            case 'N': Polygon(p, 0,170, 0,0, 29,0, 108,113, 108,0, 140,0, 140,170, 112,170, 32,56, 32,170); break;
            case 'T': Polygon(p, 0,0, 131,0, 131,31, 82,31, 82,170, 49,170, 49,31, 0,31); break;
            case 'Y': Polygon(p, 0,0, 37,0, 78,69, 119,0, 156,0, 95,103, 95,170, 61,170, 61,103); break;
        }
        return p;
    }

    //生成整行标题，使字距与最终宽度保持一致。
    static GraphicsPath Title(float x, float y, float width)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        float cursor = 0;
        foreach (char c in "RIMUNITY")
        {
            using (var letter = Letter(c))
            using (var move = new Matrix())
            {
                move.Translate(cursor, 0); letter.Transform(move); path.AddPath(letter, false);
                cursor = letter.GetBounds().Right + 27;
            }
        }
        float scale = width / path.GetBounds().Width;
        using (var place = new Matrix(scale, 0, 0, scale, x, y)) path.Transform(place);
        return path;
    }

    //依据 Unity 官方方块图标的几何比例绘制。
    //参考：https://unity.com/legal/branding-trademarks
    static GraphicsPath UnityCube(float x, float y, float height)
    {
        var p = new GraphicsPath();
        Polygon(p, 40,0, 40,17.5f, 23.5f,27.2f, 44.2f,39.6f,
            64.7f,27.2f, 47.5f,17.5f, 47.5f,0, 88.5f,23, 88.5f,69.2f,
            72.8f,60.4f, 72.8f,41.4f, 51.3f,53.3f, 51.3f,76.4f,
            68.5f,66.8f, 84.5f,76.1f, 44.2f,99.5f, 3.7f,76.1f,
            19.1f,66.8f, 36.2f,76.4f, 36.2f,53.3f, 15.1f,41.4f,
            15.1f,60.4f, 0,69.7f, 0,23);
        float s = height / 99.5f;
        using (var m = new Matrix(s, 0, 0, s, x, y)) p.Transform(m);
        return p;
    }

    //绘制柔和的局部环境光。
    static void Glow(Graphics g, float x, float y, float w, float h, Color color)
    {
        using (var p = new GraphicsPath())
        {
            p.AddEllipse(x, y, w, h);
            using (var b = new PathGradientBrush(p))
            {
                b.CenterColor = color; b.SurroundColors = new[] { Color.FromArgb(0, color) };
                g.FillPath(b, p);
            }
        }
    }

    //在轮廓内叠加细微磨损，保持缩略图的可读性。
    static void Wear(Graphics g, GraphicsPath path, int count)
    {
        var saved = g.Save(); g.SetClip(path);
        RectangleF bounds = path.GetBounds();
        for (int i = 0; i < count; i++)
        {
            float x = bounds.X + (float)Random.NextDouble() * bounds.Width;
            float y = bounds.Y + (float)Random.NextDouble() * bounds.Height;
            float size = (float)Random.NextDouble() * 2.3f + .3f;
            using (var brush = new SolidBrush(Color.FromArgb(Random.Next(30, 100), 14, 24, 34)))
                g.FillEllipse(brush, x, y, size * 2, size);
        }
        for (int i = 0; i < 50; i++)
        {
            float x = bounds.X + (float)Random.NextDouble() * bounds.Width;
            float y = bounds.Y + (float)Random.NextDouble() * bounds.Height;
            using (var pen = new Pen(Color.FromArgb(28, 11, 22, 31), .7f))
                g.DrawLine(pen, x, y, x + Random.Next(4, 18), y - Random.Next(1, 5));
        }
        g.Restore(saved);
    }

    //用深色厚度、窄倒角和冷灰渐变组成金属字面。
    static void Metal(Graphics g, GraphicsPath path)
    {
        RectangleF b = path.GetBounds();
        using (var back = (GraphicsPath)path.Clone())
        using (var move = new Matrix())
        using (var edge = new Pen(Color.FromArgb(4, 8, 13), 7))
        using (var depth = new SolidBrush(Color.FromArgb(26, 38, 47)))
        {
            move.Translate(0, 8); back.Transform(move);
            g.DrawPath(edge, back); g.FillPath(depth, back);
        }
        using (var bevel = new Pen(Color.FromArgb(115, 128, 133), 2.4f)) g.DrawPath(bevel, path);
        using (var metal = new LinearGradientBrush(b, Color.White, Color.Gray, 90))
        {
            metal.InterpolationColors = new ColorBlend {
                Colors = new[] { Color.FromArgb(247,249,244), Color.FromArgb(211,219,220), Color.FromArgb(154,170,179), Color.FromArgb(104,123,137) },
                Positions = new[] { 0f, .30f, .68f, 1f }
            };
            g.FillPath(metal, path);
        }
        Wear(g, path, 1150);
    }

    //以双倍尺寸绘制，再缩小到发布用 PNG。
    public static void Draw(string output, string previewOutput)
    {
        using (var canvas = new Bitmap(3840, 2160, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.ScaleTransform(2, 2); g.Clear(Dark);
            using (var bg = new LinearGradientBrush(new Rectangle(0,0,1920,1080), Color.FromArgb(16,29,42), Dark, 75))
                g.FillRectangle(bg, 0,0,1920,1080);
            Glow(g, 180,140,1660,740, Color.FromArgb(95,54,77,91));
            Glow(g, -580,480,1460,970, Color.FromArgb(66,139,100,69));
            for (int i = 0; i < 185; i++)
            {
                float x = Random.Next(28,1892), y = Random.Next(28,1052);
                float r = i % 17 == 0 ? 1.5f : .65f;
                using (var star = new SolidBrush(Color.FromArgb(Random.Next(25,95), 194,209,219)))
                    g.FillEllipse(star, x,y,r,r);
            }
            //轨道弧线与远处的行星边缘保持低对比。
            using (var orbit = new Pen(Color.FromArgb(20,145,171,186), 1))
                g.DrawEllipse(orbit, 130,-340,1750,1750);
            Glow(g, -410,755,1720,950, Color.FromArgb(52,83,121,142));
            using (var planet = new LinearGradientBrush(new Rectangle(0,700,1920,800), Color.FromArgb(13,27,36), Color.FromArgb(4,10,17),90))
                g.FillEllipse(planet,-390,856,1710,1140);
            using (var rim = new Pen(Color.FromArgb(54,111,146,160), 1.2f))
                g.DrawEllipse(rim,-390,856,1710,1140);

            //Unity 徽记与标题共享水平中线。
            using (var ring = new GraphicsPath(FillMode.Alternate))
            {
                ring.AddEllipse(156,357,354,354); ring.AddEllipse(176,377,314,314);
                Metal(g, ring);
            }
            using (var icon = UnityCube(238,426,216)) g.FillPath(Brushes.White, icon);
            using (var title = Title(568,438,1200)) Metal(g, title);

            using (var result = new Bitmap(1920,1080,PixelFormat.Format24bppRgb))
            using (var final = Graphics.FromImage(result))
            {
                final.InterpolationMode = InterpolationMode.HighQualityBicubic;
                final.DrawImage(canvas, 0,0,1920,1080);
                result.Save(output, ImageFormat.Png);
            }
            using (var preview = new Bitmap(1280,720,PixelFormat.Format24bppRgb))
            using (var final = Graphics.FromImage(preview))
            {
                final.InterpolationMode = InterpolationMode.HighQualityBicubic;
                final.DrawImage(canvas, 0,0,1280,720);
                preview.Save(previewOutput, ImageFormat.Png);
            }
        }
    }
}
'@

$target = [System.IO.Path]::GetFullPath($Output)
$previewTarget = [System.IO.Path]::GetFullPath($PreviewOutput)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($previewTarget)) | Out-Null
[RimUnityCover]::Draw($target, $previewTarget)
Write-Output $target
Write-Output $previewTarget
