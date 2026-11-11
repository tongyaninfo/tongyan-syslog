import { createHash, randomUUID } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { readFile, realpath, stat } from 'node:fs/promises';
import http from 'node:http';
import { isIP } from 'node:net';
import { networkInterfaces } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// 独立的本机 QA 服务：只读构建资源，业务状态全在内存，不连接真实后端或持久化数据。
const frontendRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '..',
);
const distRoot = process.env.SYSLOG_PREVIEW_DIST
  ? path.resolve(process.env.SYSLOG_PREVIEW_DIST)
  : path.join(frontendRoot, 'dist');
const port = Number(process.env.SYSLOG_PREVIEW_PORT ?? '8019');
const host = process.env.SYSLOG_PREVIEW_HOST ?? '127.0.0.1';
const loopbackAuthority = `127.0.0.1:${port}`;
const authorities = new Set([loopbackAuthority]);
const prefix = '/api/admin/v1';
const previewUser = {
  id: '9007199254740993',
  username: 'preview',
  role: 'admin',
};
const pageRoutes = new Set([
  '/',
  '/overview',
  '/logs',
  '/sources',
  '/unregistered',
  '/listeners',
  '/retention',
  '/user/login',
  '/user/setup',
  '/users',
]);
const scenarios = new Set([
  'normal',
  'empty',
  'error',
  'save-error',
  'expired',
  'setup',
  'setup-blocked',
  'setup-error',
]);
const contentTypes = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.wasm': 'application/wasm',
  '.txt': 'text/plain; charset=utf-8',
};
let scenario = 'normal';
// 合成会话按账号和版本隔离，重置/停用可验证所有旧会话失效。
const sessions = new Map();
let users = seedUsers();
let userSequence = 9007199254741100n;
let sourceSequence = 100n;
let sources = seedSources();
let logs = seedLogs();
let lastReceiveUnixMilliseconds = Date.parse(logs[0].receivedAt);
let unregistered = seedUnregistered();
let listenerSettings = seedListenerSettings();
let resolvedDistRoot;

function seedListenerSettings() {
  const configuration = {
    enabled: false,
    listeners: ['udp', 'tcp', 'tls', 'relp'].map((protocol, index) => ({
      protocol,
      enabled: false,
      address: '0.0.0.0',
      port: [514, 514, 6514, 2514][index],
      framing: 'octet-counting',
      certificateId: '',
      frameTimeoutSeconds: 30,
      idleTimeoutSeconds: 1800,
    })),
  };
  return {
    saved: { version: '0', configuration },
    running: structuredClone(configuration),
    restartRequired: false,
    certificates: [],
    connectionErrors: [],
    states: configuration.listeners.map(({ protocol, address, port }) => ({
      protocol,
      address,
      port,
      state: 'disabled',
      failureCode: '',
    })),
  };
}

