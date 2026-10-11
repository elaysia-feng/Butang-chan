using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
internal static class AlignFrame
{
    static Rectangle Bounds(Bitmap image){int l=image.Width,t=image.Height,r=0,b=0;for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(image.GetPixel(x,y).A>=24){l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x);b=Math.Max(b,y);}return Rectangle.FromLTRB(l,t,r+1,b+1);}
    static double Feet(Bitmap image,Rectangle box){double xsum=0,count=0;int height=Math.Max(10,box.Height/10);for(int y=box.Bottom-height;y<box.Bottom;y++)for(int x=box.Left;x<box.Right;x++)if(image.GetPixel(x,y).A>=64){xsum+=x;count++;}return xsum/count;}
    static void Main(string[] args){using(Bitmap reference=new Bitmap(args[1]))using(Bitmap source=new Bitmap(args[0]))using(Bitmap target=new Bitmap(reference.Width,reference.Height,PixelFormat.Format32bppArgb)){Rectangle a=Bounds(reference),b=Bounds(source);float scale=(float)a.Height/b.Height;float x=(float)(Feet(reference,a)-Feet(source,b)*scale),y=a.Bottom-b.Bottom*scale;using(Graphics g=Graphics.FromImage(target)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,x,y,source.Width*scale,source.Height*scale);}target.Save(args[2],ImageFormat.Png);}}
}
