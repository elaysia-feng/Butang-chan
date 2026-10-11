# 从任意本机业务程序向桌宠发送状态；不需要另起 Web 服务。
param(
    [ValidateSet('task','notify','action','reminder')][string]$Type = 'task',
    [string]$Id = 'example-task',
    [string]$Title = '我的任务',
    [ValidateSet('waiting','running','success','error','paused','cancelled')][string]$Status = 'running',
    [ValidateRange(0,100)][int]$Progress = 0,
    [string]$Detail = '',
    [string]$Text = '',
    [string]$Motion = 'wave',
    [datetime]$Due = (Get-Date).AddMinutes(5),
    [int]$IntervalMinutes = 0
)
$ErrorActionPreference = 'Stop'
$payload = @{ type=$Type; id=$Id; title=$Title; status=$Status; progress=$Progress; detail=$Detail;
    text=$Text; motion=$Motion; due=$Due.ToUniversalTime().ToString('o'); intervalMinutes=$IntervalMinutes }
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'ButangPet.Events', [IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(2000)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress) + "`n")
    $pipe.Write($bytes,0,$bytes.Length); $pipe.Flush()
    $reader = [IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8)
    $reply = $reader.ReadLine() | ConvertFrom-Json
    if (!$reply.ok) { throw $reply.error }
    $reply
}
finally { $pipe.Dispose() }
