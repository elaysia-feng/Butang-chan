(() => {
  const $ = selector => document.querySelector(selector);
  const host = window.chrome?.webview;
  let state = { messages: [], view: 'talk', detail: null, busy: false, config: {} };
  const cards = new Map();
  let settingsFilled = false;
  let toastTimer;
  let cameraStream;
  let featureSignature = '';
  let draftFilled = false;
  const send = (t, fields = {}) => host?.postMessage({ t, ...fields });

  function setView(view) {
    if (view !== 'vision') stopCamera();
    state.view = view;
    document.querySelectorAll('.view').forEach(el => { el.hidden = el.id !== view; });
    document.querySelectorAll('nav button').forEach(el => el.classList.toggle('selected', el.dataset.view === view));
    if (view === 'settings' && !settingsFilled) fillSettings();
  }

  function fillSettings() {
    const config = state.config;
    for (const name of ['baseUrl', 'model', 'visionModel']) $('#' + name).value = config[name] || '';
    $('#persona').value = config.systemPrompt || '';
    for (const name of ['topMost', 'clickThrough', 'hourlyChime', 'autoStart']) $('#' + name).checked = !!config[name];
    $('#key-status').textContent = config.hasKey ? '已保存 · 仅存本机' : '仅存本机';
    settingsFilled = true;
  }

  function renderMessages() {
    const list = $('#messages');
    const follow = list.scrollHeight - list.scrollTop - list.clientHeight < 45;
    const ids = new Set(state.messages.map(message => message.id));
    for (const [id, card] of cards) if (!ids.has(id)) { card.remove(); cards.delete(id); }
    list.querySelector('.empty')?.remove();
    if (!state.messages.length) {
      const empty = document.createElement('div'); empty.className = 'empty';
      const line = document.createElement('p'); line.textContent = '有什么想说的？';
      const hint = document.createElement('p'); hint.textContent = '写一句，我陪你聊聊。';
      empty.append(line, hint); list.append(empty);
    }
    for (const message of state.messages) {
      let card = cards.get(message.id);
      if (!card) {
        card = document.createElement('button'); card.type = 'button';
        const meta = document.createElement('div'); meta.className = 'message-meta';
        const name = document.createElement('span'); name.className = 'author';
        const time = document.createElement('time');
        const preview = document.createElement('p'); preview.className = 'message-preview';
        meta.append(name, time); card.append(meta, preview);
        card.addEventListener('click', () => { state.detail = message.id; renderDetail(); send('detail', { id: message.id }); });
        cards.set(message.id, card); list.append(card);
      }
      card.className = 'message' + (message.author === '你' ? ' user' : message.author === '提示' ? ' error' : '');
      card.querySelector('.author').textContent = message.author + ' · 查看全文';
      card.querySelector('time').textContent = new Date(message.at).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
      card.querySelector('.message-preview').textContent = message.text;
      card.setAttribute('aria-label', message.author + '的消息，点击查看全文');
    }
    if (follow) list.scrollTop = list.scrollHeight;
    renderDetail();
  }

  function renderDetail() {
    const message = state.messages.find(item => item.id === state.detail);
    $('#messages').hidden = !!message; $('#detail').hidden = !message;
    if (!message) { state.detail = null; return; }
    $('#detail-author').textContent = message.author;
    if ($('#detail-text').textContent !== message.text) $('#detail-text').textContent = message.text;
  }

  function sizePreview(value) {
    $('#size-value').value = value + '%';
    $('#size').style.setProperty('--progress', ((value - 20) / 130 * 100) + '%');
    $('#size-pet').style.transform = 'scale(' + Math.min(1.05, .7 + Number(value) / 260) + ')';
    document.querySelectorAll('[data-size]').forEach(el => el.classList.toggle('on', Number(el.dataset.size) === Number(value)));
  }

  function applyState(next) {
    state = { ...state, ...next };
    $('.shell').classList.toggle('compact', !state.expanded);
    $('#expand-panel').textContent = state.expanded ? '收起' : '展开';
    $('#expand-panel').setAttribute('aria-label', state.expanded ? '收起为紧凑聊天' : '展开工具与历史');
    setView(state.view);
    renderMessages();
    $('#busy').hidden = !state.busy; $('#send').disabled = state.busy;
    if (document.activeElement !== $('#size')) $('#size').value = Math.round((state.config.scale || .35) * 100);
    sizePreview($('#size').value); $('#wander').checked = !!state.config.wander;
    $('#inputReactions').checked = !!state.config.inputReactions;
    $('#climb').checked = !!state.config.climb;
    $('#interrupt').hidden = !state.busy;
    $('#attachment').hidden = !state.hasImage;
    $('#vision-send').disabled = !state.hasImage || state.busy;
    if (next.hasImage === false) { $('#vision-preview').hidden = true; $('#vision-preview').replaceChildren(); $('#vision-status').textContent = '还没有选择画面。'; }
    renderFeatures();
    if (!draftFilled && typeof next.draft === 'string') { $('#input').value = next.draft; draftFilled = true; }
    if (next.focus) setTimeout(() => $('#input').focus(), 20);
  }

  function toast(text) {
    $('#toast').textContent = text; $('#toast').hidden = false;
    clearTimeout(toastTimer); toastTimer = setTimeout(() => { $('#toast').hidden = true; }, 2300);
  }

  document.querySelectorAll('[data-view]').forEach(button => button.addEventListener('click', () => {
    setView(button.dataset.view); send('view', { view: button.dataset.view });
  }));
  $('#close').addEventListener('click', () => { stopCamera(); send('draft', { text: $('#input').value }); send('hide'); });
  $('#expand-panel').addEventListener('click', () => send('expand', { value: !state.expanded }));
  $('#back').addEventListener('click', () => { state.detail = null; renderDetail(); send('detail', { id: null }); });
  $('#composer').addEventListener('submit', event => {
    event.preventDefault(); const text = $('#input').value.trim();
    if (!text || state.busy) return;
    state.busy = true; $('#send').disabled = true; send('chat', { text }); $('#input').value = '';
    send('draft', { text: '' });
  });
  $('#input').addEventListener('keydown', event => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); $('#composer').requestSubmit(); }
  });
  $('#input').addEventListener('input', () => send('draft', { text: $('#input').value }));
  $('#size').addEventListener('input', () => { sizePreview($('#size').value); send('size', { value: Number($('#size').value) / 100, save: false }); });
  $('#size').addEventListener('change', () => send('size', { value: Number($('#size').value) / 100, save: true }));
  document.querySelectorAll('[data-size]').forEach(button => button.addEventListener('click', () => {
    $('#size').value = button.dataset.size; sizePreview(button.dataset.size); send('size', { value: Number(button.dataset.size) / 100, save: true });
  }));
  document.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => {
    send('action', { index: Number(button.dataset.action) });
  }));
  $('#wander').addEventListener('change', () => send('config', { map: { wander: $('#wander').checked } }));
  $('#inputReactions').addEventListener('change', () => send('config', { map: { inputReactions: $('#inputReactions').checked } }));
  $('#climb').addEventListener('change', () => send('config', { map: { climb: $('#climb').checked } }));
  $('#climb-now').addEventListener('click', () => send('climb'));
  $('#autoStart').addEventListener('change', () => send('auto-start', { enabled: $('#autoStart').checked }));
  $('#interrupt').addEventListener('click', () => send('interrupt'));
  $('#remove-image').addEventListener('click', () => send('vision-clear'));
  $('#settings-form').addEventListener('submit', event => {
    event.preventDefault();
    send('config', { map: { baseUrl: $('#baseUrl').value.trim(), model: $('#model').value.trim(), systemPrompt: $('#persona').value,
      topMost: $('#topMost').checked, clickThrough: $('#clickThrough').checked, hourlyChime: $('#hourlyChime').checked,
      visionModel: $('#visionModel').value.trim() }, key: $('#key').value.trim() });
    $('#key').value = '';
  });
  let clearArmed = false;
  $('#clear').addEventListener('click', () => {
    if (state.busy) { toast('请等当前回复完成后再清空'); return; }
    if (!clearArmed) { clearArmed = true; $('#clear').textContent = '再点一次确认清空（无法恢复）'; setTimeout(() => { clearArmed = false; $('#clear').textContent = '清空聊天记忆'; }, 5000); return; }
    clearArmed = false; send('clear'); $('#clear').textContent = '清空聊天记忆';
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') { if (state.detail !== null) $('#back').click(); else send('hide'); }
  });
  host?.addEventListener('message', event => {
    const message = event.data;
    if (message.t === 'state') applyState(message);
    if (message.t === 'saved') { settingsFilled = false; fillSettings(); $('#save-status').textContent = '已保存'; toast('设置已记住'); }
    if (message.t === 'error') { state.busy = false; $('#send').disabled = false; toast(message.text); }
    if (message.t === 'saved-feature') {
      toast(message.text);
      if (message.text === '提醒已记住') { $('#reminder-form').hidden = true; $('#reminder-add-toggle').textContent = '添加提醒'; $('#reminder-add-toggle').setAttribute('aria-expanded', 'false'); }
    }
    if (message.t === 'vision-image') {
      const preview = document.createElement('img'); preview.src = message.data; preview.alt = '待发送的画面';
      $('#vision-preview').replaceChildren(preview); $('#vision-preview').hidden = false;
      $('#vision-status').textContent = '画面已选好，发送后交给识图模型。';
    }
  });

  function renderFeatures() {
    const features = state.features || { reminders: [], tasks: [], plugins: [] };
    const signature = JSON.stringify(features);
    if (signature === featureSignature) return;
    featureSignature = signature;
    const item = (list, title, description) => {
      const row = document.createElement('article'); row.className = 'feature-item';
      const heading = document.createElement('h3'); heading.textContent = title;
      const text = document.createElement('p'); text.textContent = description;
      row.append(heading, text); list.append(row); return row;
    };
    const button = (row, label, action) => {
      let buttons = row.querySelector('.feature-buttons');
      if (!buttons) { buttons = document.createElement('div'); buttons.className = 'feature-buttons'; row.append(buttons); }
      const control = document.createElement('button'); control.textContent = label; control.addEventListener('click', action); buttons.append(control);
    };
    const clear = (selector, empty) => {
      const list = $(selector); list.replaceChildren();
      list.dataset.emptyText = empty; return list;
    };
    const reminders = clear('#reminder-list', '还没有提醒，记一件小事吧。');
    for (const reminder of features.reminders) {
      const status = reminder.status === 'due' ? '该做这件事啦' : reminder.status === 'done' ? '已确认' : new Date(reminder.due).toLocaleString('zh-CN');
      const row = item(reminders, reminder.text, status + (reminder.repeat ? ' · 重复提醒' : ''));
      if (reminder.status === 'due') {
        button(row, '知道啦', () => send('reminder-change', { id: reminder.id, action: 'ack' }));
        button(row, '5 分钟后', () => send('reminder-change', { id: reminder.id, action: 'snooze' }));
      }
      button(row, '删除', () => send('reminder-change', { id: reminder.id, action: 'delete' }));
    }
    const tasks = clear('#task-list', '还没有接入任务。');
    const labels = { waiting: '等待开始', running: '正在进行', success: '已完成', error: '遇到问题', paused: '等待重新连接', cancelled: '已取消' };
    for (const task of features.tasks) {
      const row = item(tasks, task.title, (labels[task.status] || task.status) + ' · ' + task.progress + '%' + (task.detail ? '\n' + task.detail : ''));
      const progress = document.createElement('progress'); progress.max = 100; progress.value = task.progress; progress.setAttribute('aria-label', task.title + '进度'); row.append(progress);
      button(row, '移除', () => send('task-delete', { id: task.id }));
    }
    const plugins = clear('#plugin-list', '没有找到插件，可以添加后重新加载。');
    for (const plugin of features.plugins) {
      const row = item(plugins, plugin.name, plugin.description);
      button(row, plugin.enabled ? '停用' : '启用', () => send('plugin-toggle', { id: plugin.id, enabled: !plugin.enabled }));
      if (plugin.enabled) button(row, '运行一次', () => send('plugin-run', { id: plugin.id }));
    }
    for (const list of [reminders, tasks, plugins]) if (!list.children.length) {
      const empty = document.createElement('p'); empty.className = 'feature-empty'; empty.textContent = list.dataset.emptyText; list.append(empty);
    }
  }

  document.querySelectorAll('[data-tool]').forEach(button => button.addEventListener('click', () => {
    document.querySelectorAll('.tool-content').forEach(section => { section.hidden = section.id !== button.dataset.tool; });
    document.querySelectorAll('[data-tool]').forEach(control => control.classList.toggle('selected', control === button));
  }));
  const reminderDefault = new Date(Date.now() + 5 * 60000);
  $('#reminder-add-toggle').addEventListener('click', () => {
    const form = $('#reminder-form'); form.hidden = !form.hidden;
    $('#reminder-add-toggle').textContent = form.hidden ? '添加提醒' : '收起';
    $('#reminder-add-toggle').setAttribute('aria-expanded', String(!form.hidden));
    if (!form.hidden) { $('#reminder-text').focus(); form.scrollIntoView({ block: 'nearest' }); }
  });
  $('#reminder-due').value = new Date(reminderDefault - reminderDefault.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
  $('#reminder-form').addEventListener('submit', event => {
    event.preventDefault();
    const due = new Date($('#reminder-due').value);
    if (!Number.isFinite(due.getTime())) { toast('请选择提醒时间'); return; }
    send('reminder-add', { text: $('#reminder-text').value.trim(), due: due.toISOString(), repeat: Number($('#reminder-repeat').value) });
    $('#reminder-text').value = '';
  });
  $('#reload-plugins').addEventListener('click', () => send('plugin-reload'));
  document.querySelectorAll('[data-pomodoro]').forEach(button => button.addEventListener('click', () => send('pomodoro', { minutes: Number(button.dataset.pomodoro) })));

  function stopCamera() {
    cameraStream?.getTracks().forEach(track => track.stop()); cameraStream = null;
    $('#camera').srcObject = null; $('#camera').hidden = true;
    $('#camera-start').hidden = false; $('#camera-stop').hidden = true; $('#camera-shot').hidden = true;
  }
  $('#capture-screen').addEventListener('click', () => send('screen'));
  $('#camera-start').addEventListener('click', async () => {
    try {
      stopCamera();
      const stream = await navigator.mediaDevices.getUserMedia({ video: { width: { ideal: 1280 }, height: { ideal: 720 } }, audio: false });
      if (state.view !== 'vision') { stream.getTracks().forEach(track => track.stop()); return; }
      cameraStream = stream; $('#camera').srcObject = stream; $('#camera').hidden = false;
      $('#camera-start').hidden = true; $('#camera-stop').hidden = false; $('#camera-shot').hidden = false;
    } catch (error) { toast('摄像头无法打开，请检查设备和 Windows 摄像头权限'); }
  });
  $('#camera-stop').addEventListener('click', stopCamera);
  $('#camera-shot').addEventListener('click', () => {
    const video = $('#camera');
    if (!video.videoWidth) { toast('摄像头还在准备，请稍等'); return; }
    const canvas = document.createElement('canvas'); canvas.width = Math.min(1280, video.videoWidth); canvas.height = Math.round(canvas.width * video.videoHeight / video.videoWidth);
    canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
    send('vision-attach', { data: canvas.toDataURL('image/jpeg', .85) }); stopCamera();
  });
  $('#vision-send').addEventListener('click', () => {
    if (state.busy || !state.hasImage) return;
    send('chat', { text: $('#vision-question').value.trim() || '请描述这张画面，并回答值得注意的内容。' });
  });
  window.addEventListener('pagehide', stopCamera);
  window.panelPreview = applyState;
  applyState(state); send('ready');
})();
