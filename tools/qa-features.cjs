// 接收实际 HTTP 请求，核对图片输入，并提供可中断的慢速流。
const http = require('http');
http.createServer((req, res) => {
  let raw = '';
  req.on('data', chunk => { raw += chunk; });
  req.on('end', () => {
    const body = JSON.parse(raw);
    const content = body.messages.at(-1).content;
    const text = Array.isArray(content) ? content.find(part => part.type === 'text').text : content;
    let reply = '原生流式回复验证成功。';
    if (Array.isArray(content)) {
      const image = content.find(part => part.type === 'image_url')?.image_url.url;
      if (!image?.startsWith('data:image/jpeg;base64,') || body.model !== 'qa-vision') { res.writeHead(400); res.end('图片请求不符合预期'); return; }
      reply = '图片请求验证成功。';
    }
    res.setHeader('Content-Type', 'text/event-stream');
    const write = value => res.write('data: ' + JSON.stringify({ choices: [{ delta: { content: value } }] }) + '\n\n');
    if (text === '验证打断') {
      write('开始回复');
      const timer = setInterval(() => write('后续文本'), 500);
      res.on('close', () => clearInterval(timer));
      return;
    }
    for (const value of reply) write(value);
    res.end('data: [DONE]\n\n');
  });
}).listen(4181, '127.0.0.1');
