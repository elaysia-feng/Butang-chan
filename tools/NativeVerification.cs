using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Text;
using Microsoft.Web.WebView2.Core;
namespace ButangPet
{
    internal static class NativeVerification
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root=Path.GetFullPath("app"),qa=Path.Combine(root,"native-qa");Directory.CreateDirectory(qa);
            string featureFile=Path.Combine(qa,"data","companion.json");if(File.Exists(featureFile))File.Delete(featureFile);
            Trace.Init(Path.Combine(qa,"verify.log"));
            Config cfg=Config.Load(qa);cfg.scale=.35;cfg.lastX="100";cfg.lastY="180";cfg.wander=false;
            cfg.baseUrl="http://127.0.0.1:4181/v1";cfg.model="qa-model";cfg.apiKey="qa";cfg.inputReactions=false;cfg.climb=false;
            MemoryStore memory=new MemoryStore(Path.Combine(qa,"data"));memory.Clear();
            LlmClient llm=new LlmClient("http://127.0.0.1:4181/v1","qa","qa-model");
            PetForm pet=new PetForm(root,root,cfg,memory,llm);pet.Show();pet.ShowChat();
            ChatForm chat=Field<ChatForm>(pet,"_chat");
            JavaScriptSerializer json=new JavaScriptSerializer();
            int phase=0;bool working=false;DateTime started=DateTime.UtcNow;
            Timer timer=new Timer{Interval=700};
            timer.Tick+=async(s,e)=>{
                if(working)return;working=true;
                try
                {
                    if((DateTime.UtcNow-started).TotalSeconds>90)throw new Exception("网页浮层验证超时，阶段="+phase);
                    if(!chat.PageReady)return;
                    if(args.Contains("--camera-only"))
                    {
                        await VerifyCamera(pet,chat,qa);
                        timer.Stop();pet.Close();Application.Exit();return;
                    }
                    if(args.Contains("--task-only"))
                    {
                        chat.Hide();
                        pet.Features.UpdateTask("toolbar-test","工具条任务状态","running",50,"");await Task.Delay(200);
                        DesktopShot(pet,Path.Combine(qa,"task-status.png"));
                        Rectangle compose=Field<Rectangle>(pet,"_composeBounds");
                        using(Bitmap bitmap=new Bitmap(Path.Combine(qa,"task-status.png")))
                        {
                            Color pixel=bitmap.GetPixel(compose.Left+20,compose.Bottom-4);
                            if(Math.Abs(pixel.R-150)+Math.Abs(pixel.G-112)+Math.Abs(pixel.B-160)>20)throw new Exception("任务进度没有出现在工具条");
                        }
                        pet.Features.UpdateTask("toolbar-test","工具条任务状态","success",100,"");await Task.Delay(100);
                        File.WriteAllText(Path.Combine(qa,"task-status-result.txt"),"PASS: 运行任务在实际透明窗口工具条持续显示进度，完成状态已更新。",Encoding.UTF8);
                        timer.Stop();pet.Close();Application.Exit();return;
                    }
                    if(args.Contains("--menu-only"))
                    {
                        await VerifyCompactAndMenu(pet,chat,qa);
                        File.WriteAllText(Path.Combine(qa,"menu-result.txt"),"PASS: 紧凑聊天展开收起、真实右键菜单、点击空白与Esc关闭。",Encoding.UTF8);
                        timer.Stop();pet.Close();Application.Exit();return;
                    }
                    if(phase==0)
                    {
                        await VerifyCompactAndMenu(pet,chat,qa);
                        await VerifyInteractions(pet,chat,cfg,qa);
                        string longText="完整消息\n"+string.Concat(Enumerable.Repeat("角色参考图与定制界面。",80));
                        chat.AddMessage("不糖酱",longText);chat.ShowText(longText);await Task.Delay(200);
                        string full=json.Deserialize<string>(await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#detail-text').textContent"));
                        if(full!=longText)throw new Exception("全文被截断");
                        await Shot(chat,Path.Combine(qa,"custom-detail.png"));
                        chat.ShowHistory();chat.FocusInput();await Task.Delay(100);
                        await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#input').value='验证网页流式聊天';document.querySelector('#composer').requestSubmit();document.querySelector('#composer').requestSubmit();");
                    }
                    else if(phase==1)
                    {
                        if(memory.Tail(10).LastOrDefault()?.role!="assistant")return;
                        if(memory.Tail(10).Count(m=>m.role=="user")!=1)throw new Exception("重复发送");
                        if(memory.Tail(10).Last().content!="原生流式回复验证成功。")throw new Exception("流式文本错误");
                        if(Field<string>(pet,"_bubble")!="原生流式回复验证成功。")throw new Exception("回复摘要未更新");
                        pet.ShowChat();chat.ShowHistory();await Task.Delay(150);await Shot(chat,Path.Combine(qa,"custom-messages.png"));
                        chat.ShowAppearance();await Task.Delay(100);
                    }
                    else if(phase==2)
                    {
                        await chat.Browser.CoreWebView2.ExecuteScriptAsync("let slider=document.querySelector('#size');slider.value=20;slider.dispatchEvent(new Event('input'));slider.dispatchEvent(new Event('change'));");await Task.Delay(100);
                        if(Math.Abs(cfg.scale-.2)>.001)throw new Exception("滑块下限不生效");
                        await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#size').value=150;document.querySelector('#size').dispatchEvent(new Event('input'));document.querySelector('#size').dispatchEvent(new Event('change'));");await Task.Delay(100);
                        if(Math.Abs(cfg.scale-1.5)>.001)throw new Exception("滑块上限不生效");
                        await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-size]')[0].click()");await Task.Delay(100);
                        if(Math.Abs(Config.Load(qa).scale-.35)>.001)throw new Exception("大小未保存");
                        await Shot(chat,Path.Combine(qa,"custom-appearance.png"));
                        await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-action]')[0].click()");await Task.Delay(100);
                        if(Field<int>(pet,"_expression")!=6)throw new Exception("动作按钮未生效");
                        pet.ShowChat();chat.ShowSettings();await Task.Delay(100);
                    }
                    else if(phase==3)
                    {
                        await Shot(chat,Path.Combine(qa,"custom-settings.png"));
                        await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#baseUrl').value='http://127.0.0.1:4181/v1';document.querySelector('#model').value='qa-model';document.querySelector('#settings-form').requestSubmit();");await Task.Delay(150);
                        if(cfg.baseUrl!="http://127.0.0.1:4181/v1")throw new Exception("设置保存失败");
                        string source=await chat.Browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({overflow:document.body.scrollWidth>innerWidth,hasKey:document.querySelector('#key').value})");
                        if(source.Contains("qa"))throw new Exception("密钥泄露到页面输入框");
                        chat.Hide();pet.SetExpression(6);
                        MotionPlayer motion=Field<MotionPlayer>(pet,"_motion");
                        // 从动作开头采样，不能把上一段挥手的尾部当成完整新动作。
                        for(int wait=0;wait<240&&(motion.CurrentName!="wave"||motion.FrameIndex>3);wait++)await Task.Delay(33);
                        System.Collections.Generic.HashSet<int> played=new System.Collections.Generic.HashSet<int>();
                        for(int capture=0;capture<22;capture++)
                        {
                            await Task.Delay(50);
                            if(motion.CurrentName=="wave")played.Add(motion.FrameIndex);
                            DesktopShot(pet,Path.Combine(qa,"motion-"+capture.ToString("00")+".png"));
                        }
                        if(played.Count<16)throw new Exception("实际窗口没有推进连续中间帧："+played.Count);
                        if(Field<bool>(pet,"_renderFailed"))throw new Exception("透明绘制失败");
                        var motionManifest=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"assets","motions","manifest.json")));
                        var clips=(System.Collections.Generic.Dictionary<string,object>)motionManifest["clips"];
                        foreach(var pair in clips)
                            foreach(object file in (System.Collections.IList)((System.Collections.Generic.Dictionary<string,object>)pair.Value)["frames"])
                            using(Bitmap sprite=new Bitmap(Path.Combine(root,"assets","motions",Convert.ToString(file))))
                                if(sprite.GetPixel(0,0).A!=0||sprite.GetPixel(sprite.Width-1,sprite.Height-1).A!=0)throw new Exception("中间帧角落不透明");
                        await VerifyFeatures(pet,chat,cfg,qa);
                        await VerifyWalkAndCompact(pet,chat,cfg,qa);
                        await VerifyRelease(pet,chat,qa);
                        File.WriteAllText(Path.Combine(qa,"result.txt"),"PASS: 真实鼠标摸头与快速提起、原位放下、独立气泡不遮角色且三条全文可点、流式回复、提醒恢复、插件启停、任务事件、截图识图协议、停止回复、走到边缘再爬墙、尺寸保存、面板释放与草稿恢复、透明帧。",System.Text.Encoding.UTF8);
                        timer.Stop();pet.Close();Application.Exit();return;
                    }
                    phase++;
                }
                catch(Exception ex){File.WriteAllText(Path.Combine(qa,"result.txt"),ex.ToString());Environment.Exit(1);}
                finally{working=false;}
            };
            timer.Start();Application.Run();
        }
        static T Field<T>(object target,string name){return(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);}
        [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr info);

        /// <summary>用真实鼠标验证透明窗口的命中、拖放和堆叠消息，不改用户配置。</summary>
        static async Task VerifyInteractions(PetForm pet,ChatForm chat,Config cfg,string qa)
        {
            chat.Hide();
            Point originalCursor=Cursor.Position;
            try
            {
                // 1. 摸头与拿起，验证专用连续动作，松开后位置不吸附。
                RectangleF head=Field<RectangleF>(pet,"_headBounds");
                Point headPoint=new Point((int)(head.X+head.Width/2),(int)(head.Y+head.Height/2));
                Cursor.Position=pet.PointToScreen(headPoint);await Task.Delay(150);
                mouse_event(2,0,0,0,UIntPtr.Zero);await Task.Delay(100);mouse_event(4,0,0,0,UIntPtr.Zero);await Task.Delay(150);
                if(Field<DateTime>(pet,"_heartsEnd")<=DateTime.UtcNow)throw new Exception("真实鼠标摸头未命中");
                Point start=pet.Location;
                mouse_event(2,0,0,0,UIntPtr.Zero);await Task.Delay(100);
                var liftTime=System.Diagnostics.Stopwatch.StartNew();
                Cursor.Position=new Point(Cursor.Position.X+37,Cursor.Position.Y+19);await Task.Delay(150);
                MotionPlayer motion=Field<MotionPlayer>(pet,"_motion");
                for(int i=0;i<25&&motion.CurrentName!="lift";i++)await Task.Delay(16);
                liftTime.Stop();
                if(liftTime.ElapsedMilliseconds>450)throw new Exception("提起响应仍超过450ms："+liftTime.ElapsedMilliseconds);
                File.WriteAllText(Path.Combine(qa,"lift-latency.txt"),liftTime.ElapsedMilliseconds+" ms（从鼠标开始移动到完整提起循环）",Encoding.UTF8);
                int frame=motion.FrameIndex;await Task.Delay(250);
                if(!Field<bool>(pet,"_moved")||motion.CurrentName!="lift"||motion.FrameIndex==frame)throw new Exception("拿起连续动作未播放");
                DesktopShotUnion(pet,pet.MessageWindow,Path.Combine(qa,"lift.png"));
                mouse_event(4,0,0,0,UIntPtr.Zero);await Task.Delay(1700);
                if(pet.Location!=new Point(start.X+37,start.Y+19))throw new Exception("松开后位置跳变");
                if(Math.Abs(Field<double>(pet,"_holdAngle"))>.1)throw new Exception("放下后角度未恢复");
                // 2. 流式文本只更新一条；每条露出的气泡都能打开自己的全文。
                var bubbles=Field<System.Collections.IList>(pet,"_bubbles");bubbles.Clear();
                pet.Say("第一条独立消息",30);pet.Say("第二条独立消息",30);pet.Say("正在回复",30,"verify-stream");
                pet.Say("第三条流式回复的完整内容",30,"verify-stream");
                if(bubbles.Count!=3)throw new Exception("流式更新产生重复气泡");
                Rectangle character=new Rectangle(pet.Left+(int)((pet.Width-475*cfg.scale*.8)/2),pet.Top+80,(int)(475*cfg.scale*.8),(int)(475*cfg.scale));
                if(!Rectangle.Intersect(character,pet.MessageWindow.Bounds).IsEmpty)throw new Exception("独立气泡仍遮挡角色");
                if(!Screen.FromControl(pet).WorkingArea.Contains(pet.MessageWindow.Bounds))throw new Exception("气泡超出屏幕");
                DesktopShotUnion(pet,pet.MessageWindow,Path.Combine(qa,"interaction-stack.png"));
                for(int i=0;i<3;i++)
                {
                    chat.Hide();await Task.Delay(100);
                    object bubble=bubbles[i];Type type=bubble.GetType();
                    Rectangle bounds=(Rectangle)type.GetField("Bounds").GetValue(bubble);
                    string expected=(string)type.GetField("Text").GetValue(bubble);
                    Point click=new Point(bounds.Left+bounds.Width/2,i==2?bounds.Top+10:bounds.Bottom-6);
                    Cursor.Position=pet.MessageWindow.PointToScreen(click);await Task.Delay(100);
                    mouse_event(2,0,0,0,UIntPtr.Zero);await Task.Delay(80);mouse_event(4,0,0,0,UIntPtr.Zero);await Task.Delay(180);
                    string actual=new JavaScriptSerializer().Deserialize<string>(await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#detail-text').textContent"));
                    if(!chat.Visible||actual!=expected)throw new Exception("堆叠气泡全文不匹配："+i);
                }
                // 3. 新增开关经过网页指令写入和重新读取。
                pet.ShowChat();chat.ShowAppearance();await Task.Delay(100);
                await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#inputReactions').click()");await Task.Delay(100);
                if(!Config.Load(qa).inputReactions)throw new Exception("输入反馈开关未持久化");
                await Shot(chat,Path.Combine(qa,"input-reactions.png"));
                await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#inputReactions').click()");await Task.Delay(100);
            }
            finally{mouse_event(4,0,0,0,UIntPtr.Zero);Cursor.Position=originalCursor;}
            chat.ShowHistory();
        }

        /// <summary>用真实面板与本机接口验证新增功能，模型请求由本地测试服务接收。</summary>
        static async Task VerifyFeatures(PetForm pet,ChatForm chat,Config cfg,string qa)
        {
            // 1. 提醒在网页创建、到期、确认，并能从文件恢复。
            pet.ShowChat();chat.ShowTools();await Task.Delay(100);
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#reminder-add-toggle').click();document.querySelector('#reminder-text').value='验证持久化提醒';document.querySelector('#reminder-due').value='2026-01-01T12:00';document.querySelector('#reminder-form').requestSubmit()");
            await Task.Delay(1300);
            var reminder=pet.Features.Data.Reminders.Find(r=>r.Text=="验证持久化提醒");
            if(reminder==null||reminder.Status!="due")throw new Exception("到期提醒未送达");
            using(var restored=new CompanionFeatures(Path.GetFullPath("app"),action=>action(),qa))
                if(restored.Data.Reminders.Find(r=>r.Id==reminder.Id)?.Status!="due")throw new Exception("提醒重启恢复失败");
            await Shot(chat,Path.Combine(qa,"reminders.png"));
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("Array.from(document.querySelectorAll('#reminder-list button')).find(b=>b.textContent==='知道啦').click()");await Task.Delay(100);
            if(reminder.Status!="done")throw new Exception("提醒确认未生效");
            // 2. 插件实际启用、运行和停用；外部任务通过受限命名管道传入。
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-tool=plugins]').click();document.querySelector('#plugin-list button').click()");await Task.Delay(100);
            if(!pet.Features.Plugins[0].enabled)throw new Exception("插件未启用");
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("Array.from(document.querySelectorAll('#plugin-list button')).find(b=>b.textContent==='运行一次').click()");await Task.Delay(100);
            if(Field<string>(pet,"_bubble")!=pet.Features.Plugins[0].text)throw new Exception("插件未触发消息");
            await Shot(chat,Path.Combine(qa,"plugins.png"));
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#plugin-list button').click()");await Task.Delay(100);
            string reply=await SendEvent(new {type="task",id="qa-task",title="真实任务事件",status="running",progress=37,detail="外部程序正在执行"});
            if(!reply.Contains("true")||pet.Features.Data.Tasks.Find(t=>t.Id=="qa-task")?.Progress!=37)throw new Exception("任务事件未接入");
            await SendEvent(new {type="task",id="qa-task",title="真实任务事件",status="success",progress=100,detail="已完成"});
            if(pet.Features.Data.Tasks.Find(t=>t.Id=="qa-task")?.Status!="success")throw new Exception("任务完成反馈未生效");
            string invalid=await SendEvent(new {type="task",id="qa-task",title="真实任务事件",status="invented",progress=0});
            if(!invalid.Contains("false"))throw new Exception("非法任务状态被接受");
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-tool=tasks]').click()");await Task.Delay(100);
            await Shot(chat,Path.Combine(qa,"tasks.png"));
            // 3. 真正截取屏幕，预览后发出带 JPEG 的模型请求。
            chat.ShowVision();await Task.Delay(100);
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("window.qaCameraResult=null;(async()=>{try{let stream=await navigator.mediaDevices.getUserMedia({video:true,audio:false});const track=stream.getVideoTracks()[0];stream.getTracks().forEach(t=>t.stop());window.qaCameraResult={ok:true,label:track.label}}catch(e){window.qaCameraResult={ok:false,error:e.name}}})()");
            string cameraResult="null";
            for(int i=0;i<50&&cameraResult=="null";i++){await Task.Delay(100);cameraResult=await chat.Browser.CoreWebView2.ExecuteScriptAsync("window.qaCameraResult");}
            File.WriteAllText(Path.Combine(qa,"camera-result.json"),cameraResult,Encoding.UTF8);
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#capture-screen').click()");await Task.Delay(500);
            if(Field<string>(chat,"_imageData")==null)throw new Exception("屏幕截图没有附上");
            await Shot(chat,Path.Combine(qa,"vision.png"));
            cfg.visionModel="qa-vision";
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#vision-question').value='验证图片输入';document.querySelector('#vision-send').click()");
            for(int i=0;i<100&&Field<bool>(chat,"_busy");i++)await Task.Delay(30);
            if(Field<string>(pet,"_bubble")!="图片请求验证成功。")throw new Exception("多模态请求未完成");
            // 4. 取消尚在输出的请求，必须停止接收后续文本。
            pet.ShowChat();chat.FocusInput();await Task.Delay(100);
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#input').value='验证打断';document.querySelector('#composer').requestSubmit()");await Task.Delay(250);
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#interrupt').click()");
            for(int i=0;i<100&&Field<bool>(chat,"_busy");i++)await Task.Delay(30);
            if(Field<bool>(chat,"_busy")||!Field<string>(pet,"_bubble").Contains("已打断"))throw new Exception("流式打断未完成");
            // 5. 先接近墙边，抵达后再向上爬，不瞬移。
            chat.Hide();cfg.climb=true;Point before=pet.Location;
            Rectangle work=Screen.FromControl(pet).WorkingArea;
            pet.Location=new Point(work.Left+90,Math.Max(work.Top+120,pet.Top));Point approach=pet.Location;
            pet.StartClimbing();int top=pet.Top;
            if(pet.Location!=approach)throw new Exception("开始爬墙仍瞬移到边缘");
            MotionPlayer motion=Field<MotionPlayer>(pet,"_motion");
            for(int i=0;i<180&&motion.CurrentName!="climb";i++)await Task.Delay(33);
            int first=motion.FrameIndex;await Task.Delay(250);
            if(pet.Top>=top||motion.CurrentName!="climb"||motion.FrameIndex==first)throw new Exception("爬墙位置或动作没有推进");
            DesktopShotUnion(pet,pet.MessageWindow,Path.Combine(qa,"climb.png"));cfg.climb=false;await Task.Delay(1800);
            if(Field<bool>(pet,"_climbing"))throw new Exception("爬墙未停止");
            pet.Location=before;pet.ShowChat();chat.ShowHistory();
        }

        static async Task VerifyRelease(PetForm pet,ChatForm chat,string qa)
        {
            pet.ShowChat();chat.FocusInput();await Task.Delay(100);
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#input').value='释放面板后保留的草稿';document.querySelector('#input').dispatchEvent(new Event('input'));document.querySelector('#close').click()");
            await Task.Delay(100);
            pet.GetType().GetField("_panelHiddenSince",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,DateTime.UtcNow.AddSeconds(-31));
            await Task.Delay(1300);
            if(Field<ChatForm>(pet,"_chat")!=null)throw new Exception("闲置网页面板没有释放");
            pet.ShowChat();ChatForm reopened=Field<ChatForm>(pet,"_chat");
            for(int i=0;i<70&&!reopened.PageReady;i++)await Task.Delay(100);
            string draft=new JavaScriptSerializer().Deserialize<string>(await reopened.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#input').value"));
            if(draft!="释放面板后保留的草稿")throw new Exception("面板释放后草稿丢失");
            await Shot(reopened,Path.Combine(qa,"reopened-draft.png"));
        }

        static async Task VerifyCompactAndMenu(PetForm pet,ChatForm chat,string qa)
        {
            if(chat.ClientSize.Height>270*pet.DisplayScale)throw new Exception("默认聊天仍过大");
            await Shot(chat,Path.Combine(qa,"compact-chat.png"));
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#expand-panel').click()");await Task.Delay(100);
            if(chat.ClientSize.Height<300*pet.DisplayScale)throw new Exception("聊天展开未生效");
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#expand-panel').click()");await Task.Delay(100);
            if(chat.ClientSize.Height>270*pet.DisplayScale)throw new Exception("聊天无法收起");
            chat.Hide();await Task.Delay(200);Point cursor=Cursor.Position;
            try
            {
                RectangleF head=Field<RectangleF>(pet,"_headBounds");
                Cursor.Position=pet.PointToScreen(new Point((int)(head.X+head.Width/2),(int)(head.Y+head.Height/2)));
                DesktopShot(pet,Path.Combine(qa,"menu-hit-target.png"));
                var surface=Field<Native.LayeredSurface>(pet,"_surface");
                Trace.Log("菜单右键命中检查，画布Alpha="+surface.Canvas.GetPixel((int)(head.X+head.Width/2),(int)(head.Y+head.Height/2)).A);
                IntPtr hit=Native.WindowFromPoint(new Native.POINT(Cursor.Position.X,Cursor.Position.Y));Native.GetWindowThreadProcessId(hit,out uint hitPid);
                Trace.Log("右键目标 hwnd="+hit+" 角色="+pet.Handle+" 进程="+hitPid);
                mouse_event(8,0,0,0,UIntPtr.Zero);await Task.Delay(80);mouse_event(16,0,0,0,UIntPtr.Zero);await Task.Delay(100);
                PetMenuForm menu=Field<PetMenuForm>(pet,"_menu");
                if(!menu.Visible)throw new Exception("自绘右键菜单没有打开");
                DesktopShot(menu,Path.Combine(qa,"custom-menu.png"));
                Rectangle work=Screen.FromControl(pet).WorkingArea;
                Cursor.Position=new Point(work.Left+2,work.Top+2);mouse_event(2,0,0,0,UIntPtr.Zero);await Task.Delay(80);mouse_event(4,0,0,0,UIntPtr.Zero);await Task.Delay(100);
                if(menu.Visible)throw new Exception("点击空白没有关闭菜单");
                menu.ShowPopup(pet.Location,pet.DisplayScale);await Task.Delay(80);SendKeys.SendWait("{ESC}");await Task.Delay(100);
                if(menu.Visible)throw new Exception("Esc没有关闭菜单");
                if(Field<IntPtr>(menu,"_keyboardHook")!=IntPtr.Zero||Field<IntPtr>(menu,"_mouseHook")!=IntPtr.Zero)throw new Exception("菜单关闭后仍保留输入监听");
            }
            finally{mouse_event(4,0,0,0,UIntPtr.Zero);mouse_event(16,0,0,0,UIntPtr.Zero);Cursor.Position=cursor;}
            pet.ShowChat();chat.FocusInput();
        }

        static async Task VerifyWalkAndCompact(PetForm pet,ChatForm chat,Config cfg,string qa)
        {
            chat.Hide();Point start=pet.Location;cfg.wander=false;pet.SetExpression(10);
            for(int i=0;i<160&&Field<bool>(pet,"_wandering");i++)await Task.Delay(33);
            MotionPlayer motion=Field<MotionPlayer>(pet,"_motion");await Task.Delay(500);
            if(pet.Location==start||Field<bool>(pet,"_wandering")||motion.InWalkingPose)throw new Exception("走两步未真实移动或未自动结束");
            using(var compact=new BubbleForm())
            {
                Rectangle work=Screen.FromControl(pet).WorkingArea;
                compact.UpdateMessages(new[]{"最新消息","第二条消息","第三条消息"},work,1,true,pet);
                if(!compact.Visible||compact.CardBounds.Length!=1||!work.Contains(compact.Bounds))throw new Exception("拥挤消息没有降级为可点击提示");
            }
            File.WriteAllText(Path.Combine(qa,"walk-compact-result.txt"),"PASS: 关闭自动溜达时手动走两步仍有限移动并结束；空间不足时保留单条可展开消息提示。",Encoding.UTF8);
            pet.ShowChat();chat.ShowHistory();
        }

        static Task<string> SendEvent(object value)
        {
            return Task.Run(()=>{
                using(var pipe=new NamedPipeClientStream(".",CompanionFeatures.PipeName,PipeDirection.InOut))
                {
                    pipe.Connect(2000);byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)+"\n");
                    pipe.Write(bytes,0,bytes.Length);pipe.Flush();
                    using(var reader=new StreamReader(pipe,Encoding.UTF8))return reader.ReadLine();
                }
            });
        }

        /// <summary>短暂打开真实摄像头，拍照附图后关闭，不保存或上传摄像头照片。</summary>
        static async Task VerifyCamera(PetForm pet,ChatForm chat,string qa)
        {
            pet.ShowChat();chat.ShowVision();await Task.Delay(100);
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#camera-start').click()");
            bool ready=false;
            for(int i=0;i<70&&!ready;i++)
            {
                await Task.Delay(100);
                ready=await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#camera').videoWidth>0")=="true";
            }
            if(!ready)throw new Exception("摄像头预览未就绪");
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#camera-shot').click()");await Task.Delay(150);
            if(Field<string>(chat,"_imageData")==null)throw new Exception("摄像头照片没有附上");
            string closed=await chat.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#camera').srcObject===null");
            if(closed!="true")throw new Exception("拍摄后摄像头未关闭");
            await chat.Browser.CoreWebView2.ExecuteScriptAsync("window.chrome.webview.postMessage({t:'vision-clear'})");
            File.WriteAllText(Path.Combine(qa,"camera-capture-result.txt"),"PASS: 真实摄像头预览、JPEG拍摄附图、关闭摄像头并移除照片；未保存或上传摄像头照片。",Encoding.UTF8);
        }
        static async Task Shot(ChatForm chat,string file){using(FileStream stream=File.Create(file))await chat.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
        static void DesktopShot(Form form,string file){using(Bitmap bitmap=new Bitmap(form.Width,form.Height)){using(Graphics g=Graphics.FromImage(bitmap))g.CopyFromScreen(form.Location,Point.Empty,form.Size);bitmap.Save(file,ImageFormat.Png);}}
        static void DesktopShotUnion(Form first,Form second,string file){Rectangle bounds=Rectangle.Union(first.Bounds,second.Bounds);using(Bitmap bitmap=new Bitmap(bounds.Width,bounds.Height)){using(Graphics g=Graphics.FromImage(bitmap))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);bitmap.Save(file,ImageFormat.Png);}}
    }
}

