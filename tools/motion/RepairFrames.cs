using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
internal static class RepairFrames
{
    struct Anchor { public double X,Y,FaceY; }
    static byte[] Pixels(Bitmap b){var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);byte[] p=new byte[b.Width*b.Height*4];for(int y=0;y<b.Height;y++)Marshal.Copy(IntPtr.Add(d.Scan0,y*d.Stride),p,y*b.Width*4,b.Width*4);b.UnlockBits(d);return p;}
    static void Write(Bitmap b,byte[] p){var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);for(int y=0;y<b.Height;y++)Marshal.Copy(p,y*b.Width*4,IntPtr.Add(d.Scan0,y*d.Stride),b.Width*4);b.UnlockBits(d);}
    static Anchor Measure(Bitmap b)
    {
        byte[] p=Pixels(b);int bottom=0;double sx=0,n=0,fy=0,fn=0;
        for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(p[(y*b.Width+x)*4+3]>=96)bottom=Math.Max(bottom,y);
        for(int y=Math.Max(0,bottom-25);y<=bottom;y++)for(int x=0;x<b.Width;x++)if(p[(y*b.Width+x)*4+3]>=128){sx+=x;n++;}
        for(int y=30;y<110;y++)for(int x=125;x<210;x++){int i=(y*b.Width+x)*4;if(p[i+3]>=128&&p[i+2]>p[i+1]+8&&p[i+1]>p[i]+1&&p[i+1]>125){fy+=y;fn++;}}
        return new Anchor{X=sx/Math.Max(1,n),Y=bottom,FaceY=fn>50?fy/fn:75};
    }
    static Bitmap Small(string file){using(Bitmap source=new Bitmap(file)){Bitmap b=new Bitmap(320,400,PixelFormat.Format32bppArgb);using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,0,0,320,400);}return b;}}
    static void Main(string[] args)
    {
        string root=args[0],output=args[1],name=args[2];Directory.CreateDirectory(output);
        using(Bitmap neutral=Small(Path.Combine(root,"idle","frame-00.png")))
        using(Bitmap first=Small(Path.Combine(root,name,"frame-00.png")))
        {
            neutral.Save(Path.Combine(output,"neutral-rgba.png"),ImageFormat.Png);
            Anchor reference=Measure(neutral),fixedAnchor=Measure(first);byte[] basePixels=Pixels(neutral);
            double minX=999,maxX=-999,minY=999,maxY=-999,rawMinX=999,rawMaxX=-999,rawMinY=999,rawMaxY=-999;
            bool moving=name=="walk"||name=="cheer"||name=="lift"||name=="climb";
            for(int frame=0;frame<8;frame++)using(Bitmap source=Small(Path.Combine(root,name,"frame-"+frame.ToString("00")+".png")))
            using(Bitmap target=new Bitmap(320,400,PixelFormat.Format32bppArgb))
            {
                Anchor measured=Measure(source),anchor=moving?fixedAnchor:measured;
                float scale=(float)Math.Max(.92,Math.Min(1.08,(reference.Y-reference.FaceY)/(anchor.Y-anchor.FaceY)));
                float dx=(float)(reference.X-anchor.X*scale),dy=(float)(reference.Y-anchor.Y*scale);
                // 1. 锁定落脚点和头身比例，移动动作保留原始轨迹，不逐帧抹掉跳跃。
                using(Graphics g=Graphics.FromImage(target)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,dx,dy,320*scale,400*scale);}
                if(!moving)
                {
                    // 2. 站立动作的裙摆与腿复用同一绘制，消除AI重绘造成的衣服闪烁。
                    byte[] pixels=Pixels(target);
                    for(int y=220;y<400;y++)for(int x=0;x<320;x++)
                    {
                        float blend=Math.Min(1,(y-220)/22f);int i=(y*320+x)*4;
                        for(int channel=0;channel<4;channel++)pixels[i+channel]=(byte)Math.Round(pixels[i+channel]*(1-blend)+basePixels[i+channel]*blend);
                    }
                    Write(target,pixels);
                }
                Anchor repaired=Measure(target);
                rawMinX=Math.Min(rawMinX,measured.X);rawMaxX=Math.Max(rawMaxX,measured.X);rawMinY=Math.Min(rawMinY,measured.Y);rawMaxY=Math.Max(rawMaxY,measured.Y);
                minX=Math.Min(minX,repaired.X);maxX=Math.Max(maxX,repaired.X);minY=Math.Min(minY,repaired.Y);maxY=Math.Max(maxY,repaired.Y);
                target.Save(Path.Combine(output,"rgba-"+frame.ToString("00")+".png"),ImageFormat.Png);
                using(Bitmap green=new Bitmap(320,400)){using(Graphics g=Graphics.FromImage(green)){g.Clear(Color.Lime);g.DrawImage(target,0,0);}green.Save(Path.Combine(output,"green-"+frame.ToString("00")+".png"),ImageFormat.Png);}
            }
            using(Bitmap green=new Bitmap(320,400)){using(Graphics g=Graphics.FromImage(green)){g.Clear(Color.Lime);g.DrawImage(neutral,0,0);}green.Save(Path.Combine(output,"neutral.png"),ImageFormat.Png);}
            string report="{\"clip\":\""+name+"\",\"moving\":"+moving.ToString().ToLower()+",\"beforeFootXRange\":"+(rawMaxX-rawMinX).ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"afterFootXRange\":"+(maxX-minX).ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"beforeFootYRange\":"+(rawMaxY-rawMinY)+",\"afterFootYRange\":"+(maxY-minY)+"}";
            File.WriteAllText(Path.Combine(output,"alignment.json"),report,Encoding.UTF8);Console.WriteLine(report);
        }
    }
}