class PreviewError extends Error {
  constructor(status, code, message) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

// 文档保留网段的合成样本，只演示未登记设备交互，不打开 Syslog 接收端口。
function seedUnregistered() {
  const now = Date.now();
  const epoch = randomUUID().replaceAll('-', '');
  return ['192.0.2.90', '2001:db8::90'].map((peerAddress, deviceIndex) => ({
    peerAddress,
    firstReceivedAt: new Date(now - 105000).toISOString(),
    samples: Array.from({ length: 100 }, (_, index) => {
      const sequence = 105 - index;
      const text =
        index === 0
          ? '<14><script>window.__syslogUnsafe = true</script> synthetic evidence'
          : index === 1
            ? '合成超长日志'.repeat(10000)
            : `<14>synthetic unregistered sample ${sequence}`;
      const raw =
        index === 2
          ? Buffer.from([0xff, 0xfe, 0x41])
          : Buffer.from(text).subarray(0, 65536);
      return {
        sampleKey: `${epoch}-${deviceIndex * 1000 + sequence}`,
        receivedAt: new Date(now - index * 1000).toISOString(),
        transport: deviceIndex === 0 ? 'udp' : 'tls',
        byteCount: raw.length,
        preview: raw.toString('utf8').slice(0, 256),
        rawBytes: raw.toString('base64'),
      };
    }),
  }));
}

function pendingDevices() {
  return unregistered.filter(
    (device) =>
      !sources.some((source) => source.peerAddress === device.peerAddress),
  );
}

function unregisteredSnapshot() {
  const pending = pendingDevices();
  return {
    items: pending.map((device) => ({
      peerAddress: device.peerAddress,
      firstReceivedAt: device.firstReceivedAt,
      lastReceivedAt: device.samples[0].receivedAt,
      transports: [
        ...new Set(device.samples.map((sample) => sample.transport)),
      ],
      received: 105,
      sampleCount: device.samples.length,
    })),
    status: {
      enabled: scenario !== 'empty',
      allowedNetworks: ['192.0.2.0/24', '2001:db8::/32'],
      deviceCount: pending.length,
      sampleCount: pending.reduce(
        (sum, device) => sum + device.samples.length,
        0,
      ),
      bufferedBytes: pending.reduce(
        (sum, device) =>
          sum +
          device.samples.reduce(
            (bytes, sample) => bytes + sample.byteCount + 512,
            0,
          ),
        0,
      ),
      maxBytes: 67108864,
      maxDevices: 1000,
      received: scenario === 'empty' ? 0 : 210,
      evicted: scenario === 'empty' ? 0 : 10,
      rejected: 0,
    },
  };
}

function seedUsers() {
  return [
    {
      ...previewUser,
      enabled: true,
      version: '1',
      createdAt: '2026-11-10T00:00:00.000Z',
    },
    {
      id: '9007199254740994',
      username: 'ops-example',
      role: 'admin',
      enabled: true,
      version: '1',
      createdAt: '2026-11-10T00:01:00.000Z',
    },
  ];
}

function currentUser(user) {
  return { id: user.id, username: user.username, role: user.role };
}

function validateUsername(input) {
  const username = typeof input === 'string' ? input.trim() : '';
  if (!/^[A-Za-z0-9._-]{3,64}$/.test(username))
    throw new PreviewError(400, 'username_invalid', '账号格式无效。');
  return username;
}

function validatePassword(input) {
  if (
    typeof input !== 'string' ||
    input.length < 8 ||
    input.length > 1024 ||
    !input.trim()
  )
    throw new PreviewError(400, 'password_policy', '密码不符合要求。');
}

function revokeSessions(user) {
  user.version = (BigInt(user.version) + 1n).toString();
  for (const [token, session] of sessions) {
    if (session.id === user.id) sessions.delete(token);
  }
}

function seedSources() {
  return [
    {
      id: '42',
      name: '核心交换机示例',
      peerAddress: '192.0.2.10',
      timeZoneId: 'Asia/Shanghai',
      retentionDays: 1095,
      enabled: true,
    },
    {
      id: '43',
      name: 'ESXi 示例',
      peerAddress: '198.51.100.20',
      timeZoneId: 'Asia/Shanghai',
      retentionDays: 730,
      enabled: true,
    },
    {
      id: '44',
      name: '应用日志示例',
      peerAddress: '203.0.113.30',
      timeZoneId: 'UTC',
      retentionDays: 365,
      enabled: false,
    },
  ];
}

function seedLogs() {
  const now = Date.now();
  const fixtures = seedSources();
  return Array.from({ length: 300 }, (_, index) => {
    const source = fixtures[index % fixtures.length];
    const receivedAt = new Date(now - 1000 - index * 10000).toISOString();
    const hostname = ['switch-example', 'esxi-example', 'application-example'][
      index % 3
    ];
    const appName = ['syslog', 'hostd', 'sshd'][index % 3];
    let message =
      index % 2 === 0
        ? `LINK down interface Gi1/0/${(index % 48) + 1} synthetic event ${index + 1}`
        : `LOGIN user synthetic-user result denied synthetic event ${index + 1}`;
    if (index === 0)
      message += ' <img src=x onerror="window.__syslogXssExecuted=true">';
    let raw = Buffer.from(
      `<34>1 ${receivedAt} ${hostname} ${appName} 123 TEST - ${message}`,
      'utf8',
    );
    let parseStatus = index % 29 === 0 && index > 2 ? 'unknown' : 'parsed';
    let failureCode = parseStatus === 'unknown' ? 'unrecognized_format' : '';
    if (index === 1) {
      // 精确 64 KiB 且保持合法 UTF-8，便于检查原文预览的截断提示和响应式布局。
      const head = Buffer.from(
        `<34>1 ${receivedAt} esxi-example hostd 123 TEST - LINK down `,
      );
      const fragment = Buffer.from('合成原始日志片段。', 'utf8');
      const count = Math.floor((65536 - head.length) / fragment.length);
      const body = Buffer.concat(Array.from({ length: count }, () => fragment));
      raw = Buffer.concat([
        head,
        body,
        Buffer.alloc(65536 - head.length - body.length, 0x20),
      ]);
      message = `LINK down ${body.toString('utf8')}`;
    }
    if (index === 2) {
      raw = Buffer.from([0x3c, 0x33, 0x34, 0x3e, 0xff, 0x00, 0xc3, 0x28]);
      message = 'LOGIN user synthetic invalid UTF-8 evidence';
      parseStatus = 'failed';
      failureCode = 'unsupported_message_encoding';
    }
    return {
      eventId: (9007199254740993n + BigInt(index)).toString(),
      batchId: 'synthetic-preview-batch',
      sourceId: source.id,
      peerAddress: source.peerAddress,
      transport: ['udp', 'tcp', 'tls'][index % 3],
      receivedAt,
      eventTime: parseStatus === 'parsed' ? receivedAt : null,
      expiresAt: new Date(now + source.retentionDays * 86400000).toISOString(),
      originalTimestamp: parseStatus === 'parsed' ? receivedAt : '',
      timeConfidence: parseStatus === 'parsed' ? 'explicit' : 'unknown',
      hostname,
      appName,
      procId: '123',
      msgId: 'TEST',
      structuredData: '',
      facility: 4,
      severity: index % 8,
      message,
      rawBytes: raw.toString('base64'),
      parseStatus,
      failureCode,
      parserVersion: 'rfc-v1',
    };
  });
}

function send(response, status, data = null, code = 'OK', message = '成功') {
  response.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  });
  response.end(
    JSON.stringify({
      success: status >= 200 && status < 300,
      code,
      message,
      data,
    }),
  );
}

