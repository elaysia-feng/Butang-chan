$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class PetCaptureRect {
public delegate bool Callback(IntPtr h,IntPtr p);
[DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
[DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out R r);
[DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out P p);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr info);
public struct P {public int X,Y;}
public struct R {public int L,T,Rt,B;}
public static void Open(uint target){R r=Find(target,"不糖酱");P cursor;GetCursorPos(out cursor);try{SetCursorPos((r.L+r.Rt)/2+26,r.B-24);mouse_event(2,0,0,0,UIntPtr.Zero);System.Threading.Thread.Sleep(100);mouse_event(4,0,0,0,UIntPtr.Zero);System.Threading.Thread.Sleep(250);}finally{SetCursorPos(cursor.X,cursor.Y);}}
public static R Find(uint target,string title){SetThreadDpiAwarenessContext(new IntPtr(-4));IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{uint id;GetWindowThreadProcessId(h,out id);if(id==target&&IsWindowVisible(h)){var text=new StringBuilder(256);GetWindowText(h,text,256);if(text.ToString()==title)found=h;}return true;},IntPtr.Zero);R r;if(found==IntPtr.Zero||!GetWindowRect(found,out r))throw new Exception("找不到可见窗口："+title);return r;}
}
"@
Add-Type -AssemblyName System.Drawing
$runningPet=Get-Process ButangPet | Where-Object Path -eq (Join-Path $PWD 'app\ButangPet.exe')
[PetCaptureRect]::Open($runningPet.Id)
Start-Sleep -Seconds 1
$panelBounds=[PetCaptureRect]::Find($runningPet.Id,'不糖酱 · 陪伴')
$panelShot=[System.Drawing.Bitmap]::new(($panelBounds.Rt-$panelBounds.L),($panelBounds.B-$panelBounds.T))
$panelGraphics=[System.Drawing.Graphics]::FromImage($panelShot)
$panelGraphics.CopyFromScreen($panelBounds.L,$panelBounds.T,0,0,$panelShot.Size)
$panelShot.Save((Join-Path $PWD 'app\custom-panel-final.png'))
$panelGraphics.Dispose(); $panelShot.Dispose()

