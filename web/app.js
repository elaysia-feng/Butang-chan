/* 桥接 + 行为 + 界面。
   这里的分工：宿主负责"系统能做的事"（窗口、网络、文件），
   页面负责"她怎么表现"（表情、眼神、台词、面板）。 */

(function () {
  const $ = s => document.querySelector(s);

  const stageEl = $('#stage');
  const bubbleEl = $('#bubble');
  const bubbleText = $('#bubble-text');
  const chatEl = $('#chat');
  const badgeEl = $('#badge');
  const msgsEl = $('#msgs');
  const inputEl = $('#input');

  let cfg = {
    body: 'full', scale: 1, opacity: 1, topMost: true, clickThrough: false, tightClip: true,
    wander: true, speakOnClick: true, hourlyChime: false,
    sedentaryMinutes: 45, pomodoroMinutes: 25,
    persona: '', model: '', baseUrl: '', hasKey: false,
  };
  let expressions = [];      // 当前模型自带的��情名

  let chatOpen = false;
  let lastActivity = Date.now();
  let bubbleTimer = null;
  let idleTimer = null;
  let streaming = null;
  let petInfo = null;
  let detailMessage = null;

  // ══════════════════════════════════════════════════════
  // 与宿主通信
  // ══════════════════════════════════════════════════════
  function send(t, extra) {
    if (!window.chrome || !window.chrome.webview) return;
    const m = Object.assign({ t }, extra || {});
    try { window.chrome.webview.postMessage(m); } catch (e) { log('postMessage 失败 ' + e.message); }
  }
  const log = s => send('log', { s: String(s) });

  // 页面里的异常如果只留在 DevTools，桌宠用户永远看不到；这里直接送进宿主日志
  window.addEventListener('error', e => {
    log('页面异常: ' + (e.message || '') + ' @ ' + (e.filename || '') + ':' + (e.lineno || 0));
  });
  window.addEventListener('unhandledrejection', e => {
    log('页面 Promise 异常: ' + (e.reason && e.reason.message ? e.reason.message : e.reason));
  });

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', onHostMessage);
  } else {
    bubble('宿��通道没建起来，这只是浏览器预览');
  }

  function onHostMessage(e) {
    const m = e.data;
    if (!m || !m.t) return;
    switch (m.t) {
      case 'cursor': onCursor(m); break;
      case 'config': onConfig(m); break;
      case 'say': say(m.text); break;
      case 'mode':
        chatOpen = !!m.chat;
        chatEl.hidden = !chatOpen;
        if (chatOpen) { bubbleEl.hidden = true; clearTimeout(bubbleTimer); }
        document.body.classList.toggle('chat', chatOpen);
        paintChat();
        if (chatOpen && !detailMessage) setTimeout(() => inputEl.focus(), 120);
        break;
      case 'chat': onChat(m); break;
      case 'emotion': doExpression(m.name); break;
      case 'pomodoro': onPomodoro(m); break;
      case 'scale': Pet.relayout(); Pet.rebuildMask(); break;
      case 'needMask': Pet.rebuildMask(); break;
      default: break;
    }
  }

  // ══════════════════════════════════════════════════════
  // 眼神跟随
  // ══════════════════════════════════════════════════════
  function onCursor(m) {
    lastActivity = Date.now();
    const dpr = window.devicePixelRatio || 1;
    // 宿主给的是屏幕物理坐标，窗口原点也是物理坐标；除以 dpr 换回页面 CSS 像素
    const px = (m.x - m.wx) / dpr;
    const py = (m.y - m.wy) / dpr;

    // 脸大致在舞台偏上的位置。用它做原点，光标在窗口外时方向依然是对的
    const rect = stageEl.getBoundingClientRect();
    const faceX = rect.left + rect.width * 0.5;
    const faceY = rect.top + rect.height * 0.34;
    const reach = rect.width * 0.85;

    Pet.setLook((px - faceX) / reach, (py - faceY) / (reach * 0.8));
  }

  // ══════════════════════════════════════════════════════
  // 气泡与台词
  // ══════════════════════════════════════════════════════
  function say(text, ms) {
    if (!text) return;
    if (chatOpen) { pushMsg(text, 'bot'); return; }
    bubbleText.textContent = text;
    bubbleEl.hidden = false;
    clearTimeout(bubbleTimer);
    bubbleTimer = setTimeout(() => {
      bubbleEl.hidden = true;
      Pet.rebuildMask();          // 气泡是遮罩的一部分，收起后要重新裁
    }, ms || Math.min(9000, 2600 + text.length * 110));
    // 气泡出现/收起都会改变窗口该保留的区域，不重裁就会被裁掉
    setTimeout(() => Pet.rebuildMask(), 60);
  }
  const bubble = t => say(t, 4000);

  function doExpression(name, withLine) {
    if (!expressions.includes(name)) {
      // 模型没有这个表情时退回自然，别把参数硬打上去
      Pet.setExpression('neutral');
    } else {
      Pet.setExpression(name);
    }
    if (withLine !== false && cfg.speakOnClick) {
      const group = name === 'surprise' ? 'poke' : 'click';
      say(window.Lines.pick(group));
    }
  }

  // ══════════════════════════════════════════════════════
  // 交互：拖拽 / 点击 / 双击 / 右键 / 滚轮
  // ══════════════════════════════════════════════════════
  let down = null;
  let dragPending = false;

  stageEl.addEventListener('mousedown', e => {
    if (e.button !== 0) return;
    log('mousedown target=' + e.target.tagName);
    down = { x: e.screenX, y: e.screenY, t: Date.now(), moved: false };
    dragPending = true;
    send('drag', { phase: 'start' });
  });

  window.addEventListener('mousemove', e => {
    if (!down) return;
    if (Math.abs(e.screenX - down.x) + Math.abs(e.screenY - down.y) > 5) down.moved = true;
    if (!dragPending) return;
    dragPending = false;                       // 一帧最多发一次，别把 IPC 打满
    requestAnimationFrame(() => {
      if (down && down.moved) send('drag', { phase: 'move' });
      dragPending = true;
    });
  });

  window.addEventListener('mouseup', () => {
    if (!down) return;
    const wasTap = !down.moved && Date.now() - down.t < 420;
    send('drag', { phase: 'end' });
    if (wasTap) {
      Pet.poke();
      lastActivity = Date.now();
      // 点一下就开对话卡片；再点一下收起来
      toggleChat();
    } else if (down.moved) {
      say(window.Lines.pick('dragEnd'), 3200);
    }
    down = null;
  });

  /** 对话卡片的开关。窗口尺寸是固定的，卡片收起时会被裁剪区域裁掉。 */
  function toggleChat() {
    setChat(!chatOpen);
  }

  function setChat(open) {
    setChatMode(open);
  }

  stageEl.addEventListener('dblclick', e => {
    e.preventDefault();
    say(window.Lines.pick('click'), 3000);
  });

  window.addEventListener('contextmenu', e => {
    e.preventDefault();
    send('menu');
  });

  stageEl.addEventListener('wheel', e => {
    e.preventDefault();
    send('scaleBy', { k: e.deltaY < 0 ? 1.08 : 0.93 });
  }, { passive: false });

  // ══════════════════════════════════════════════════════
  // 对话面板
  // ══════════════════════════════════════════════════════
  function setChatMode(open) {
    chatOpen = open;
    // 先告诉宿主把窗口撑开，等宿主回 mode 再量尺寸重排
    send('resize', { open });
    if (!open) { chatEl.hidden = true; document.body.classList.remove('chat'); }
  }

  function paintChat() {
    requestAnimationFrame(() => {
      Pet.relayout();
      Pet.rebuildMask();
    });
  }

  $('#send').addEventListener('click', sendInput);
  inputEl.addEventListener('keydown', e => {
    if (e.key === 'Enter' && !e.isComposing) sendInput();
  });

  function sendInput() {
    const text = inputEl.value.trim();
    if (!text || $('#send').disabled) return;
    inputEl.value = '';
    pushMsg(text, 'user');
    $('#send').disabled = true;
    Pet.setTalking(true);
    send('chat', { text });
  }

  // 摘要留在卡片里，全文保留在数据中，避免长回复挤满桌面。
  function pushMsg(text, who) {
    const el = document.createElement('button');
    el.type = 'button';
    el.className = 'msg ' + who;
    el.dataset.who = who;
    const label = document.createElement('span');
    label.className = 'msg-label';
    label.textContent = who === 'user' ? '你' : who === 'err' ? '提示' : '不糖酱';
    const preview = document.createElement('span');
    preview.className = 'msg-preview';
    el.append(label, preview);
    el.addEventListener('click', () => showDetail(el));
    msgsEl.appendChild(el);
    updateMsg(el, text);
    return el;
  }

  function updateMsg(el, text, pending) {
    const nearBottom = msgsEl.scrollHeight - msgsEl.scrollTop - msgsEl.clientHeight < 48;
    el.dataset.raw = text || '';
    el.querySelector('.msg-preview').textContent = text || (pending ? '正在回复…' : '（空消息）');
    el.querySelector('.msg-label').textContent =
      (el.dataset.who === 'user' ? '你' : el.dataset.who === 'err' ? '提示' : '不糖酱') +
      (pending ? ' · 正在回复' : ' · 查看全文');
    el.setAttribute('aria-label', el.querySelector('.msg-label').textContent + '：' + (text || ''));
    if (detailMessage === el) $('#detail-text').textContent = text || '正在回复…';
    if (nearBottom) msgsEl.scrollTop = msgsEl.scrollHeight;
  }

  function showDetail(el) {
    detailMessage = el;
    $('#detail-author').textContent = el.dataset.who === 'user' ? '你' : el.dataset.who === 'err' ? '提示' : '不糖酱';
    $('#detail-text').textContent = el.dataset.raw || '正在回复…';
    msgsEl.hidden = true;
    $('#message-detail').hidden = false;
    $('#detail-back').focus();
  }

  function closeDetail() {
    $('#message-detail').hidden = true;
    msgsEl.hidden = false;
    if (detailMessage && detailMessage.isConnected) detailMessage.focus();
    detailMessage = null;
  }
  $('#detail-back').addEventListener('click', closeDetail);
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && detailMessage) closeDetail();
  });
  bubbleEl.addEventListener('click', () => {
    const el = pushMsg(bubbleText.textContent, 'bot');
    setChatMode(true);
    showDetail(el);
  });

  function onChat(m) {
    if (m.phase === 'start') {
      streaming = pushMsg('', 'bot');
      updateMsg(streaming, '', true);
      Pet.setTalking(true);
      $('#send').disabled = true;
    } else if (m.phase === 'delta') {
      if (streaming) updateMsg(streaming, m.text, true);
    } else if (m.phase === 'done') {
      if (streaming) { updateMsg(streaming, m.text); streaming = null; }
      Pet.setTalking(false);
      $('#send').disabled = false;
      if (!detailMessage) inputEl.focus();
    } else if (m.phase === 'error') {
      if (streaming) {
        if (detailMessage === streaming) closeDetail();
        streaming.remove(); streaming = null;
      }
      Pet.setTalking(false);
      $('#send').disabled = false;
      pushMsg(m.error, 'err');
    } else if (m.phase === 'notice') {
      pushMsg(m.text, 'bot');
    } else if (m.phase === 'cleared') {
      closeDetail();
      msgsEl.innerHTML = '';
      streaming = null;
      Pet.setTalking(false);
      $('#send').disabled = false;
      pushMsg('（记忆已清空）', 'err');
    }
  }

  function onPomodoro(m) {
    if (m.state === 'idle') { badgeEl.hidden = true; return; }
    if (m.state === 'done') { badgeEl.hidden = true; return; }
    const min = Math.floor(m.left / 60), sec = m.left % 60;
    badgeEl.hidden = false;
    badgeEl.textContent = '🍅 ' + String(min).padStart(2, '0') + ':' + String(sec).padStart(2, '0');
  }

  // ── 面板页签 ──
  document.querySelectorAll('.tab[data-tab]').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab[data-tab]').forEach(b => b.classList.toggle('on', b === btn));
      $('#pane-talk').hidden = btn.dataset.tab !== 'talk';
      $('#pane-set').hidden = btn.dataset.tab !== 'set';
    });
  });
  $('#chat-close').addEventListener('click', () => setChatMode(false));

  // ── 设置 ──
  function fillSettings() {
    $('#cfg-baseUrl').value = cfg.baseUrl || '';
    $('#cfg-model').value = cfg.model || '';
    $('#cfg-persona').value = cfg.persona || '';
    $('#cfg-wander').checked = !!cfg.wander;
    $('#cfg-hourly').checked = !!cfg.hourlyChime;
    $('#cfg-topmost').checked = !!cfg.topMost;
    $('#cfg-through').checked = !!cfg.clickThrough;
    $('#cfg-status').textContent = cfg.hasKey
      ? '已保存 API Key。改完点「保存」。'
      : '还没设置 API Key。可以先用内置台词，功能都在，只是不能自由聊天。';
    $('#cfg-key').placeholder = cfg.hasKey ? '已设置（留空表示不改）' : '粘贴你的 Key';
  }

  $('#cfg-save').addEventListener('click', () => {
    const map = {
      baseUrl: $('#cfg-baseUrl').value.trim(),
      model: $('#cfg-model').value.trim(),
      persona: $('#cfg-persona').value,
      wander: $('#cfg-wander').checked,
      hourlyChime: $('#cfg-hourly').checked,
      topMost: $('#cfg-topmost').checked,
      clickThrough: $('#cfg-through').checked,
    };
    send('config', { map });
    const key = $('#cfg-key').value.trim();
    if (key) send('setKey', { key });
    $('#cfg-key').value = '';
    $('#cfg-status').textContent = '已保存';
  });

  $('#cfg-clear').addEventListener('click', () => {
    if (confirm('清空全部聊天记忆？不可恢复。')) send('clearMemory');
  });

  function onConfig(m) {
    Object.assign(cfg, m);
    fillSettings();
  }

  // ══════════════════════════════════════════════════════
  // 待机：太久没人理就自己嘟囔一句
  // ══════════════════════════════════════════════════════
  function scheduleIdle() {
    clearTimeout(idleTimer);
    idleTimer = setTimeout(function again() {
      const quietFor = (Date.now() - lastActivity) / 1000;
      if (!chatOpen && quietFor > 75) {
        say(window.Lines.pick(quietFor > 240 ? 'idleLong' : 'idleShort'));
      }
      scheduleIdle();
    }, 45000 + Math.random() * 45000);
  }

  // ══════════════════════════════════════════════════════
  // 启动
  // ══════════════════════════════════════════════════════
  let booted = false;
  async function boot() {
    if (booted) return;      // DOMContentLoaded 和 readyState 可能都触发，只跑一次
    booted = true;
    try {
      petInfo = await Pet.boot(stageEl, (spans, info, ui) => {
        if (spans && spans.length >= 3) send('mask', { spans, ui: ui || [] });
        else send('log', { s: '遮罩读回为空 ' + JSON.stringify(info) });
      }, cfg.body);
      expressions = petInfo.expressions || [];
      fitWindow();
      window.addEventListener('resize', () => { Pet.relayout(); Pet.rebuildMask(); });
      send('ready');
      scheduleIdle();
      setTimeout(() => say('你好呀，我又住进你电脑里啦～'), 1200);
    } catch (e) {
      bubble('加载失败：' + e.message);
      send('log', { s: 'boot 失败 ' + e.message });
    }
  }

  /** 让窗口正好装下角色 + 头顶的气泡区，不用两套写死的尺寸。 */
  function fitWindow() {
    const art = Pet.measureArt();
    if (!art) return;
    const pad = 10;
    const w = Math.max(200, Math.min(520, Math.round(art.want * cfg.scale) + pad * 2));
    const h = Math.round(art.h * cfg.scale) + art.top + pad;
    document.documentElement.style.setProperty('--stage-w', w + 'px');
    document.documentElement.style.setProperty('--stage-h', Math.round(art.h * cfg.scale) + 'px');
    send('resize', { open: chatOpen, w, h });
  }

  // 暴露给宿主和 DevTools 用的探针
  window.__pet = {
    info: () => ({ cfg, pet: petInfo, info: Pet.info(), expressions }),
    mask: spans => send('mask', { spans }),
    say, setChatMode, fitWindow,
  };

  document.addEventListener('DOMContentLoaded', boot);
  if (document.readyState !== 'loading') boot();
})();