function readJson(request) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    let settled = false;
    request.on('data', (chunk) => {
      if (settled) return;
      size += chunk.length;
      if (size > 65536) {
        settled = true;
        reject(new PreviewError(413, 'invalid_request', '请求内容过大。'));
        return;
      }
      chunks.push(chunk);
    });
    request.once('end', () => {
      if (settled) return;
      settled = true;
      try {
        const value = JSON.parse(Buffer.concat(chunks).toString('utf8'));
        if (value === null || typeof value !== 'object' || Array.isArray(value))
          throw new Error();
        resolve(value);
      } catch {
        reject(new PreviewError(400, 'invalid_request', '请求格式无效。'));
      }
    });
    request.once('aborted', () => {
      if (!settled) {
        settled = true;
        reject(new PreviewError(400, 'invalid_request', '请求已中断。'));
      }
    });
    request.once('error', () => {
      if (!settled) {
        settled = true;
        reject(new PreviewError(400, 'invalid_request', '请求读取失败。'));
      }
    });
  });
}

function normalizeSource(input) {
  if (
    typeof input.name !== 'string' ||
    !input.name.trim() ||
    input.name.trim().length > 128 ||
    typeof input.peerAddress !== 'string' ||
    !isIP(input.peerAddress) ||
    typeof input.timeZoneId !== 'string' ||
    input.timeZoneId.length > 128 ||
    !Number.isInteger(input.retentionDays) ||
    input.retentionDays < 1 ||
    input.retentionDays > 3650 ||
    (input.enabled !== undefined && typeof input.enabled !== 'boolean')
  ) {
    throw new PreviewError(400, 'INVALID_INPUT', '来源参数无效。');
  }
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: input.timeZoneId }).format(
      new Date(),
    );
  } catch {
    throw new PreviewError(400, 'INVALID_INPUT', '来源时区无效。');
  }
  let address = input.peerAddress;
  const mapped = /^::ffff:(\d+\.\d+\.\d+\.\d+)$/i.exec(address);
  if (mapped) address = mapped[1];
  else if (isIP(address) === 6)
    address = new URL(`http://[${address}]/`).hostname.slice(1, -1);
  return {
    name: input.name.trim(),
    peerAddress: address,
    timeZoneId: input.timeZoneId,
    retentionDays: input.retentionDays,
    enabled: input.enabled ?? true,
  };
}

function parseTime(value) {
  if (typeof value !== 'string' || !/(?:Z|[+-]\d{2}:\d{2})$/i.test(value))
    return NaN;
  const fraction = /\.(\d+)/.exec(value)?.[1] ?? '';
  if (fraction.length > 3 && /[1-9]/.test(fraction.slice(3))) return NaN;
  return Date.parse(value);
}

function search(query) {
  const from = parseTime(query.from);
  const to = parseTime(query.to);
  const pageSize = query.pageSize ?? 100;
  if (
    !Number.isFinite(from) ||
    !Number.isFinite(to) ||
    from >= to ||
    to - from > 30 * 86400000 ||
    !Number.isInteger(pageSize) ||
    pageSize < 1 ||
    pageSize > 500 ||
    (query.severity != null &&
      (!Number.isInteger(query.severity) ||
        query.severity < 0 ||
        query.severity > 7)) ||
    ['sourceId', 'hostname', 'appName', 'keywords'].some(
      (name) => query[name] != null && typeof query[name] !== 'string',
    ) ||
    (query.hostname?.length ?? 0) > 255 ||
    (query.appName?.length ?? 0) > 255 ||
    (query.keywords?.length ?? 0) > 512
  ) {
    throw new PreviewError(400, 'invalid_query', '查询条件无效。');
  }
  const keywords = query.keywords?.trim() ?? '';
  if (
    keywords &&
    ([...keywords].some((character) => character.charCodeAt(0) > 127) ||
      !/[A-Za-z0-9]/.test(keywords))
  ) {
    throw new PreviewError(
      400,
      'unsupported_search_token',
      '当前关键词仅支持 ASCII 全词匹配。',
    );
  }
  const scope = sources.map((source) => source.id);
  if (query.sourceId != null && !scope.includes(query.sourceId))
    throw new PreviewError(403, 'source_forbidden', '无可查询来源权限。');
  const tokens = keywords.match(/[A-Za-z0-9]+/g) ?? [];
  const filtered = logs.filter((item) => {
    const time = Date.parse(item.receivedAt);
    const itemTokens = new Set(item.message.match(/[A-Za-z0-9]+/g) ?? []);
    return (
      scope.includes(item.sourceId) &&
      time >= from &&
      time < to &&
      Date.parse(item.expiresAt) > Date.now() &&
      (query.sourceId == null || item.sourceId === query.sourceId) &&
      (query.severity == null || item.severity === query.severity) &&
      (query.hostname == null || item.hostname === query.hostname) &&
      (query.appName == null || item.appName === query.appName) &&
      tokens.every((token) => itemTokens.has(token))
    );
  });
  const fingerprint = createHash('sha256')
    .update(
      JSON.stringify({
        from,
        to,
        pageSize,
        source: query.sourceId ?? null,
        severity: query.severity ?? null,
        host: query.hostname ?? null,
        app: query.appName ?? null,
        keywords,
        scope,
      }),
    )
    .digest('hex');
  let offset = 0;
  if (query.cursor != null) {
    try {
      if (typeof query.cursor !== 'string' || query.cursor.length > 2048)
        throw new Error();
      const cursor = JSON.parse(
        Buffer.from(query.cursor, 'base64url').toString('utf8'),
      );
      if (cursor.fingerprint !== fingerprint) throw new Error();
      const last = filtered.findIndex(
        (item) => item.eventId === cursor.id && item.receivedAt === cursor.at,
      );
      if (last < 0) throw new Error();
      offset = last + 1;
    } catch {
      throw new PreviewError(400, 'invalid_query', '游标与查询条件不匹配。');
    }
  }
  const page = filtered.slice(offset, offset + pageSize);
  const hasMore = offset + page.length < filtered.length;
  const last = page.at(-1);
  const nextCursor =
    hasMore && last
      ? Buffer.from(
          JSON.stringify({
            fingerprint,
            id: last.eventId,
            at: last.receivedAt,
          }),
        ).toString('base64url')
      : null;
  return {
    items: page.map((item) => ({ ...item, rawBytes: '' })),
    nextCursor,
    hasMore,
  };
}

