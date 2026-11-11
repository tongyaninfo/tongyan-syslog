export default {
  'menu.httpsCertificate': 'HTTPS 憑證',
  'httpsCertificate.title': '管理介面 HTTPS 憑證',
  'httpsCertificate.current': '目前生效的憑證',
  'httpsCertificate.pending': '待生效憑證',
  'httpsCertificate.noCurrent': '目前沒有可顯示的憑證資訊。',
  'httpsCertificate.noPending': '目前沒有待生效憑證。',
  'httpsCertificate.subject': '憑證主體',
  'httpsCertificate.fingerprint': '憑證指紋',
  'httpsCertificate.notBefore': '生效時間（UTC+8）',
  'httpsCertificate.notAfter': '到期時間（UTC+8）',
  'httpsCertificate.upload': '上傳憑證',
  'httpsCertificate.file': '憑證檔案',
  'httpsCertificate.chooseFile': '選擇憑證檔案',
  'httpsCertificate.fileHint':
    '選擇含有私密金鑰的 .pfx 或 .p12 檔案，上限為 256 KiB。',
  'httpsCertificate.password': '憑證密碼（選填）',
  'httpsCertificate.readPasswordFile': '讀取密碼檔案',
  'httpsCertificate.passwordFileInvalid':
    '請選擇 4 KiB 以內的 UTF-8 TXT 檔案。密碼應為一行，最多 1024 個字元。',
  'httpsCertificate.save': '儲存憑證',
  'httpsCertificate.confirmTitle': '確認儲存 HTTPS 憑證',
  'httpsCertificate.confirmHint':
    '儲存後，管理介面的新 HTTPS 連線將自動使用新憑證。',
  'httpsCertificate.pendingNotice': '憑證已儲存，等待生效。',
  'httpsCertificate.saved':
    '新建 HTTPS 連線會使用新憑證。現有連線會保留原憑證直至中斷。若要確認新憑證，請重新啟動瀏覽器，再透過憑證涵蓋的網域名稱或 IP 存取。',
  'httpsCertificate.loadFailed': '無法載入已儲存的憑證',
  'httpsCertificate.fileRequired': '請選擇有效的 .pfx 或 .p12 檔案。',
  'httpsCertificate.tooLarge': '憑證檔案不得超過 256 KiB。',
  'httpsCertificate.invalid':
    '憑證或密碼無效。請確認檔案含有可用的私密金鑰，且憑證仍在有效期限內。',
  'httpsCertificate.unavailable': '憑證暫時無法使用，請檢查伺服器設定。',
  'httpsCertificate.managedExternally':
    'HTTPS 憑證由伺服器設定管理，請在伺服器上更新。',
  'httpsCertificate.selfSigned': '服務自簽 HTTPS 憑證',
  'httpsCertificate.uploaded': '已上傳的 HTTPS 憑證',
};
