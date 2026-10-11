using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ButangPet
{
    /// <summary>隔离配置下测量三档尺寸的稳态进程开销，记录面板释放。</summary>
    internal static class ResourceVerification
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string root=Path.GetFullPath("app"),qa=Path.Combine(root,"resource-qa");Directory.CreateDirectory(qa);
            Trace.Init(Path.Combine(qa,"log.txt"));
            Config cfg=Config.Load(qa);cfg.scale=.35;cfg.lastX="400";cfg.lastY="150";cfg.wander=cfg.climb=cfg.inputReactions=false;
            PetForm pet=new PetForm(root,root,cfg,new MemoryStore(Path.Combine(qa,"data")),new LlmClient("","",""));
            pet.Shown+=async(s,e)=>{
                try
                {
                    pet.GetType().GetField("_nextAction",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,DateTime.MaxValue);
                    using(var output=new StreamWriter(Path.Combine(qa,"metrics.csv"),false,System.Text.Encoding.UTF8))
                    {
                        output.WriteLine("scale,privateMiB,workingSetMiB,cpuPercentOneCore,paintsPerSecond");
                        foreach(double scale in new[]{.2,.35,.41,1.5})
                        {
                            pet.SetScale(scale,false);await Task.Delay(5000);
                            Process process=Process.GetCurrentProcess();double cpu=process.TotalProcessorTime.TotalSeconds;int paints=pet.PaintCount;
                            var watch=Stopwatch.StartNew();await Task.Delay(5000);watch.Stop();process.Refresh();
                            output.WriteLine(string.Join(",",scale,Math.Round(process.PrivateMemorySize64/1048576d,1),
                                Math.Round(process.WorkingSet64/1048576d,1),Math.Round(100*(process.TotalProcessorTime.TotalSeconds-cpu)/watch.Elapsed.TotalSeconds,1),
                                Math.Round((pet.PaintCount-paints)/watch.Elapsed.TotalSeconds,1)));output.Flush();
                        }
                    }
                    pet.SetScale(.35,false);pet.ShowChat();await Task.Delay(5000);
                    File.WriteAllText(Path.Combine(qa,"panel-open.pid"),Process.GetCurrentProcess().Id.ToString());
                    await Task.Delay(8000);
                    ChatForm chat=(ChatForm)pet.GetType().GetField("_chat",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(pet);chat.Hide();
                    await Task.Delay(33000);
                    if(pet.GetType().GetField("_chat",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(pet)!=null)throw new Exception("面板没有释放");
                    File.WriteAllText(Path.Combine(qa,"panel-released.pid"),Process.GetCurrentProcess().Id.ToString());
                    await Task.Delay(8000);
                    File.WriteAllText(Path.Combine(qa,"result.txt"),"PASS: 三档尺寸稳态采样完成，真实等待30秒后释放网页面板。",System.Text.Encoding.UTF8);
                    pet.Close();Application.Exit();
                }
                catch(Exception ex){File.WriteAllText(Path.Combine(qa,"result.txt"),ex.ToString());Environment.Exit(1);}
            };
            pet.Show();Application.Run();
        }
    }
}
