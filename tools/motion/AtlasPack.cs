using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
internal static class AtlasPack
{
    private sealed class Part
    {
        public int Id,Count,MinX=int.MaxValue,MinY=int.MaxValue,MaxX,MaxY;
        public long SumX,SumY;
        public double X {get{return(double)SumX/Count;}} public double Y {get{return(double)SumY/Count;}}
    }
    static void Main(string[] args) { try { Pack(args); } catch(Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); Environment.Exit(1); } }
    static void Pack(string[] args)
    {
        string source=args[0],output=args[1];Directory.CreateDirectory(output);
        using(Bitmap image=new Bitmap(source))
        {
            int w=image.Width,h=image.Height;
            BitmapData data=image.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            byte[] pixels=new byte[w*h*4];
            for(int y=0;y<h;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),pixels,y*w*4,w*4);
            image.UnlockBits(data);
            // 1. 按真实Alpha连通角色，避免图集边界切掉袖子或混入邻格。
            int[] labels=Enumerable.Repeat(-1,w*h).ToArray(),queue=new int[w*h];
            List<Part> parts=new List<Part>();
            for(int index=0;index<w*h;index++)
            {
                if(labels[index]>=0||pixels[index*4+3]<8)continue;
                Part part=new Part{Id=parts.Count};int head=0,tail=0;queue[tail++]=index;labels[index]=part.Id;
                while(head<tail)
                {
                    int p=queue[head++],x=p%w,y=p/w;part.Count++;part.SumX+=x;part.SumY+=y;
                    part.MinX=Math.Min(part.MinX,x);part.MaxX=Math.Max(part.MaxX,x);part.MinY=Math.Min(part.MinY,y);part.MaxY=Math.Max(part.MaxY,y);
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {int nx=x+dx,ny=y+dy;if(nx<0||nx>=w||ny<0||ny>=h||ny*2/h!=y*2/h)continue;int n=ny*w+nx;if(labels[n]<0&&pixels[n*4+3]>=8){labels[n]=part.Id;queue[tail++]=n;}}
                }
                parts.Add(part);
            }
            Part[] figures=parts.OrderByDescending(p=>p.Count).Take(8).OrderBy(p=>p.Y<h/2?0:1).ThenBy(p=>p.X).ToArray();
            Console.WriteLine("主连通域: "+string.Join(";",figures.Select(p=>p.Count+"@"+(int)p.X+","+(int)p.Y)));
            if(figures.Length!=8||figures.Any(p=>p.Count<4000))throw new Exception("找不到8个完整角色："+source);
            if(figures.Count(p=>p.Y<h/2)!=4)throw new Exception("图集行数与预期不一致");
            int[] owners=Enumerable.Repeat(-1,parts.Count).ToArray();
            for(int i=0;i<8;i++)owners[figures[i].Id]=i;
            foreach(Part part in parts)
            {
                if(owners[part.Id]>=0)continue;
                double distance=double.MaxValue;int owner=-1;
                for(int i=0;i<8;i++)
                {
                    Part f=figures[i];double dx=Math.Max(f.MinX-part.X,Math.Max(0,part.X-f.MaxX));double dy=Math.Max(f.MinY-part.Y,Math.Max(0,part.Y-f.MaxY));
                    double d=dx*dx+dy*dy;if(d<distance){distance=d;owner=i;}
                }
                if(distance<=144)owners[part.Id]=owner;
            }
            // 2. 所有帧共用一个局部裁切范围和缩放，保留跳跃与呼吸的位移。
            int left=int.MaxValue,top=int.MaxValue,right=int.MinValue,bottom=int.MinValue;
            for(int i=0;i<8;i++)
            {
                int ox=(i%4)*w/4,oy=(i/4)*h/2;
                foreach(Part part in parts.Where(p=>owners[p.Id]==i))
                {left=Math.Min(left,part.MinX-ox);top=Math.Min(top,part.MinY-oy);right=Math.Max(right,part.MaxX-ox+1);bottom=Math.Max(bottom,part.MaxY-oy+1);}
            }
            left-=4;top-=4;right+=4;bottom+=4;
            int bw=right-left,bh=bottom-top;float scale=Math.Min(620f/bw,760f/bh);
            List<object> report=new List<object>();
            for(int frame=0;frame<8;frame++)
            {
                int ox=(frame%4)*w/4,oy=(frame/4)*h/2;byte[] isolated=new byte[bw*bh*4];
                for(int y=0;y<bh;y++)for(int x=0;x<bw;x++)
                {
                    int sx=ox+left+x,sy=oy+top+y;if(sx<0||sx>=w||sy<0||sy>=h)continue;
                    int index=sy*w+sx,label=labels[index];bool own=label>=0&&owners[label]==frame;
                    if(!own&&label<0&&pixels[index*4+3]>0)
                        for(int dy=-1;dy<=1&&!own;dy++)for(int dx=-1;dx<=1&&!own;dx++)
                        {int nx=sx+dx,ny=sy+dy;if(nx<0||nx>=w||ny<0||ny>=h)continue;int other=labels[ny*w+nx];own=other>=0&&owners[other]==frame;}
                    if(own)Array.Copy(pixels,index*4,isolated,(y*bw+x)*4,4);
                }
                using(Bitmap original=new Bitmap(bw,bh,PixelFormat.Format32bppArgb))
                using(Bitmap target=new Bitmap(640,800,PixelFormat.Format32bppArgb))
                {
                    data=original.LockBits(new Rectangle(0,0,bw,bh),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                    for(int y=0;y<bh;y++)Marshal.Copy(isolated,y*bw*4,IntPtr.Add(data.Scan0,y*data.Stride),bw*4);original.UnlockBits(data);
                    using(Graphics g=Graphics.FromImage(target)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(original,(640-bw*scale)/2,780-bh*scale,bw*scale,bh*scale);}
                    target.Save(Path.Combine(output,"frame-"+frame.ToString("00")+".png"),ImageFormat.Png);
                }
                report.Add(new{frame,component=figures[frame].Id,pixels=figures[frame].Count});
            }
            File.WriteAllText(Path.Combine(output,"pack-report.json"),new JavaScriptSerializer().Serialize(new{source,width=w,height=h,columns=4,rows=2,crop=new{left,top,right,bottom},frames=report}),Encoding.UTF8);
            Console.WriteLine(Path.GetFileName(output)+": 8帧，统一裁切 "+bw+"x"+bh+" → 640x800");
        }
    }
}