function statusSnapshot() {
  const empty = scenario === 'empty';
  return {
    state: empty ? 'disabled' : 'running',
    failureCode: '',
    listenerState: empty ? 'disabled' : 'listening',
    listenerFailureCode: '',
    received: empty ? 0 : 8640320,
    receivedBytes: empty ? 0 : 3317882880,
    dropped: empty ? 0 : 12,
    persisted: empty ? 0 : 8640208,
    parsed: empty ? 0 : 8639260,
    parseFailed: empty ? 0 : 48,
    parseUnknown: empty ? 0 : 1000,
    written: empty ? 0 : 8640000,
    writeFailures: 0,
    queuedBytes: empty ? 0 : 65536,
    spoolBytes: empty ? 0 : 524288,
    pendingBatches: empty ? 0 : 2,
    lastReceiveUnixMilliseconds: empty ? 0 : lastReceiveUnixMilliseconds,
    lastWriteUnixMilliseconds: empty ? 0 : Date.now() - 2000,
  };
}

async function serveFile(response, pathname, head) {
  const segments = pathname.split('/');
  if (
    segments.some((segment) => segment.startsWith('.')) ||
    pathname.toLowerCase().endsWith('.map')
  )
    return send(response, 404, null, 'not_found', '资源不存在。');
  const candidate = path.resolve(distRoot, `.${pathname}`);
  if (!candidate.startsWith(distRoot + path.sep))
    return send(response, 404, null, 'not_found', '资源不存在。');
  let actual;
  let info;
  try {
    actual = await realpath(candidate);
    info = await stat(actual);
  } catch {
    return send(response, 404, null, 'not_found', '资源不存在。');
  }
  if (!actual.startsWith(resolvedDistRoot + path.sep) || !info.isFile())
    return send(response, 404, null, 'not_found', '资源不存在。');
  const contentType = contentTypes[path.extname(actual).toLowerCase()];
  if (!contentType)
    return send(response, 404, null, 'not_found', '资源不存在。');
  response.writeHead(200, {
    'Content-Type': contentType,
    'Content-Length': info.size,
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  });
  if (head) return response.end();
  const stream = createReadStream(actual);
  stream.on('error', () => response.destroy());
  stream.pipe(response);
}

