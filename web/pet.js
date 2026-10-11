/* Live2D 渲染层。只管把模型画出来、把参数驱动好；
   行为决策（什么时候眨眼、什么时候换表情）在 app.js。 */
window.Pet = (function () {
  // 半身是打磨过的正式交付；全身是项目里已有的阶段稿。
  // 两者模型路径、表情名、比例都不同，所以这里不写死任何一个。
  const MODELS = {
    half: {
      url: '../../07_export/butang.model3.json',
      // 半身：宽，窗口矮一些，头顶留气泡
      box: { w: 300, top: 76 }
    },
    full: {
      url: '../../08_validation/full-stage-export/butang.model3.json',
      box: { w: 260, top: 84 }
    },
  };
  const VER = 'multi-model';

  let app = null, model = null, stageEl = null;
  let W = 0, H = 0, natural = null;
  let ready = false, failed = null;

  // ── 表情参数（与 07_export 的 exp3 和 09_web 保持一致） ──
  const EMOTIONS = {
    neutral: {},
    smile:   { ParamMouthForm: .35, ParamMouthOpenY: .7, ParamBrowLY: .2, ParamBrowRY: .2 },
    wink:    { ParamEyeROpen: 0,   ParamMouthForm: .9, ParamMouthOpenY: .35 },
    surprise:{ ParamMouthRound: 1, ParamMouthOpenY: .85, ParamMouthForm: -.7, ParamBrowLY: .8, ParamBrowRY: .8 },
    shy:     { ParamFrown: 1, ParamMouthForm: -.8, ParamEyeLOpen: .82, ParamEyeROpen: .82, ParamBrowLY: -.2, ParamBrowRY: -.2 },
    smug:    { ParamMouthForm: .8, ParamEyeLOpen: .78, ParamEyeROpen: .7, ParamBrowLY: .4, ParamBrowRY: -.1 },
    sad:     { ParamFrown: 1, ParamTear: 1, ParamMouthForm: -.9, ParamEyeLOpen: .7, ParamEyeROpen: .7, ParamBrowLY: -.6, ParamBrowRY: -.6 },
    sparkle: { ParamStar: 1, ParamMouthForm: .5, ParamMouthOpenY: 1, ParamBrowLY: .3, ParamBrowRY: .3 },
    sleepy:  { ParamMouthRound: 1, ParamMouthOpenY: .25, ParamMouthForm: -.8, ParamEyeLOpen: .5, ParamEyeROpen: 0, ParamBrowLY: -.3, ParamBrowRY: -.3 },
  };
  // 每次切表情都要先把互斥部件清零，否则星星会跟着泪眼一起挂脸上
  const CLEAR = { ParamStar: 0, ParamTear: 0, ParamMouthRound: 0, ParamFrown: 0 };

  let emotion = {};
  let look = { x: 0, y: 0 }, lookTarget = { x: 0, y: 0 };
  let talking = false, talkPhase = 0;
  let blink = 1, nextBlink = 2.5;
  let pokeT = 0;
  let last = performance.now();

  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const smooth = (c, t, r, dt) => c + (t - c) * (1 - Math.exp(-r * dt));

  // ── 参数读写 ──
  function applyParams(values) {
    if (!model) return;
    const core = model.internalModel.coreModel;
    const ids = core._model.parameters.ids;
    for (const id in values) {
      if (ids.includes(id)) core.setParameterValueById(id, values[id]);
    }
  }
  function peek(id) {
    if (!model) return 0;
    const core = model.internalModel.coreModel._model;
    const i = core.parameters.ids.indexOf(id);
    return i < 0 ? 0 : core.parameters.values[i];
  }

  /** 量模型自身的自然尺寸。之前直接拿 model.width 算缩放算错过，
   *  实测这个模型的 DisplayObject 尺寸和贴图尺寸对不上，
   *  所以必须量 localBounds，量不到再退回 internalModel。 */
  function measure() {
    const sx = model.scale.x, sy = model.scale.y;
    const px = model.position.x, py = model.position.y;
    model.scale.set(1);
    model.position.set(0, 0);
    let w = 0, h = 0;
    try {
      const b = model.getLocalBounds();
      w = b.width; h = b.height;
    } catch (e) { /* 退回下面 */ }
    if (!isFinite(w) || !isFinite(h) || w < 8 || h < 8) {
      const im = model.internalModel;
      w = im.width || model.width;
      h = im.height || model.height;
    }
    model.scale.set(sx, sy);
    model.position.set(px, py);
    if (!isFinite(w) || !isFinite(h) || w < 8 || h < 8) throw new Error('模型尺寸异常');
    return { w, h };
  }

  /**
   * 读回舞台像素。
   *
   * 首选直接读 WebGL 画布：boot 时开了 preserveDrawingBuffer，帧外的画布内容还在。
   * renderer.extract 会自己再渲染一遍去抢渲染器的目标状态，实测会让画面出现
   * 局部丢失（角色头发位置变成黑洞），所以只当备用。
   */
  function extractStage() {
    if (app && app.view) return app.view;
    if (app && app.renderer && app.renderer.extract) {
      try { return app.renderer.extract.canvas(app.stage, new PIXI.Rectangle(0, 0, W, H)); }
      catch (e) { return null; }
    }
    return null;
  }

  /** 扫描一张画布，返回不透明像素的包围盒；顺带返回是否找得到。 */
  function alphaBox(canvas, w, h) {
    let d;
    try {
      const g2 = canvas.getContext ? canvas.getContext('2d') : null;
      if (!g2) return null;
      d = g2.getImageData(0, 0, w, h).data;
    } catch (e) { return null; }

    let x0 = w, y0 = h, x1 = -1, y1 = -1, n = 0;
    // 抽样步长 1 太慢也没必要，角色是连成片的，步长 2 足够定位
    for (let y = 0; y < h; y += 2) {
      for (let x = 0; x < w; x += 2) {
        if (d[(y * w + x) * 4 + 3] >= MASK_ALPHA) {
          if (x < x0) x0 = x;
          if (x > x1) x1 = x;
          if (y < y0) y0 = y;
          if (y > y1) y1 = y;
          n++;
        }
      }
    }
    return n < 40 ? null : { x0, y0, x1, y1, n };
  }

  /**
   * 自适应缩放：不信任任何"标称尺寸"，直接量舞台上真正有像素的范围。
   * 实测 getLocalBounds() 给的是模型画布大小（1792x1260），而角色实际只占其中
   * 一部分；按标称尺寸缩放会让角色明显偏小。改成按实测包围盒反推比例。
   */
  function calibrate() {
    const src = extractStage();
    if (!src) return false;
    const box = alphaBox(src, W, H);
    if (!box) return false;

    const s = model.scale.x || 1;
    // 换成模型本地坐标（anchor 固定 0,0，本地坐标 = 舞台坐标 / scale）
    const lx0 = box.x0 / s, ly0 = box.y0 / s;
    const lw = (box.x1 - box.x0) / s, lh = (box.y1 - box.y0) / s;
    if (!isFinite(lw) || !isFinite(lh) || lw < 8 || lh < 8) return false;

    const target = Math.min(W / lw, H / lh) * 0.99;
    model.anchor.set(0, 0);
    model.scale.set(target);
    // 水平居中、底部贴齐舞台底边
    model.position.set(W / 2 - (lx0 + lw / 2) * target,
                       H - (ly0 + lh) * target);
    lastBox = { lw: Math.round(lw * target), lh: Math.round(lh * target) };
    return true;
  }

  function layout() {
    if (!app || !stageEl) return;
    const rect = stageEl.getBoundingClientRect();
    const w = Math.max(80, Math.round(rect.width));
    const h = Math.max(80, Math.round(rect.height));
    if (w === W && h === H) return;
    W = w; H = h;
    app.renderer.resize(W, H);
    if (!calibrate()) fit();
    requestMask();
  }

  function fit() {
    if (!model || !natural) return;
    const s = Math.min(W / natural.w, H / natural.h) * 0.99;
    model.anchor.set(0.5, 1);
    model.scale.set(s);
    model.position.set(W / 2, H);
  }

  // ── 遮罩：按模型实际不透明像素裁窗口 ──
  // WebView2 的"透明"只是透出宿主窗体，窗体本身是不透明的。所以真正的桌宠
  // 效果靠 SetWindowRgn：把窗口区域裁成角色的轮廓，窗外的地方根本不存在，
  // 桌面图标和别的窗口自然能点得到。
  // 阈值取高值（接近不透明）：角色轮廓是抗锯齿的，半透明边缘会被 WebView2
  // 合成到窗体的黑色底色上，形成一圈黑边——那不是裁剪扩出来的，
  // 就算把半透明像素排除在区域外，只要还留着一点点 alpha 就会发暗。
  // 这里宁可让轮廓硬一点，也不要那圈黑边。
  const MASK_ALPHA = 215;
  // 色带高度（设备像素）。必须和宿主 src/PetForm.cs 里的 BAND 保持一致，
  // 否则色带之间会漏出缝，屏幕上是一排横穿角色的黑条。
  const BAND = 1;
  let lastFill = 0;
  let preserve = false;
  let lastBox = null;
  let currentModel = 'half';


  /** 画布相对窗口顶部的偏移。舞台上面留了气泡的位置，不加回去遮罩会整体上移。 */
  function stageOffsetTop() {
    if (!stageEl) return 0;
    const r = stageEl.getBoundingClientRect();
    return Math.round(r.top);
  }

  /**
   * 界面上挡住桌面的那些框（气泡/对话面板/番茄钟挂件）也要留在窗口区域里，
   * 否则会被裁掉。但**必须报圆角**，宿主据此用圆角矩形而不是外接矩形——
   * 外接矩形的四个角是空的，那部分会露出窗体黑色底色，看上去就是气泡外面套了圈黑框。
   */
  function uiRects() {
    const out = [];
    const dpr = window.devicePixelRatio || 1;
    const R = { '#bubble': 14, '#chat': 14, '#badge': 9 };
    Object.keys(R).forEach(sel => {
      const el = document.querySelector(sel);
      if (!el || el.hidden) return;
      const r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) return;
      out.push({
        x: Math.round(r.left * dpr),
        y: Math.round(r.top * dpr),
        w: Math.round(r.width * dpr),
        h: Math.round(r.height * dpr),
        r: Math.round(R[sel] * dpr),
      });
    });

    return out;
  }

  function buildMaskSpans() {
    if (!app || !model || !W || !H) return [];

    // 用 Pixi 的 extract（渲染到 RenderTexture 再 readPixels），
    // 提取的是整个 stage 而不是模型本身——只有 stage 里才带着模型的位置，
    // 拿模型自己的包围盒会把角色画到遮罩左上角去，裁出来的形状完全不对。
    // 也不能直接 drawImage(WebGL 画布)：绘制缓冲在帧间会被丢，实测读回全透明。
    const src = extractStage();
    if (!src) { lastFill = -3; return []; }
    // 遮罩网格直接建立在**设备像素**上。之前按 CSS 行扫描、再乘 dpr 取整，
    // 2 CSS px 在 125% 缩放下变成 2.5，取整后行距忽大忽小，而色带高度是固定的
    // 2px，于是每两条之间漏出 1px——屏幕上就是横穿角色的一排黑条。
    const dpr = window.devicePixelRatio || 1;
    const mw = Math.max(80, Math.min(900, Math.round(W * dpr)));
    const mh = Math.max(1, Math.round(H * dpr));
    const c = document.createElement('canvas');
    c.width = mw; c.height = mh;
    const g = c.getContext('2d', { willReadFrequently: true });
    g.clearRect(0, 0, mw, mh);
    try { g.drawImage(src, 0, 0, mw, mh); }
    catch (e) { lastFill = -1; return []; }

    let d;
    try { d = g.getImageData(0, 0, mw, mh).data; }
    catch (e) { lastFill = -2; return []; }

    const spans = [];
    const toX = v => v;    // 网格已经是设备像素，1:1 对应窗口坐标
    const toY = v => v;
    // 画布在窗口里不是从 0 开始的（上面要给气泡留位置），
    // 而宿主的 region 用的是窗口坐标，所以这里必须把画布的偏移加回去
    const oy = Math.round(stageOffsetTop() * dpr);
    // 行高必须和宿主的 BAND 一致，否则色带之间会留缝
    const DY = BAND;

  // 两个阈值，因为要解决的是两件相反的事：
  //   ROW_ALPHA  低 —— 决定"这一行要不要"。蕾丝、碎发、发梢本来就半透明，
  //                    用高阈值会把身体掏成一排横条。
  //   EDGE_ALPHA 高 —— 决定"这一行左右到哪"。半透明的轮廓像素会被 WebView2
  //                    合成到窗体黑色底色上，形成一圈黑边，横向要收干净。
  // 每个设备像素单独扫描，不偏移行坐标，避免保留相邻透明行。
  const ROW_ALPHA = 60, EDGE_ALPHA = 170;
  let filled = 0;

  for (let y = 0; y < mh; y += DY) {
    // 先看这一行有没有内容
    let rowMax = 0;
    for (let x = 0; x < mw; x++) {
      const a = d[(y * mw + x) * 4 + 3];
      if (a > rowMax) rowMax = a;
    }
    if (rowMax < ROW_ALPHA) continue;

    let start = -1;
    for (let x = 0; x <= mw; x++) {
      const on = x < mw && d[(y * mw + x) * 4 + 3] >= EDGE_ALPHA;
      if (on && start < 0) start = x;
      else if (!on && start >= 0) {
        spans.push(toY(y) + oy, toX(start), toX(x));
        filled += x - start;
        start = -1;
      }
    }
  }

    // 一个像素都没读到 = 读回失败，别拿空遮罩去裁窗口
    lastFill = filled;
    if (filled < 40) return [];
    return spans;
  }

  // ── 每帧 ──
  function frame() {
    const now = performance.now();
    const dt = Math.min((now - last) / 1000, 0.1);
    last = now;
    const t = now / 1000;

    look.x = smooth(look.x, lookTarget.x, 7, dt);
    look.y = smooth(look.y, lookTarget.y, 7, dt);

    // 眨眼：随机 3~6 秒一次，闭眼 0.12 秒
    nextBlink -= dt;
    if (nextBlink <= 0 && blink >= 1) { blink = 0; nextBlink = 3 + Math.random() * 3; }
    if (blink < 1) blink = Math.min(1, blink + dt / 0.12);

    const pose = {
      // 待机：呼吸 + 极轻微的左右摇晃，越不动越明显
      ParamBreath: 0.5 + Math.sin(t * 1.4) * 0.5,
      ParamAngleX: look.x * 13 + Math.sin(t * 0.37) * 1.2,
      ParamAngleY: look.y * 9 + Math.sin(t * 0.29) * 0.8,
      ParamAngleZ: Math.sin(t * 0.23) * 2.2,
      ParamBodyAngleX: look.x * 2.4,
      ParamBodyAngleZ: Math.sin(t * 0.19) * 1.0,

      ParamEyeBallX: look.x,
      ParamEyeBallY: look.y * 0.8,
      ParamEyeLOpen: blink,
      ParamEyeROpen: blink,
      ParamMouthOpenY: 0,
      ParamMouthForm: 0,
    };

    if (pokeT > 0) {
      // 被吓到：头往后一仰再弹回来
      pokeT -= dt;
      const k = Math.max(0, pokeT / 0.5);
      pose.ParamAngleX += Math.sin(k * Math.PI * 3) * 9 * k;
      pose.ParamEyeLOpen = Math.min(pose.ParamEyeLOpen, 1 - k * 0.7);
      pose.ParamEyeROpen = Math.min(pose.ParamEyeROpen, 1 - k * 0.7);
    }

    Object.assign(pose, emotion);

    if (talking) {
      talkPhase += dt;
      // 用方波而不是正弦：正弦的嘴像在"呜"，方波更像在说话
      const mouth = 0.2 + 0.45 * Math.abs(Math.sin(talkPhase * 11));
      pose.ParamMouthOpenY = Math.max(pose.ParamMouthOpenY || 0, mouth);
      pose.ParamMouthForm = Math.max(pose.ParamMouthForm || 0, 0.15);
    }

    applyParams(pose);
  }

  // ── 遮罩调度 ──
  // 提取必须跑在渲染循环之外。在 beforeModelUpdate 里调 renderer.extract 等于
  // 在渲染途中重入渲染器，实测会让帧循环直接停掉（画面定格、遮罩也不再上报）。
  let maskPending = false, maskTimer = null, maskWarned = false;

  function requestMask() {
    maskPending = true;
    if (maskTimer) return;
    // 延到下一帧之后再做，确保当前这一帧已经画完
    maskTimer = setTimeout(function run() {
      maskTimer = null;
      if (!ready || !maskPending) return;
      maskPending = false;

      let spans = [];
      try { spans = buildMaskSpans(); }
      catch (e) { lastFill = -9; }

      if (spans.length >= 3) {
        maskWarned = false;
        onMask && onMask(spans, null, uiRects());
      } else if (!maskWarned) {
        maskWarned = true;
        // 读回失败必须让人看得见，否则表现只是「窗口没被裁」，根本猜不到是这一步坏了
        onMask && onMask(null, {
          phase: 'empty', W, H, filled: lastFill, keep: !!preserve,
          grid: [Math.round(W * (window.devicePixelRatio || 1)), Math.round(H * (window.devicePixelRatio || 1))],
          hasExtract: !!(app && app.renderer && app.renderer.extract),
          hasCanvasFn: !!(app && app.renderer && app.renderer.extract && app.renderer.extract.canvas)
        });
      }
    }, 80);
  }

  // 轮廓会随头部转动和头发摆动而变，所以除了按需重建，每隔 250ms 也要跟一次
  setInterval(() => { if (ready) requestMask(); }, 250);

  let onMask = null;

  return {
    async boot(el, maskCallback, which) {
      stageEl = el;
      onMask = maskCallback;
      const spec = MODELS[which] || MODELS.half;
      currentModel = which in MODELS ? which : 'half';
      if (!window.Live2DCubismCore) throw new Error('Cubism Core 加载失败');
      if (!window.PIXI) throw new Error('PixiJS 加载失败');

      W = Math.round(el.getBoundingClientRect().width) || 340;
      H = Math.round(el.getBoundingClientRect().height) || 260;

      app = new PIXI.Application({
        width: W, height: H,
        backgroundAlpha: 0,
        antialias: true,
        autoDensity: true,
        resolution: window.devicePixelRatio || 1,
        // 必须开：遮罩要把画布 alpha 读回来，而 WebGL 默认在帧末就丢弃绘制缓冲，
        // 不开这个的话在渲染帧之外 drawImage 拿到的是一张全透明的空图，
        // 于是遮罩算出来永远是空的，窗口裁剪静默失效。
        preserveDrawingBuffer: true,
      });
      // Pixi 未必认这个选项，读回是否真的可用要靠后面遮罩的 filled 数来验证，
      // 所以把它记下来，出问题时能一眼看出是不是这条没生效
      preserve = app.renderer.gl && !!app.renderer.gl.getContextAttributes().preserveDrawingBuffer;
      el.appendChild(app.view);

      model = await PIXI.live2d.Live2DModel.from(spec.url, { autoInteract: false, autoFocus: false });

      const native = model.internalModel.coreModel._model;
      if (!native.drawables.renderOrders && native.getRenderOrders) {
        if (native.offscreens && native.offscreens.count) throw new Error('当前渲染器不支持离屏部件');
        native.drawables.renderOrders = native.getRenderOrders();
      }

      model.anchor.set(0.5, 1);
      app.stage.addChild(model);

      natural = measure();
      fit();
      const ok = calibrate();
      if (!ok) console.warn('[pet] calibrate 失败，退回标称尺寸');

      // 参数在动作与物理之后写入，保证当前帧渲染用的是最新值
      model.internalModel.on('beforeModelUpdate', frame);

      // 表情名两个模型不一样（半身有 sparkle/sleepy，全身是 angry/happy），
      // 所以列出模型自己带的，交给上层去匹配，别在前端写死
      let exprNames = [];
      try {
        const em = model.internalModel.live2dExpressionManager;
        if (em && em.expressions) exprNames = em.expressions.map(e => e.Name);
      } catch (e) { }

      ready = true;
      requestMask();
      return { ver: VER, which, spec, natural, expressions: exprNames,
               params: Array.from(native.parameters.ids) };
    },

    relayout() { layout(); requestMask(); },
    markMaskDirty() { requestMask(); },
    rebuildMask() { requestMask(); },

    setLook(x, y) {
      lookTarget.x = clamp(x, -1, 1);
      lookTarget.y = clamp(y, -1, 1);
    },

    setExpression(name) {
      // 优先用模型自带的表情文件：两个模型的表情名和参数都不一样，
      // 走 expression() 才不会把半身的 sparkle 打到全身模型上找不到参数。
      if (!name || name === 'neutral') {
        emotion = {};
        try { model && model.expression(); } catch (e) { }
        return true;
      }
      try {
        if (model && typeof model.expression === 'function') {
          model.expression(name);
          emotion = {};                 // 已经由 expression 接管
          return true;
        }
      } catch (e) { /* 模型没这个表情，退回参数表 */ }
      emotion = Object.assign({}, CLEAR, EMOTIONS[name] || {});
      return !!EMOTIONS[name];
    },

    /** 实测出来的角色占位，用来告诉宿主窗口该开多大 */
    measureArt() {
      if (!lastBox) return null;
      const box = MODELS[currentModel] ? MODELS[currentModel].box : MODELS.half.box;
      return { w: lastBox.lw, h: lastBox.lh, top: box.top, want: box.w };
    },

    /** 点击：给一个小反应，不换表情（换表情由上层决定） */
    poke() { pokeT = 0.5; },

    setTalking(on) {
      talking = !!on;
      if (!on) talkPhase = 0;
    },

    setBreathAmp(v) { /* 预留：将来做"睡着"状态 */ },

    info() {
      return {
        ready, W, H,
        natural,
        art: lastBox,
        fps: app ? Math.round(PIXI.Ticker.shared.FPS) : 0,
      };
    },
  };
})();
