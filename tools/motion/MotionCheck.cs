using System;
using System.Drawing;
using ButangPet;
internal static class MotionCheck
{
    static void Advance(MotionPlayer player,int ms){for(int t=0;t<ms;t+=16)player.Update(16);}
    static void Main(string[] args)
    {
        using(var player=new MotionPlayer(args[0]))
        {
            player.SetRenderHeight(180);
            for(int i=0;i<180;i++){Bitmap frame=player.Frame;player.Update(33);}
            int decoded=player.DecodeCount;
            for(int i=0;i<172;i++){Bitmap frame=player.Frame;player.Update(33);}
            if(player.DecodeCount!=decoded)throw new Exception("待机循环仍反复解码");
            foreach(string state in new[]{"wave","shy","sleepy","cheer"})
            {
                player.React(state);Advance(player,700);player.SetDragging(true);
                Advance(player,400);if(player.CurrentName!="lift")throw new Exception(state+"提起响应超过400ms");
                player.SetDragging(false);Advance(player,400);if(player.CurrentName!="idle")throw new Exception("放下没有结束");
            }
            player.SetClimbing(true,true);Advance(player,1000);
            if(!player.InClimbingPose||!player.FacingLeft)throw new Exception("左侧爬墙朝向错误");
            player.SetDragging(true);Advance(player,400);if(player.CurrentName!="lift")throw new Exception("爬墙中提起过慢");
            player.SetDragging(false);Advance(player,400);
            player.SetDragging(true);Advance(player,64);player.SetDragging(false);Advance(player,32);player.SetDragging(true);
            Advance(player,400);if(player.CurrentName!="lift")throw new Exception("快速提放未采用最后意图");
            player.SetDragging(false);Advance(player,400);if(player.CurrentName!="idle")throw new Exception("快速提放后卡在循环");
            player.SetWalking(true,true);Advance(player,1000);if(!player.InWalkingPose)throw new Exception("没有进入步行");
            player.SetWalking(false);Advance(player,400);if(player.CurrentName!="idle")throw new Exception("步行不能结束");
            player.SetTalking(true);Advance(player,1200);if(player.CurrentName=="talk")throw new Exception("文字等待仍在张嘴");
        }
        Console.WriteLine("PASS: 待机不重复解码、四种手势400ms内提放、爬墙中提起、快速反复提放、步行停止、文本思考状态。");
    }
}