async function handler(request, response) {
  response.setHeader('Cache-Control', 'no-store');
  response.setHeader('X-Content-Type-Options', 'nosniff');
  const authority = request.headers.host;
  if (!authority || !authorities.has(authority))
    return send(response, 403, null, 'forbidden', '请求主机无效。');
  const origin = `http://${authority}`;
  let url;
  let pathname;
  try {
    url = new URL(request.url ?? '/', origin);
    if (url.origin !== origin)
      return send(response, 400, null, 'invalid_request', '请求地址无效。');
    pathname = decodeURIComponent(url.pathname);
  } catch {
    return send(response, 400, null, 'invalid_request', '请求地址无效。');
  }
  const method = request.method ?? 'GET';
  const isWrite = !['GET', 'HEAD', 'OPTIONS'].includes(method);
  if (
    isWrite &&
    (request.headers['x-syslog-request'] !== '1' ||
      (request.headers.origin && request.headers.origin !== origin))
  ) {
    return send(response, 403, null, 'csrf_rejected', '请求来源校验失败。');
  }

  if (pathname === '/__preview/scenario' && method === 'POST') {
    if (
      authority !== loopbackAuthority ||
      request.socket.remoteAddress !== '127.0.0.1'
    )
      return send(response, 403, null, 'forbidden', '场景切换仅支持本机访问。');
    const input = await readJson(request);
    if (!scenarios.has(input.scenario))
      throw new PreviewError(400, 'invalid_request', '预览场景无效。');
    scenario = input.scenario;
    if (scenario === 'normal') {
      listenerSettings = seedListenerSettings();
      sources = seedSources();
      logs = seedLogs();
      lastReceiveUnixMilliseconds = Date.parse(logs[0].receivedAt);
      unregistered = seedUnregistered();
      sourceSequence = 100n;
      users = seedUsers();
      sessions.clear();
      userSequence = 9007199254741100n;
    }
    if (scenario === 'empty') {
      listenerSettings = seedListenerSettings();
      sources = [];
      logs = [];
      unregistered = [];
    }
    if (['setup', 'setup-blocked'].includes(scenario)) {
      users = [];
      sessions.clear();
    }
    if (scenario === 'expired') sessions.clear();
    return send(response, 200, { scenario });
  }

  if (pathname.startsWith(`${prefix}/`)) {
    const route = pathname.slice(prefix.length);
    if (route === '/setup/status' && method === 'GET') {
      if (scenario === 'setup-error')
        return send(
          response,
          503,
          null,
          'service_unavailable',
          '合成初始化状态故障。',
        );
      return send(response, 200, {
        setupRequired: users.length === 0,
        canInitialize: users.length === 0 && scenario !== 'setup-blocked',
      });
    }
    if (route === '/setup/admin' && method === 'POST') {
      if (scenario === 'setup-error')
        return send(
          response,
          503,
          null,
          'service_unavailable',
          '合成初始化状态故障。',
        );
      if (scenario === 'setup-blocked')
        return send(
          response,
          403,
          null,
          'setup_local_required',
          '首次初始化仅允许服务器本机提交。',
        );
      const input = await readJson(request);
      const username = validateUsername(input.username);
      validatePassword(input.password);
      if (users.length)
        return send(
          response,
          409,
          null,
          'setup_already_completed',
          '管理员已初始化。',
        );
      // 演示不保留密码。所有合成账号仍使用公开 fixture 口令登录。
      const user = {
        id: (userSequence++).toString(),
        username,
        role: 'admin',
        enabled: true,
        version: '1',
        createdAt: new Date().toISOString(),
      };
      users.push(user);
      return send(response, 200, currentUser(user));
    }
    if (route === '/session/login' && method === 'POST') {
      const input = await readJson(request);
      // 这是公开的合成测试账号，不是系统默认管理员；Cookie 名称也与生产安全会话隔离。
      const user = users.find(
        (item) =>
          typeof input.username === 'string' &&
          item.username.toUpperCase() === input.username.trim().toUpperCase(),
      );
      if (!user?.enabled || input.password !== 'preview-only')
        return send(response, 401, null, 'AUTH_FAILED', '账号或密码无效。');
      if (scenario === 'expired')
        return send(
          response,
          401,
          null,
          'authentication_required',
          '会话已失效。',
        );
      const token = randomUUID();
      sessions.set(token, { id: user.id, version: user.version });
      response.setHeader(
        'Set-Cookie',
        `syslog_preview=${token}; HttpOnly; SameSite=Strict; Path=/`,
      );
      return send(response, 200, currentUser(user));
    }
    const knownRoute =
      (method === 'GET' &&
        [
          '/session',
          '/system/status',
          '/system/listeners',
          '/sources',
          '/users',
          '/unregistered-devices',
          '/unregistered-devices/logs',
          '/unregistered-devices/sample',
        ].includes(route)) ||
      (method === 'POST' &&
        [
          '/session/logout',
          '/session/password',
          '/sources',
          '/logs/search',
          '/logs/clear',
          '/users',
        ].includes(route)) ||
      (method === 'PUT' &&
        ['/system/listeners', '/system/listeners/reception'].includes(route)) ||
      (method === 'PUT' && /^\/sources\/[^/]+$/.test(route)) ||
      (method === 'PUT' && /^\/users\/[^/]+$/.test(route)) ||
      (method === 'POST' && /^\/users\/[^/]+\/password$/.test(route)) ||
      (method === 'GET' && /^\/logs\/[^/]+$/.test(route));
    if (!knownRoute)
      return send(response, 404, null, 'not_found', '接口不存在。');
    const token = /(?:^|;\s*)syslog_preview=([^;]+)(?:;|$)/.exec(
      request.headers.cookie ?? '',
    )?.[1];
    const session = sessions.get(token);
    const actor = users.find((item) => item.id === session?.id);
    const authenticated =
      actor?.enabled &&
      actor.version === session?.version &&
      scenario !== 'expired';
    if (!authenticated)
      return send(response, 401, null, 'authentication_required', '请先登录。');
    if (route === '/session' && method === 'GET')
      return send(response, 200, currentUser(actor));
    if (
      ['/session/logout', '/session/password'].includes(route) &&
      method === 'POST'
    ) {
      if (route === '/session/password') {
        const { currentPassword, newPassword } = await readJson(request);
        if (
          typeof newPassword !== 'string' ||
          newPassword.length < 8 ||
          newPassword.length > 1024 ||
          !newPassword.trim()
        )
          return send(
            response,
            400,
            null,
            'password_policy',
            '新密码不符合要求。',
          );
        if (currentPassword !== 'preview-only')
          return send(
            response,
            400,
            null,
            'current_password_invalid',
            '当前密码不正确。',
          );
        if (newPassword === currentPassword)
          return send(
            response,
            400,
            null,
            'password_unchanged',
            '新密码不能与当前密码相同。',
          );
        // 只模拟会话撤销，不改变演示账号的固定口令。
        revokeSessions(actor);
      }
      sessions.delete(token);
      response.setHeader(
        'Set-Cookie',
        'syslog_preview=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0',
      );
      return send(response, 200, true);
    }
    if (
      scenario === 'error' &&
      ((method === 'GET' &&
        ['/system/status', '/system/listeners', '/sources', '/users'].includes(
          route,
        )) ||
        route.startsWith('/logs/') ||
        route.startsWith('/unregistered-devices'))
    ) {
      return send(
        response,
        503,
        null,
        'service_unavailable',
        '合成服务故障场景。',
      );
    }
    if (route === '/system/status' && method === 'GET')
      return send(response, 200, statusSnapshot());
    if (route === '/system/listeners/reception') {
      const input = await readJson(request);
      if (scenario === 'save-error')
        throw new PreviewError(
          503,
          'service_unavailable',
          '合成切换失败场景。',
        );
      if (typeof input?.enabled !== 'boolean')
        throw new PreviewError(400, 'invalid_request', '接收开关无效。');
      const current = listenerSettings.saved.configuration;
      if (input.enabled && !current.listeners.some((item) => item.enabled))
        throw new PreviewError(
          400,
          'listener_no_enabled_protocols',
          '请先启用并保存至少一种协议。',
        );
      const configuration = { ...current, enabled: input.enabled };
      listenerSettings.saved = {
        version: (BigInt(listenerSettings.saved.version) + 1n).toString(),
        configuration,
      };
      // 合成预览只演示开关与快照变化，网络监听仍由实际后端负责。
      listenerSettings.running = input.enabled
        ? structuredClone(configuration)
        : { ...listenerSettings.running, enabled: false };
      listenerSettings.states = listenerSettings.running.listeners.map(
        ({ protocol, address, port, enabled }) => ({
          protocol,
          address,
          port,
          state: input.enabled && enabled ? 'listening' : 'disabled',
          failureCode: '',
        }),
      );
      listenerSettings.restartRequired =
        JSON.stringify(configuration) !==
        JSON.stringify(listenerSettings.running);
      return send(response, 200, listenerSettings);
    }
    if (route === '/system/listeners') {
      if (method === 'GET') return send(response, 200, listenerSettings);
      const input = await readJson(request);
      if (scenario === 'save-error')
        throw new PreviewError(
          503,
          'service_unavailable',
          '合成保存失败场景。',
        );
      if (input.version !== listenerSettings.saved.version)
        throw new PreviewError(409, 'version_conflict', '配置版本已改变。');
      const configuration = input.configuration;
      const protocols = ['udp', 'tcp', 'tls', 'relp'];
      if (
        typeof configuration?.enabled !== 'boolean' ||
        !Array.isArray(configuration.listeners) ||
        configuration.listeners.length !== 4 ||
        new Set(configuration.listeners.map((item) => item.protocol)).size !==
          4 ||
        configuration.listeners.some(
          (item) =>
            !protocols.includes(item.protocol) ||
            typeof item.enabled !== 'boolean' ||
            !isIP(item.address ?? '') ||
            !Number.isInteger(item.port) ||
            item.port < 1 ||
            item.port > 65535 ||
            (!['octet-counting', 'newline'].includes(item.framing) &&
              !(
                item.framing === 'auto' &&
                ['tcp', 'tls'].includes(item.protocol)
              )) ||
            typeof item.certificateId !== 'string' ||
            !Number.isInteger(item.frameTimeoutSeconds) ||
            item.frameTimeoutSeconds < 1 ||
            item.frameTimeoutSeconds > 300 ||
            !Number.isInteger(item.idleTimeoutSeconds) ||
            item.idleTimeoutSeconds < 1 ||
            item.idleTimeoutSeconds > 86400,
        )
      )
        throw new PreviewError(
          400,
          'invalid_listener_configuration',
          '接收配置无效。',
        );
      if (
        configuration.listeners.some(
          (item) => item.protocol === 'tls' && item.enabled,
        )
      )
        throw new PreviewError(
          400,
          'listener_certificate_missing',
          '合成预览没有服务器证书。',
        );
      // 保存只更新待生效快照，实时总开关负责切换合成监听状态。
      listenerSettings.saved = {
        version: (BigInt(listenerSettings.saved.version) + 1n).toString(),
        configuration: structuredClone(configuration),
      };
      listenerSettings.restartRequired =
        JSON.stringify(configuration) !==
        JSON.stringify(listenerSettings.running);
      return send(response, 200, listenerSettings);
    }
    if (route === '/sources' && method === 'GET')
      return send(response, 200, sources);
    if (route === '/unregistered-devices' && method === 'GET')
      return send(response, 200, unregisteredSnapshot());
    if (route.startsWith('/unregistered-devices/') && method === 'GET') {
      const peerAddress = url.searchParams.get('peerAddress');
      if (!isIP(peerAddress ?? ''))
        return send(
          response,
          400,
          null,
          'invalid_request',
          '请提供有效来源 IP。',
        );
      const device = pendingDevices().find(
        (item) => item.peerAddress === peerAddress,
      );
      if (!device)
        return send(
          response,
          404,
          null,
          'not_found',
          '设备已登记或样本已淘汰。',
        );
      if (route === '/unregistered-devices/logs')
        return send(
          response,
          200,
          device.samples.map(({ rawBytes: _raw, ...summary }) => summary),
        );
      const sample = device.samples.find(
        (item) => item.sampleKey === url.searchParams.get('sampleKey'),
      );
      if (!sample)
        return send(response, 404, null, 'not_found', '样本已淘汰。');
      const { sampleKey, receivedAt, transport, rawBytes } = sample;
      return send(response, 200, {
        sampleKey,
        receivedAt,
        transport,
        rawBytes,
      });
    }
    if (route === '/users' && method === 'GET') {
      const page = Number(url.searchParams.get('page') ?? '1');
      const pageSize = Number(url.searchParams.get('pageSize') ?? '20');
      const search = url.searchParams.get('search') ?? '';
      const enabled = url.searchParams.get('enabled');
      if (
        !Number.isSafeInteger(page) ||
        page < 1 ||
        !Number.isSafeInteger(pageSize) ||
        pageSize < 1 ||
        pageSize > 100 ||
        search.length > 64 ||
        (enabled !== null && !['true', 'false'].includes(enabled))
      )
        return send(response, 400, null, 'invalid_request', '查询条件无效。');
      const filtered = users.filter(
        (item) =>
          item.username.toLowerCase().includes(search.trim().toLowerCase()) &&
          (enabled === null || item.enabled === (enabled === 'true')),
      );
      return send(response, 200, {
        items: filtered.slice((page - 1) * pageSize, page * pageSize),
        total: filtered.length,
        page,
        pageSize,
      });
    }
    if (
      (route === '/users' && method === 'POST') ||
      /^\/users\/[^/]+(?:\/password)?$/.test(route)
    ) {
      if (scenario === 'save-error')
        return send(
          response,
          503,
          null,
          'service_unavailable',
          '合成保存失败场景。',
        );
      const input = await readJson(request);
      if (route === '/users') {
        const username = validateUsername(input.username);
        validatePassword(input.password);
        if (
          users.some(
            (item) => item.username.toUpperCase() === username.toUpperCase(),
          )
        )
          return send(
            response,
            409,
            null,
            'username_conflict',
            '用户名已存在。',
          );
        const user = {
          id: (userSequence++).toString(),
          username,
          role: 'admin',
          enabled: true,
          version: '1',
          createdAt: new Date().toISOString(),
        };
        users.push(user);
        return send(response, 200, user);
      }
      const user = users.find((item) => item.id === route.split('/')[2]);
      if (!user)
        return send(response, 404, null, 'user_not_found', '账号不存在。');
      if (input.expectedVersion !== user.version)
        return send(
          response,
          409,
          null,
          'version_conflict',
          '账号已变更，请刷新后重试。',
        );
      if (route.endsWith('/password')) {
        if (user.id === actor.id)
          return send(
            response,
            403,
            null,
            'self_reset_forbidden',
            '本人请使用修改密码。',
          );
        validatePassword(input.newPassword);
        revokeSessions(user);
        return send(response, 200, true);
      }
      const username = validateUsername(input.username);
      if (typeof input.enabled !== 'boolean')
        return send(response, 400, null, 'invalid_request', '启用状态无效。');
      if (!input.enabled && user.id === actor.id)
        return send(
          response,
          403,
          null,
          'self_disable_forbidden',
          '不能停用当前账号。',
        );
      if (
        !input.enabled &&
        user.enabled &&
        users.filter((item) => item.enabled).length <= 1
      )
        return send(
          response,
          403,
          null,
          'last_admin_required',
          '必须保留一个启用管理员。',
        );
      if (
        users.some(
          (item) =>
            item.id !== user.id &&
            item.username.toUpperCase() === username.toUpperCase(),
        )
      )
        return send(response, 409, null, 'username_conflict', '用户名已存在。');
      if (user.username !== username || user.enabled !== input.enabled) {
        user.username = username;
        user.enabled = input.enabled;
        revokeSessions(user);
      }
      return send(response, 200, user);
    }
    if (
      (route === '/sources' && method === 'POST') ||
      (route.startsWith('/sources/') && method === 'PUT')
    ) {
      if (scenario === 'save-error')
        return send(
          response,
          503,
          null,
          'service_unavailable',
          '合成保存失败场景。',
        );
      const source = normalizeSource(await readJson(request));
      const id =
        method === 'POST'
          ? (sourceSequence++).toString()
          : route.slice('/sources/'.length);
      const existing = sources.findIndex((item) => item.id === id);
      if (method === 'PUT' && existing < 0)
        return send(response, 404, null, 'NOT_FOUND', '来源不存在。');
      if (
        sources.some(
          (item) => item.peerAddress === source.peerAddress && item.id !== id,
        )
      )
        return send(response, 409, null, 'CONFLICT', '来源地址已登记。');
      const saved = { id, ...source };
      if (existing >= 0) sources[existing] = saved;
      else sources.push(saved);
      return send(response, 200, saved);
    }
    if (route === '/logs/search' && method === 'POST')
      return send(response, 200, search(await readJson(request)));
    if (route === '/logs/clear' && method === 'POST') {
      const input = await readJson(request);
      if (input?.confirmation !== 'CLEAR')
        return send(
          response,
          400,
          null,
          'log_clear_confirmation_required',
          '请输入 CLEAR 确认。',
        );
      // 合成预览的清空范围仅为当前进程中的样本日志。
      logs = [];
      return send(response, 200, true);
    }
    if (route.startsWith('/logs/') && method === 'GET') {
      const id = route.slice('/logs/'.length);
      const receivedAt = parseTime(url.searchParams.get('receivedAt'));
      if (!/^\d+$/.test(id) || !Number.isFinite(receivedAt))
        return send(
          response,
          400,
          null,
          'invalid_query',
          '日志标识或时间无效。',
        );
      const item = logs.find(
        (entry) =>
          entry.eventId === id &&
          Date.parse(entry.receivedAt) === receivedAt &&
          sources.some((source) => source.id === entry.sourceId) &&
          Date.parse(entry.expiresAt) > Date.now(),
      );
      return item
        ? send(response, 200, item)
        : send(response, 404, null, 'not_found', '日志不存在或已过期。');
    }
    return send(response, 404, null, 'not_found', '接口不存在。');
  }
  if (
    pathname === '/api' ||
    pathname.startsWith('/api/') ||
    pathname.startsWith('/__preview/')
  )
    return send(response, 404, null, 'not_found', '接口不存在。');
  if (!['GET', 'HEAD'].includes(method))
    return send(response, 404, null, 'not_found', '资源不存在。');
  const pagePath = pathname.length > 1 ? pathname.replace(/\/$/, '') : pathname;
  if (pageRoutes.has(pagePath) || pathname === '/index.html') {
    const indexPath = await realpath(path.join(distRoot, 'index.html'));
    if (!indexPath.startsWith(resolvedDistRoot + path.sep))
      return send(response, 404, null, 'not_found', '资源不存在。');
    const source = await readFile(indexPath, 'utf8');
    const marker = '<meta name="syslog-synthetic-preview" content="1">';
    const html = /<head(?:\s[^>]*)?>/i.test(source)
      ? source.replace(/<head(?:\s[^>]*)?>/i, (match) => match + marker)
      : marker + source;
    response.writeHead(200, {
      'Content-Type': 'text/html; charset=utf-8',
      'Content-Length': Buffer.byteLength(html),
      'Cache-Control': 'no-store',
      'X-Content-Type-Options': 'nosniff',
    });
    return response.end(method === 'HEAD' ? undefined : html);
  }
  return serveFile(response, pathname, method === 'HEAD');
}

