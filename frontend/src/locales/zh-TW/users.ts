import type source from '../zh-CN/users';

export default {
  'menu.users': '使用者管理',
  'users.title': '使用者管理',
  'users.description': '設定可登入管理介面的帳號，管理啟用狀態與密碼。',
  'users.permissionHint':
    '目前所有帳號都是管理員，可查看全部日誌並變更系統設定。',
  'users.search': '搜尋使用者名稱',
  'users.filter': '帳號狀態',
  'users.all': '全部狀態',
  'users.enabled': '已啟用',
  'users.disabled': '已停用',
  'users.username': '使用者名稱',
  'users.role': '角色',
  'users.admin': '管理員',
  'users.createdAt': '建立時間',
  'users.actions': '操作',
  'users.current': '目前帳號',
  'users.add': '新增使用者',
  'users.edit': '編輯使用者',
  'users.enable': '啟用',
  'users.disable': '停用',
  'users.resetPassword': '重設密碼',
  'users.refresh': '重新整理',
  'users.total': '共 {total} 個帳號',
  'users.empty': '沒有符合條件的帳號',
  'users.unavailable': '帳號清單暫時無法使用',
  'users.loadFailed': '無法讀取帳號',
  'users.stale': '目前顯示上次讀取的結果，請重新整理後再操作。',
  'users.usernameRule':
    '使用者名稱須為 3～64 個英文字母、數字、點、連字號或底線。',
  'users.passwordRule': '密碼須為 8～1024 個字元，且不能全部為空白。',
  'users.confirmPassword': '確認密碼',
  'users.passwordMismatch': '兩次輸入的密碼不一致。',
  'users.saveFailed': '儲存失敗，請查看提示後重試。',
  'users.created': '已建立使用者。',
  'users.updated': '已儲存使用者設定。',
  'users.passwordReset': '密碼已重設，該帳號須使用新密碼重新登入。',
  'users.selfChanged': '帳號資料已變更，請重新登入。',
  'users.editHint': '變更使用者名稱後，該帳號的所有工作階段都會失效。',
  'users.selfHint':
    '不能停用目前登入的帳號。變更自己的密碼請使用右上角帳號選單。',
  'users.disableTitle': '確認停用帳號',
  'users.disableHint':
    '停用 {username} 後，該帳號將無法登入，現有工作階段會立即失效。',
  'users.resetTitle': '確認重設密碼',
  'users.resetHint': '重設 {username} 的密碼後，該帳號所有工作階段都會失效。',
  'users.confirm': '確認操作',
  'users.back': '返回修改',
  'users.previewHint':
    '合成預覽僅示範帳號操作，不儲存真實密碼。範例帳號統一使用 preview-only 登入。',
  'setup.title': '初始化管理員',
  'setup.description':
    '歡迎使用通研 Syslog，請先建立管理員帳號，開始設定日誌中心。',
  'setup.cardTitle': '建立第一位管理員',
  'setup.cardHint':
    '系統尚未設定帳號。請設定使用者名稱和密碼，完成後進入登入頁面。',
  'setup.submit': '建立管理員並繼續',
  'setup.restricted': '請在伺服器本機完成初始化',
  'setup.restrictedHint':
    '首次建立管理員僅允許透過伺服器本機的 HTTPS 入口提交，遠端安裝可使用受控通道。初始化完成後即可正常遠端登入。',
  'setup.unavailable': '暫時無法讀取初始化狀態',
  'setup.unavailableHint':
    '系統狀態尚未確認，請重試或檢查服務設定。目前不會建立帳號或變更儲存資料。',
  'setup.completed': '管理員已建立，請使用剛設定的帳號登入。',
  'setup.previewCompleted':
    '初始化預覽已完成，請使用剛設定的範例使用者名稱和 preview-only 登入。',
  'setup.previewHint':
    '這是合成初始化示範，請只填寫範例帳號。輸入的密碼不會傳送或儲存，完成後請使用範例使用者名稱和 preview-only 登入。',
  'setup.loginHint': '首次使用時系統會引導建立管理員，後續帳號由管理員設定。',
  'error.usernameConflict': '此使用者名稱已存在，請使用其他名稱。',
  'error.userVersion': '帳號已被其他操作變更，請重新整理清單後再編輯。',
  'error.lastAdmin': '至少須保留一個已啟用的管理員帳號。',
  'error.selfDisable': '不能停用目前登入的帳號。',
  'error.selfReset': '請使用右上角帳號選單變更自己的密碼。',
  'error.setupCompleted': '管理員已初始化，請返回登入頁面。',
  'error.setupLocal': '請透過伺服器本機的安全入口完成初始化。',
} satisfies Record<keyof typeof source, string>;
