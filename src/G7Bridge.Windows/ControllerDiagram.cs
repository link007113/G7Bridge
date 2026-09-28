using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.ComponentModel;
using G7Bridge.Core;
using L = G7Bridge.Core.UiLanguage;

namespace G7Bridge.Windows;

internal sealed class ControllerDiagram : Control
{
    private VirtualInputSnapshot frame;
    private static readonly Color Accent=Color.FromArgb(21,128,101);
    private static readonly Color Ink=Color.FromArgb(35,49,65);
    private static readonly Color Muted=Color.FromArgb(101,116,134);
    [Browsable(false),DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal VirtualInputSnapshot Frame
    {
        get=>frame;
        set {if(frame==value)return;frame=value;Invalidate();}
    }
    internal ControllerDiagram()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        TabStop=true;
        BackColor=Color.FromArgb(247,249,252);MinimumSize=new(580,380);Dock=DockStyle.Fill;
        AccessibleName=L.Text("Virtual controller input","Invoer van de virtuele controller");
        AccessibleRole=AccessibleRole.Graphic;
    }
    // Steam's desktop layout may translate gamepad presses to Enter/Space/arrows.
    // Keep those on the read-only canvas instead of a focused action button.
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Enter or Keys.Space or Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) {e.Handled=true;e.SuppressKeyPress=true;base.OnKeyDown(e);}
    protected override void OnMouseDown(MouseEventArgs e) {Focus();base.OnMouseDown(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        float scale=Math.Min(ClientSize.Width/700f,ClientSize.Height/455f);
        g.TranslateTransform((ClientSize.Width-700*scale)/2,(ClientSize.Height-455*scale)/2);g.ScaleTransform(scale,scale);
        using var small=new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel);
        using var label=new Font("Segoe UI",14,FontStyle.Bold,GraphicsUnit.Pixel);
        using var face=new Font("Segoe UI",18,FontStyle.Bold,GraphicsUnit.Pixel);
        using var body=new GraphicsPath();
        body.AddClosedCurve([new PointF(168,86),new(120,103),new(89,186),new(83,277),new(109,316),new(142,317),new(184,270),new(242,283),new(458,283),new(516,270),new(558,317),new(591,316),new(617,277),new(611,186),new(580,103),new(532,86)],0.32f);
        using(var fill=new SolidBrush(Color.FromArgb(230,236,242)))g.FillPath(fill,body);
        using(var outline=new Pen(Color.FromArgb(177,188,201),2))g.DrawPath(outline,body);

        var pad=frame.Fresh?frame.Pad:default;
        Trigger(g,new(137,12,112,32),"LT",pad.LT,label,small);
        Trigger(g,new(451,12,112,32),"RT",pad.RT,label,small);
        Button(g,new(126,56,134,34),"LB",pad.Buttons.HasFlag(PadButtons.LB),label);
        Button(g,new(440,56,134,34),"RB",pad.Buttons.HasFlag(PadButtons.RB),label);
        Stick(g,new(188,157),pad.LX,pad.LY,"LS / L3",pad.Buttons.HasFlag(PadButtons.L3),small);
        Stick(g,new(441,244),pad.RX,pad.RY,"RS / R3",pad.Buttons.HasFlag(PadButtons.R3),small);

        const float dX=253,dY=236;
        Button(g,new(dX-13,dY-40,26,26),"↑",pad.Buttons.HasFlag(PadButtons.Up),face);
        Button(g,new(dX-13,dY+14,26,26),"↓",pad.Buttons.HasFlag(PadButtons.Down),face);
        Button(g,new(dX-40,dY-13,26,26),"←",pad.Buttons.HasFlag(PadButtons.Left),face);
        Button(g,new(dX+14,dY-13,26,26),"→",pad.Buttons.HasFlag(PadButtons.Right),face);

        Button(g,new(512,177,36,36),"A",pad.Buttons.HasFlag(PadButtons.A),face,true);
        Button(g,new(548,141,36,36),"B",pad.Buttons.HasFlag(PadButtons.B),face,true);
        Button(g,new(476,141,36,36),"X",pad.Buttons.HasFlag(PadButtons.X),face,true);
        Button(g,new(512,105,36,36),"Y",pad.Buttons.HasFlag(PadButtons.Y),face,true);
        Button(g,new(320,110,60,30),"Guide",pad.Buttons.HasFlag(PadButtons.Guide),small);
        Button(g,new(296,168,46,30),"View",pad.Buttons.HasFlag(PadButtons.View),small);
        Button(g,new(358,168,46,30),"Menu",pad.Buttons.HasFlag(PadButtons.Menu),small);
        Button(g,new(320,216,60,30),"Share",pad.Buttons.HasFlag(PadButtons.Share),small);

        DrawLabel(g,L.Text("Independent extra buttons","Onafhankelijke extra knoppen"),new(140,326,420,22),small,Muted);
        Button(g,new(176,352,76,32),"L4",pad.Buttons.HasFlag(PadButtons.L4),label);
        Button(g,new(265,352,76,32),"L5",pad.Buttons.HasFlag(PadButtons.L5),label);
        Button(g,new(359,352,76,32),"R5",pad.Buttons.HasFlag(PadButtons.R5),label);
        Button(g,new(448,352,76,32),"R4",pad.Buttons.HasFlag(PadButtons.R4),label);
        var motion=frame.Fresh?frame.Motion:default;
        DrawLabel(g,L.Text("Gyro · angular speed (°/s)","Gyro · draaisnelheid (°/s)"),new(140,394,420,20),small,Muted);
        Gyro(g,new(34,417,190,30),"X",motion.GX,small);
        Gyro(g,new(255,417,190,30),"Y",motion.GY,small);
        Gyro(g,new(476,417,190,30),"Z",motion.GZ,small);
    }
    private static void Button(Graphics g,RectangleF box,string title,bool down,Font font,bool round=false)
    {
        using var path=Shape(box,round?box.Height/2:8);
        using(var brush=new SolidBrush(down?Accent:Color.White))g.FillPath(brush,path);
        using(var pen=new Pen(down?Color.FromArgb(11,86,68):Color.FromArgb(160,174,191),down?3:1.3f))g.DrawPath(pen,path);
        DrawLabel(g,title,box,font,down?Color.White:Ink);
    }
    private static void Trigger(Graphics g,RectangleF box,string name,ushort raw,Font label,Font small)
    {
        float amount=VirtualPadState.Trigger(raw);
        using(var path=Shape(box,8)) {
            using(var fill=new SolidBrush(Color.White))g.FillPath(fill,path);
            using(var outline=new Pen(Color.FromArgb(160,174,191),1.3f))g.DrawPath(outline,path);
        }
        if(amount>0)using(var fill=new SolidBrush(Color.FromArgb(169,224,209)))g.FillRectangle(fill,box.X+3,box.Y+3,(box.Width-6)*amount,box.Height-6);
        DrawLabel(g,name,new(box.X+4,box.Y,34,box.Height),label,Ink);
        DrawLabel(g,$"{Math.Round(amount*100):0}%",new(box.X+43,box.Y,box.Width-47,box.Height),small,Ink);
    }
    private static void Stick(Graphics g,PointF center,short x,short y,string name,bool click,Font font)
    {
        var box=new RectangleF(center.X-40,center.Y-40,80,80);
        using(var fill=new SolidBrush(click?Color.FromArgb(192,237,221):Color.FromArgb(250,251,253)))g.FillEllipse(fill,box);
        using(var pen=new Pen(click?Accent:Color.FromArgb(160,174,191),click?3:1.5f))g.DrawEllipse(pen,box);
        using(var cross=new Pen(Color.FromArgb(212,219,228),1)) {
            g.DrawLine(cross,center.X-32,center.Y,center.X+32,center.Y);
            g.DrawLine(cross,center.X,center.Y-32,center.X,center.Y+32);
        }
        var point=new PointF(center.X+VirtualPadState.Axis(x)*27,center.Y-VirtualPadState.Axis(y)*27);
        using(var line=new Pen(Accent,2))g.DrawLine(line,center,point);
        using(var dot=new SolidBrush(Accent))g.FillEllipse(dot,point.X-8,point.Y-8,16,16);
        DrawLabel(g,name,new(center.X-45,center.Y+42,90,20),font,Muted);
    }
    private static void Gyro(Graphics g,RectangleF box,string axis,short raw,Font font)
    {
        float speed=raw/16.384f;
        DrawLabel(g,$"{axis}   {speed:+0.0;-0.0;0.0}",new(box.X,box.Y,box.Width,18),font,Ink);
        float mid=box.X+box.Width/2,amount=Math.Clamp(speed/180f,-1,1)*(box.Width/2-5);
        using(var background=new SolidBrush(Color.FromArgb(215,223,233)))g.FillRectangle(background,box.X+4,box.Y+23,box.Width-8,5);
        using(var fill=new SolidBrush(Accent))g.FillRectangle(fill,Math.Min(mid,mid+amount),box.Y+22,Math.Max(1,Math.Abs(amount)),7);
        using var tick=new Pen(Muted,1);g.DrawLine(tick,mid,box.Y+20,mid,box.Y+31);
    }
    private static void DrawLabel(Graphics g,string text,RectangleF box,Font font,Color color)
    {
        using var brush=new SolidBrush(color);
        using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
        g.DrawString(text,font,brush,box,format);
    }
    private static GraphicsPath Shape(RectangleF box,float radius)
    {
        var path=new GraphicsPath();float d=radius*2;
        path.AddArc(box.X,box.Y,d,d,180,90);path.AddArc(box.Right-d,box.Y,d,d,270,90);
        path.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);path.AddArc(box.X,box.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
}
