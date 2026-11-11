export default {
  'menu.httpsCertificate': 'HTTPS 证书',
  'httpsCertificate.title': '管理台 HTTPS 证书',
  'httpsCertificate.current': '当前生效证书',
  'httpsCertificate.pending': '待生效证书',
  'httpsCertificate.noCurrent': '暂无当前证书信息。',
  'httpsCertificate.noPending': '暂无待生效证书。',
  'httpsCertificate.subject': '证书主题',
  'httpsCertificate.fingerprint': '证书指纹',
  'httpsCertificate.notBefore': '有效期开始（北京时间）',
  'httpsCertificate.notAfter': '有效期截止（北京时间）',
  'httpsCertificate.upload': '上传证书',
  'httpsCertificate.file': '证书文件',
  'httpsCertificate.chooseFile': '选择证书文件',
  'httpsCertificate.fileHint': '选择含私钥的 .pfx 或 .p12 文件，最大 256 KiB。',
  'httpsCertificate.password': '证书密码（可选）',
  'httpsCertificate.readPasswordFile': '读取密码文件',
  'httpsCertificate.passwordFileInvalid':
    '请选择 4 KiB 以内的 UTF-8 TXT 文件。密码应为一行，最多 1024 个字符。',
  'httpsCertificate.save': '保存证书',
  'httpsCertificate.confirmTitle': '确认保存 HTTPS 证书',
  'httpsCertificate.confirmHint':
    '保存后，管理台的新 HTTPS 连接将自动使用新证书。',
  'httpsCertificate.pendingNotice': '证书已保存，等待生效。',
  'httpsCertificate.saved':
    '新建 HTTPS 连接会使用新证书。已有连接会保留原证书直至断开。要核对新证书，请重启浏览器，再通过证书覆盖的域名或 IP 访问。',
  'httpsCertificate.loadFailed': '已保存证书未能加载',
  'httpsCertificate.fileRequired': '请选择有效的 .pfx 或 .p12 文件。',
  'httpsCertificate.tooLarge': '证书文件不能超过 256 KiB。',
  'httpsCertificate.invalid':
    '证书或密码无效，请检查文件是否含有可用私钥且仍在有效期内。',
  'httpsCertificate.unavailable': '证书暂不可用，请检查服务器配置。',
  'httpsCertificate.managedExternally':
    'HTTPS 证书由服务器配置管理，请在服务器上更新。',
  'httpsCertificate.selfSigned': '服务自签 HTTPS 证书',
  'httpsCertificate.uploaded': '已上传的 HTTPS 证书',
};
