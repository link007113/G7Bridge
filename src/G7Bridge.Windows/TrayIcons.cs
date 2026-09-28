using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using G7Bridge.Core;

namespace G7Bridge.Windows;

// Draws the tray battery glyph. Icons are built as in-memory PNG ICO files, so
// no GDI icon handles need manual destruction.
internal static class TrayIcons
{
    private static readonly Color Active=Color.FromArgb(46,157,82),Waiting=Color.FromArgb(138,143,152),
        Off=Color.FromArgb(120,138,143,152),Attention=Color.FromArgb(217,130,43),Low=Color.FromArgb(214,69,69);
    private static readonly int[] Sizes=[16,20,24,32,48];
    internal static Icon Create(TrayView view)
    {
        var images=Sizes.Select(size=>{using var bitmap=Draw(view,size);using var png=new MemoryStream();bitmap.Save(png,ImageFormat.Png);return png.ToArray();}).ToArray();
        using var ico=new MemoryStream();using var writer=new BinaryWriter(ico);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)Sizes.Length);
        int offset=6+16*Sizes.Length;
        for(int i=0;i<Sizes.Length;i++) {
            writer.Write((byte)Sizes[i]);writer.Write((byte)Sizes[i]);writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;
        }
        foreach(var image in images)writer.Write(image);
        writer.Flush();ico.Position=0;
        return new Icon(ico,SystemInformation.SmallIconSize);
    }
    internal static Bitmap Draw(TrayView view,int size)
    {
        var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using var g=Graphics.FromImage(bitmap);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        var color=view.Kind switch {
            TrayKind.Active=>view.Battery is <=20 && !view.Charging?Low:Active,
            TrayKind.Attention=>Attention,TrayKind.Off=>Off,_=>Waiting
        };
        float unit=size/16f,stroke=Math.Max(1.25f,1.5f*unit);
        var body=new RectangleF(unit+stroke/2,4*unit+stroke/2,12.5f*unit-stroke,8*unit-stroke);
        using(var pen=new Pen(color,stroke))g.DrawRectangle(pen,body.X,body.Y,body.Width,body.Height);
        using(var brush=new SolidBrush(color)) {
            g.FillRectangle(brush,13.5f*unit,6.5f*unit,1.5f*unit,3*unit);
            float gap=Math.Max(0.5f,0.75f*unit)+stroke/2;
            var inner=RectangleF.Inflate(body,-gap,-gap);
            float level=view.Kind switch {TrayKind.Active=>Math.Max((view.Battery??100)/100f,0.12f),TrayKind.Attention=>1f,_=>0f};
            if(level>0)g.FillRectangle(brush,inner.X,inner.Y,inner.Width*level,inner.Height);
        }
        if(view.Kind==TrayKind.Attention)DrawMark(g,unit);
        else if(view.Kind==TrayKind.Active && view.Charging)DrawBolt(g,unit);
        return bitmap;
    }
    private static void DrawBolt(Graphics g,float unit)
    {
        PointF[] bolt=[new(8.4f*unit,5.3f*unit),new(5.8f*unit,8.4f*unit),new(7.5f*unit,8.4f*unit),new(6.8f*unit,10.7f*unit),new(9.6f*unit,7.4f*unit),new(7.9f*unit,7.4f*unit)];
        // An outline only helps where there are enough pixels to show it.
        if(unit>=1.5f){using var outline=new Pen(Color.FromArgb(40,40,40),unit*0.75f);g.DrawPolygon(outline,bolt);}
        g.FillPolygon(Brushes.White,bolt);
    }
    private static void DrawMark(Graphics g,float unit)
    {
        using var font=new Font("Segoe UI",7f*unit,FontStyle.Bold,GraphicsUnit.Pixel);
        using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
        g.DrawString("!",font,Brushes.White,new RectangleF(0,3.5f*unit,14f*unit,9f*unit),format);
    }
    // Layout check only: every state at 16 and 32 px on a dark and a light taskbar.
    internal static void RenderSheet(string path)
    {
        TrayView[] views=[new(TrayKind.Off,null,false),new(TrayKind.Waiting,null,false),new(TrayKind.Active,64,false),
            new(TrayKind.Active,15,false),new(TrayKind.Active,40,true),new(TrayKind.Attention,null,false)];
        const int cell=40,band=60;
        using var sheet=new Bitmap(views.Length*cell+8,band*2,PixelFormat.Format32bppArgb);
        using var g=Graphics.FromImage(sheet);
        Color[] backgrounds=[Color.FromArgb(32,32,32),Color.FromArgb(243,243,243)];
        for(int b=0;b<backgrounds.Length;b++) {
            using(var brush=new SolidBrush(backgrounds[b]))g.FillRectangle(brush,0,b*band,sheet.Width,band);
            for(int i=0;i<views.Length;i++) {
                using var small=Draw(views[i],16);g.DrawImageUnscaled(small,4+i*cell,b*band+4);
                using var large=Draw(views[i],32);g.DrawImageUnscaled(large,4+i*cell,b*band+24);
            }
        }
        sheet.Save(path,ImageFormat.Png);
    }
}
