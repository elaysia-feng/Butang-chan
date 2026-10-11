/* 内置台词。没配 API Key 时桌宠就靠这些说话，所以得有点性格，
   不能是一堆"你好""再见"。分组是为了让不同场景不撞车。 */
window.Lines = (function () {
  const sets = {
    greet: [
      '唔…你终于看我一眼了',
      '一直在哦，只是你太忙了',
      '今天也辛苦啦，摸摸头可以吗',
      '（悄悄把屏幕擦干净了一点）',
    ],
    click: [
      '诶！戳我做什么',
      '别戳啦，头发会乱的…',
      '在的在的，有事吗？',
      '再戳我就要收费了哦（并没有）',
      '你手好凉，要不要暖暖',
      '唔…痒',
    ],
    poke: [
      '呜哇——！',
      '不要突然吓我啦',
      '我心脏要跳出来了（其实没有）',
    ],
    idleLong: [
      '还在忙吗？我等你哦',
      '你已经很久没理我了…',
      '要不要起来喝口水',
      '肩膀是不是又硬了',
    ],
    idleShort: [
      '嗯？',
      '在偷看什么呀',
      '需要我做什么吗',
      '窗外好像有猫',
    ],
    dragEnd: [
      '诶——你把我放哪了',
      '这里视野不错',
      '再搬我我就罢工了',
    ],
    pomodoroStart: [
      '那我安静陪你，计时开始',
      '专注！我盯着你别摸鱼',
      '加油哦，时间到我会叫你',
    ],
    pomodoroDone: [
      '时间到！休息五分钟吧',
      '做完了吗？做不完也该抬头了',
      '眼睛干不干？眨眨看',
    ],
    hourly: [
      '整点啦',
      '一个小时过去了，辛苦了',
      '现在这个点还不睡呀',
    ],
    memory: [
      '咦，我有点想不起来我们聊过什么了',
      '（记忆整理好啦）',
    ],
  };

  const flat = Object.values(sets).reduce((a, b) => a.concat(b), []);

  function pick(group) {
    const list = sets[group] || flat;
    return list[Math.floor(Math.random() * list.length)];
  }

  /** 久坐提醒不能和普通闲聊重样，单独走一条 */
  function sedentary() {
    return Math.random() < 0.5
      ? '你已经坐很久了，起来走两步嘛'
      : '肩膀是不是很酸？我看着都累';
  }

  return { pick, sedentary, all: flat };
})();
