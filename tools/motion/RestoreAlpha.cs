using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
internal static class RestoreAlpha
{
    static void Main(string[] args)
    {
        Directory.CreateDirectory(args[1]);int frame=0;
        foreach(string file in Directory.GetFiles(args[0],"frame-*.png").OrderBy(p=>p,StringComparer.Ordinal))
        using(Bitmap source=new Bitmap(file))using(Bitmap output=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb))
        {
            int w=source.Width,h=source.Height;BitmapData data=source.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            byte[] p=new byte[w*h*4];for(int y=0;y<h;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),p,y*w*4,w*4);source.UnlockBits(data);
            // 恢复视频处理前的Alpha，扣除背景贡献；运行时仍使用真实透明通道。
            for(int i=0;i<p.Length;i+=4)
            {
                double b=p[i],g=p[i+1],r=p[i+2];double a=Math.Max(0,Math.Min(1,1-(g-Math.Max(r,b))/255));
                if(a<.035){p[i]=p[i+1]=p[i+2]=p[i+3]=0;continue;}
                p[i]=(byte)Math.Max(0,Math.Min(255,Math.Round(b/a)));
                p[i+1]=(byte)Math.Max(0,Math.Min(255,Math.Round((g-(1-a)*255)/a)));
                p[i+2]=(byte)Math.Max(0,Math.Min(255,Math.Round(r/a)));p[i+3]=(byte)Math.Round(a*255);
            }
            data=output.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            for(int y=0;y<h;y++)Marshal.Copy(p,y*w*4,IntPtr.Add(data.Scan0,y*data.Stride),w*4);output.UnlockBits(data);
            output.Save(Path.Combine(args[1],"frame-"+(frame++).ToString("000")+".png"),ImageFormat.Png);
        }
        Console.WriteLine(Path.GetFileName(args[1])+": "+frame+"个透明中间帧");
    }
}