async function start() {
  if (
    !Number.isInteger(port) ||
    port < 1024 ||
    port > 65535 ||
    (host !== '127.0.0.1' && host !== '0.0.0.0')
  ) {
    console.error(
      'Use SYSLOG_PREVIEW_HOST 127.0.0.1 or 0.0.0.0 and a port from 1024 to 65535.',
    );
    process.exitCode = 1;
    return;
  }
  if (host === '0.0.0.0') {
    // 全网卡监听时仍只接受启动时本机实际拥有的 IPv4 地址，写请求继续校验同源。
    for (const addresses of Object.values(networkInterfaces())) {
      for (const address of addresses ?? []) {
        if (address.family === 'IPv4' && !address.internal)
          authorities.add(`${address.address}:${port}`);
      }
    }
  }
  resolvedDistRoot = await realpath(distRoot);
  const indexPath = await realpath(path.join(distRoot, 'index.html'));
  if (!indexPath.startsWith(resolvedDistRoot + path.sep))
    throw new Error('preview_build_invalid');
  const index = await stat(indexPath);
  if (!index.isFile()) throw new Error('preview_build_missing');
  const server = http.createServer((request, response) => {
    Promise.resolve(handler(request, response)).catch((error) => {
      if (response.headersSent) {
        response.destroy();
        return;
      }
      if (error instanceof PreviewError)
        send(response, error.status, null, error.code, error.message);
      else
        send(response, 503, null, 'service_unavailable', '合成预览暂不可用。');
    });
  });
  server.requestTimeout = 15000;
  server.headersTimeout = 10000;
  server.keepAliveTimeout = 5000;
  server.on('error', () => {
    console.error(
      'Synthetic preview could not start. Check the build and local port.',
    );
    process.exitCode = 1;
  });
  server.listen(port, host, () => {
    console.log(
      `Synthetic preview listening on ${host}:${port} (PID ${process.pid})`,
    );
    for (const authority of authorities) console.log(`  http://${authority}`);
  });
  let closing = false;
  const close = () => {
    if (closing) return;
    closing = true;
    server.close(() => {
      process.exitCode = 0;
    });
    server.closeIdleConnections();
    const timeout = setTimeout(() => server.closeAllConnections(), 5000);
    timeout.unref();
  };
  process.once('SIGTERM', close);
  process.once('SIGINT', close);
}

start().catch(() => {
  console.error(
    'Build frontend/dist/index.html before starting the synthetic preview.',
  );
  process.exitCode = 1;
});
