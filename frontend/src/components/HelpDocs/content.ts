import { accountHelp } from './accountHelp';
import { unregisteredHelp } from './unregisteredHelp';

export const chapterImages = {
  setup: ['setup'],
  account: ['account'],
  overview: ['overview'],
  search: ['search', 'detail'],
  unregistered: [],
  sources: ['source'],
  retention: ['retention'],
  users: ['users'],
} as const;

type ChapterKey = keyof typeof chapterImages;
type ImageKey = (typeof chapterImages)[ChapterKey][number];

interface HelpCopy {
  title: string;
  screenshotNote: string;
  chapters: Record<ChapterKey, { title: string; steps: readonly string[] }>;
  captions: Record<ImageKey, string>;
}

export const helpCopy = {
  'zh-CN': {
    title: '使用文档',
    screenshotNote: '图片使用简体中文界面和合成日志。点击图片可以放大。',
    chapters: {
      ...accountHelp['zh-CN'].chapters,
      unregistered: unregisteredHelp['zh-CN'],
      account: {
        title: '登录、外观与账号',
        steps: [
          '使用管理员分配的账号登录。点击右上角的地球图标可切换界面语言。',
          '点击账号名称，选择“主题设置”，再选择自动、浅色或深色。自动模式会跟随操作系统的外观设置。',
          '选择“修改密码”，输入当前密码、新密码并再次确认。修改成功后，该账号的所有会话都会失效，请用新密码重新登录。合成预览只演示操作流程，测试密码保持不变。',
          '选择“退出登录”结束当前会话。点击底部的标志或 TongYanInfo Site 可打开通研官网。',
        ],
      },
      overview: {
        title: '运行总览',
        steps: [
          '查看累计接收、写入与丢弃数量，了解当前接收和处理状态。累计计数从进程启动时开始计算。',
          '查看解析结果、接收队列和本地暂存占用。队列持续增长或丢弃量增加时，结合最近成功写入时间检查处理进度。',
          '开启“实时更新”后，页面通过 SignalR 接收服务器推送。断线时自动重连，期间数据停止更新；可暂停更新，或点击“刷新”获取最新状态并重试连接。来源已启用仅表示接收配置开启。',
        ],
      },
      search: {
        title: '日志检索与原始报文',
        steps: [
          '选择接收时间，按需添加来源、严重级别、主机名、应用名或正文关键词，再点击“查询日志”。时间按 Asia/Shanghai 显示，每次最多查询 30 天。',
          '正文关键词按区分大小写的英文和数字全词匹配。输入多个词时，结果需要同时包含所有词。使用上一页和下一页逐页查看。',
          '点击记录的“查看详情”检查解析字段。切换“文本预览”和“十六进制预览”可查看原始报文。看到编码异常或预览截断提示时，可结合十六进制视图核对已显示的原始字节。',
        ],
      },
      sources: {
        title: '日志来源',
        steps: [
          '点击“添加来源”，填写易于识别的名称和系统所看到的发送端 IP。',
          '选择来源时区，供系统解释未携带时区的设备时间。按需设置保留天数并开启接收，保存后在设备端配置 Syslog 转发。',
          '按名称、IP 或时区搜索来源。点击“编辑来源”调整配置。来源启用状态只代表配置是否允许接收。',
        ],
      },
      retention: {
        title: '日志保留',
        steps: [
          '找到目标来源，点击“调整保留期限”。输入 1～3650 天，或选用 1 年、2 年、3 年快捷项。',
          '核对变更前后的天数并确认。新期限从之后接收的日志开始生效，历史记录沿用原到期时间。',
          '到期日志会从查询中隐藏。当前保留期限控制查询可见性，磁盘空间需由运维另行管理。',
        ],
      },
    },
    captions: {
      ...accountHelp['zh-CN'].captions,
      account: '右上角的账号菜单与语言入口',
      overview: '运行总览与接收处理指标',
      search: '检索条件与日志结果列表',
      detail: '日志详情与原始报文预览',
      source: '来源列表与编辑入口',
      retention: '按来源配置日志保留期限',
    },
  },
  'zh-TW': {
    title: '使用說明',
    screenshotNote: '圖片使用簡體中文介面及合成日誌。點選圖片可放大。',
    chapters: {
      ...accountHelp['zh-TW'].chapters,
      unregistered: unregisteredHelp['zh-TW'],
      account: {
        title: '登入、外觀與帳號',
        steps: [
          '使用管理員分配的帳號登入。點選右上角的地球圖示即可切換介面語言。',
          '點選帳號名稱，選擇「主題設定」，再選擇自動、淺色或深色。自動模式會跟隨作業系統的外觀設定。',
          '選擇「修改密碼」，輸入目前的密碼及新密碼，然後再次確認。修改成功後，此帳號的所有工作階段都會失效，請用新密碼重新登入。合成預覽僅示範操作流程，測試密碼維持不變。',
          '選擇「登出」結束目前的工作階段。點選底部標誌或 TongYanInfo Site 可開啟通研官網。',
        ],
      },
      overview: {
        title: '運作總覽',
        steps: [
          '查看累計接收、寫入與捨棄數量，瞭解目前的接收和處理狀態。累計計數從程序啟動時開始計算。',
          '查看解析結果、接收佇列與本機暫存用量。佇列持續成長或捨棄數量增加時，可參考最近成功寫入時間檢查處理進度。',
          '開啟「即時更新」後，頁面透過 SignalR 接收伺服器推送。斷線時自動重新連線，期間資料停止更新；可暫停更新，或按下「重新整理」取得最新狀態並重試連線。來源已啟用僅表示接收設定已開啟。',
        ],
      },
      search: {
        title: '日誌搜尋與原始訊息',
        steps: [
          '選擇接收時間，依需要加入來源、嚴重程度、主機名稱、應用程式名稱或內文關鍵字，再查詢日誌。時間以 Asia/Shanghai 顯示，每次最多查詢 30 天。',
          '內文關鍵字以區分大小寫的英文及數字完整字詞比對。輸入多個字詞時，結果須同時包含所有字詞。使用上一頁和下一頁逐頁瀏覽。',
          '點選記錄的詳細資料以查看解析欄位。透過文字與十六進位預覽查看原始訊息。若出現編碼異常或截斷提示，可用十六進位預覽核對已顯示的原始位元組。',
        ],
      },
      sources: {
        title: '日誌來源',
        steps: [
          '點選新增來源，輸入容易辨識的名稱及系統所看到的傳送端 IP。',
          '選擇來源時區以解讀未帶時區的裝置時間。依需要設定保留天數並啟用接收。儲存後，在裝置端設定 Syslog 轉送。',
          '依名稱、IP 或時區搜尋來源，透過編輯功能調整設定。來源啟用狀態只表示設定是否允許接收。',
        ],
      },
      retention: {
        title: '日誌保留',
        steps: [
          '找到目標來源並調整保留期限。輸入 1～3650 天，或選擇 1、2、3 年的快捷選項。',
          '核對變更前後的天數並確認。新期限適用於後續接收的日誌，歷史記錄沿用原到期時間。',
          '到期日誌會在查詢時隱藏。目前保留期限控制查詢可見性，磁碟空間須由維運人員另行管理。',
        ],
      },
    },
    captions: {
      ...accountHelp['zh-TW'].captions,
      account: '右上角的帳號選單與語言入口',
      overview: '運作總覽與接收處理指標',
      search: '搜尋條件與日誌結果清單',
      detail: '日誌詳細資料與原始訊息預覽',
      source: '來源清單與編輯入口',
      retention: '依來源設定日誌保留期限',
    },
  },
  'ja-JP': {
    title: 'ユーザーガイド',
    screenshotNote:
      '画像は簡体字中国語の画面とサンプルログです。クリックすると拡大できます。',
    chapters: {
      ...accountHelp['ja-JP'].chapters,
      unregistered: unregisteredHelp['ja-JP'],
      account: {
        title: 'ログイン・外観・アカウント',
        steps: [
          '管理者から割り当てられたアカウントでログインします。右上の地球アイコンから画面の言語を切り替えられます。',
          'アカウント名からテーマ設定を開き、自動・ライト・ダークを選びます。自動モードでは OS の外観設定に合わせて切り替わります。',
          'パスワード変更を選び、現在のパスワードと新しいパスワードを入力し、確認します。変更後はアカウントの全セッションが終了するため、新しいパスワードでログインし直してください。プレビューでは手順のみを確認でき、テスト用パスワードは維持されます。',
          'ログアウトすると現在のセッションが終了します。フッターのロゴまたは TongYanInfo Site から公式サイトを開けます。',
        ],
      },
      overview: {
        title: '稼働概要',
        steps: [
          '受信・書き込み・破棄の累計と、現在の受信および処理の状態を確認します。累計はプロセス起動時から集計されます。',
          '解析結果、受信キュー、ローカル一時保存の使用量を確認します。キューが増え続ける場合や破棄が増加する場合は、直近の書き込み成功時刻と合わせて処理状況を確認してください。',
          '「リアルタイム更新」をオンにすると、SignalR 経由でサーバーから最新データを受信します。切断時は自動で再接続し、その間データの更新は止まります。更新を一時停止することも、「更新」を選択して最新の状態を取得し、再接続を試すこともできます。ログソースの有効状態は、受信設定がオンであることのみを示します。',
        ],
      },
      search: {
        title: 'ログ検索と元のメッセージ',
        steps: [
          '受信日時を選び、必要に応じてログソース、重大度、ホスト名、アプリ名、本文のキーワードで絞り込みます。時刻は Asia/Shanghai で表示され、1 回の検索範囲は最大 30 日です。',
          'キーワードは英数字の単語に大文字と小文字を区別して完全一致します。複数の単語を入力すると、すべてを含むログが表示されます。前後のページに移動して確認します。',
          'ログの詳細を開いて解析フィールドを確認します。元のメッセージはテキスト表示と 16 進表示を切り替えられます。文字コードの異常や省略の通知が出た場合は、16 進表示で表示範囲の元バイトを確認できます。',
        ],
      },
      sources: {
        title: 'ログソース',
        steps: [
          'ログソースを追加し、識別しやすい名前と、システムが受信時に認識する送信元 IP を入力します。',
          'タイムゾーンのない機器時刻を解釈するため、ソースのタイムゾーンを選びます。保管日数と受信の有効化を設定して保存し、機器側で Syslog 転送を設定します。',
          '名前、IP、タイムゾーンでログソースを検索し、編集から設定を変更できます。有効状態は受信を許可する設定を表します。',
        ],
      },
      retention: {
        title: 'ログの保管期間',
        steps: [
          '対象のログソースを探し、保管期間を変更します。1～3650 日を入力するか、1 年・2 年・3 年のプリセットを選びます。',
          '変更前後の日数を確認して確定します。新しい期間は以降に受信するログに適用され、既存ログの有効期限は維持されます。',
          '期限切れのログは検索結果から除外されます。保管期間は検索時の表示を制御するため、ディスク容量は別途管理してください。',
        ],
      },
    },
    captions: {
      ...accountHelp['ja-JP'].captions,
      account: '右上のアカウントメニューと言語切り替え',
      overview: '稼働概要と受信・処理の指標',
      search: '検索条件とログの検索結果',
      detail: 'ログ詳細と元のメッセージのプレビュー',
      source: 'ログソース一覧と編集操作',
      retention: 'ログソース別の保管期間設定',
    },
  },
  'ko-KR': {
    title: '사용 안내',
    screenshotNote:
      '그림에는 중국어 간체 화면과 합성 로그가 사용되었습니다. 클릭하면 확대됩니다.',
    chapters: {
      ...accountHelp['ko-KR'].chapters,
      unregistered: unregisteredHelp['ko-KR'],
      account: {
        title: '로그인, 테마 및 계정',
        steps: [
          '관리자가 할당한 계정으로 로그인합니다. 오른쪽 상단의 지구본 아이콘에서 화면 언어를 변경할 수 있습니다.',
          '계정 이름을 클릭하고 테마 설정에서 자동, 밝게, 어둡게 중 하나를 선택합니다. 자동 모드는 운영체제의 화면 테마를 따릅니다.',
          '비밀번호 변경을 선택하고 현재 비밀번호와 새 비밀번호를 입력한 뒤 확인합니다. 변경이 완료되면 계정의 모든 세션이 종료되므로 새 비밀번호로 다시 로그인합니다. 미리보기에서는 절차만 시연하며 테스트 비밀번호는 유지됩니다.',
          '로그아웃을 선택하면 현재 세션이 종료됩니다. 하단의 로고 또는 TongYanInfo Site를 클릭하면 공식 사이트가 열립니다.',
        ],
      },
      overview: {
        title: '운영 개요',
        steps: [
          '수신, 저장, 폐기된 메시지의 누계와 현재 수신 및 처리 상태를 확인합니다. 누계는 프로세스 시작 시점부터 집계됩니다.',
          '파싱 결과, 수신 대기열 및 로컬 임시 저장 공간의 사용량을 확인합니다. 대기열이나 폐기 건수가 계속 늘어나면 마지막 저장 성공 시각을 함께 확인합니다.',
          '실시간 업데이트를 켜면 SignalR을 통해 서버의 최신 데이터를 받습니다. 연결이 끊기면 자동으로 다시 연결하며, 그동안 데이터 업데이트는 중단됩니다. 업데이트를 일시 중지하거나 새로 고침을 눌러 최신 상태를 받고 연결을 다시 시도할 수 있습니다. 소스의 활성화 상태는 수신 설정이 켜져 있다는 의미입니다.',
        ],
      },
      search: {
        title: '로그 검색과 원본 메시지',
        steps: [
          '수신 시간을 선택하고 필요에 따라 소스, 심각도, 호스트명, 앱 이름 또는 본문 키워드를 지정한 뒤 검색합니다. 시간은 Asia/Shanghai 기준으로 표시되며 한 번에 최대 30일을 검색할 수 있습니다.',
          '본문 키워드는 대소문자를 구분하여 영문과 숫자로 된 단어 전체를 비교합니다. 여러 단어를 입력하면 모두 포함한 로그가 표시됩니다. 이전 및 다음 페이지로 이동해 결과를 확인합니다.',
          '로그 상세에서 파싱한 필드를 확인합니다. 원본 메시지는 텍스트나 16진수로 미리 볼 수 있습니다. 인코딩 오류나 잘림 안내가 표시되면 16진수로 표시된 원본 바이트를 확인합니다.',
        ],
      },
      sources: {
        title: '로그 소스',
        steps: [
          '소스를 추가하고 알아보기 쉬운 이름과 시스템이 수신할 때 확인하는 송신 IP를 입력합니다.',
          '시간대가 없는 장치 시각을 해석하는 데 사용할 소스 시간대를 선택합니다. 보존 기간과 수신 여부를 설정해 저장한 뒤 장치에서 Syslog 전달을 설정합니다.',
          '이름, IP 또는 시간대로 소스를 검색하고 편집으로 설정을 변경합니다. 활성화 상태는 수신 허용 설정을 나타냅니다.',
        ],
      },
      retention: {
        title: '로그 보존',
        steps: [
          '대상 소스를 찾아 보존 기간을 조정합니다. 1~3650일을 입력하거나 1년, 2년, 3년 옵션을 선택합니다.',
          '변경 전후 일수를 확인한 뒤 저장합니다. 새 기간은 이후에 수신하는 로그에 적용되며 기존 로그는 원래 만료 시점을 따릅니다.',
          '만료된 로그는 검색 결과에서 숨겨집니다. 현재 보존 기간은 검색에 표시되는 기간을 제어하며 디스크 공간은 별도로 관리해야 합니다.',
        ],
      },
    },
    captions: {
      ...accountHelp['ko-KR'].captions,
      account: '오른쪽 상단의 계정 메뉴와 언어 선택',
      overview: '운영 개요와 수신 및 처리 지표',
      search: '검색 조건과 로그 결과 목록',
      detail: '로그 상세와 원본 메시지 미리보기',
      source: '소스 목록과 편집 버튼',
      retention: '소스별 로그 보존 기간 설정',
    },
  },
  'ru-RU': {
    title: 'Руководство пользователя',
    screenshotNote:
      'На снимках показан интерфейс на упрощённом китайском языке с синтетическими журналами. Нажмите на снимок, чтобы увеличить его.',
    chapters: {
      ...accountHelp['ru-RU'].chapters,
      unregistered: unregisteredHelp['ru-RU'],
      account: {
        title: 'Вход, оформление и учётная запись',
        steps: [
          'Войдите с учётной записью, выданной администратором. Язык интерфейса можно выбрать с помощью значка глобуса в правом верхнем углу.',
          'Нажмите на имя учётной записи, откройте настройки темы и выберите автоматическую, светлую или тёмную тему. Автоматический режим следует настройкам операционной системы.',
          'Выберите смену пароля, введите текущий пароль, новый пароль и его подтверждение. После успешной смены все сеансы этой учётной записи завершатся. Войдите заново с новым паролем. В демонстрации можно пройти этот сценарий, при этом тестовый пароль сохранится.',
          'Выход завершает текущий сеанс. Логотип и надпись TongYanInfo Site внизу страницы открывают официальный сайт.',
        ],
      },
      overview: {
        title: 'Обзор работы системы',
        steps: [
          'Просмотрите счётчики принятых, записанных и отброшенных сообщений и текущее состояние приёма и обработки. Счётчики ведутся с момента запуска процесса.',
          'Проверьте результаты разбора, заполнение очереди приёма и локального буфера. Если очередь или число отброшенных сообщений растут, проверьте время последней успешной записи.',
          'Включите обновление в реальном времени, чтобы получать данные от сервера через SignalR. При разрыве соединения выполняется автоматическое переподключение; до его восстановления данные не обновляются. Можно приостановить обновление или нажать «Обновить», чтобы получить актуальное состояние и повторить подключение. Включённый источник означает только то, что приём разрешён в его настройках.',
        ],
      },
      search: {
        title: 'Поиск по журналам и исходные сообщения',
        steps: [
          'Выберите интервал приёма и при необходимости укажите источник, уровень важности, имя узла, приложение или ключевые слова. Затем запустите поиск. Время показано в зоне Asia/Shanghai, интервал одного запроса составляет до 30 дней.',
          'Поиск по тексту учитывает регистр и совпадение целых слов из латинских букв и цифр. Если указано несколько слов, должны присутствовать все. Просматривайте результаты с помощью кнопок предыдущей и следующей страницы.',
          'Откройте подробности записи, чтобы увидеть разобранные поля. Исходное сообщение доступно в текстовом и шестнадцатеричном виде. При предупреждении о кодировке или усечении проверьте отображаемые исходные байты в шестнадцатеричном виде.',
        ],
      },
      sources: {
        title: 'Источники журналов',
        steps: [
          'Добавьте источник, задайте понятное название и IP отправителя, который видит система при приёме.',
          'Укажите часовой пояс источника для интерпретации времени без часового пояса. Задайте срок хранения и разрешите приём по необходимости. Сохраните настройки и настройте пересылку Syslog на устройстве.',
          'Найдите источник по названию, IP или часовому поясу и откройте его для редактирования. Статус источника показывает, разрешён ли приём в настройках.',
        ],
      },
      retention: {
        title: 'Срок хранения журналов',
        steps: [
          'Выберите нужный источник и измените срок хранения. Укажите от 1 до 3650 дней или выберите 1, 2 или 3 года.',
          'Сверьте прежний и новый срок и подтвердите изменение. Новый срок применяется к журналам, принятым после изменения. Для предыдущих записей остаётся прежняя дата окончания хранения.',
          'Записи с истёкшим сроком скрываются из результатов поиска. Срок хранения управляет видимостью при поиске, а дисковое пространство контролируется отдельно.',
        ],
      },
    },
    captions: {
      ...accountHelp['ru-RU'].captions,
      account: 'Меню учётной записи и выбор языка в правом верхнем углу',
      overview: 'Обзор работы и показатели приёма и обработки',
      search: 'Условия поиска и список найденных записей',
      detail: 'Подробности записи и просмотр исходного сообщения',
      source: 'Список источников и кнопки редактирования',
      retention: 'Настройка срока хранения для отдельных источников',
    },
  },
  'en-US': {
    title: 'User guide',
    screenshotNote:
      'Screenshots show the Simplified Chinese interface with synthetic logs. Click an image to enlarge it.',
    chapters: {
      ...accountHelp['en-US'].chapters,
      unregistered: unregisteredHelp['en-US'],
      account: {
        title: 'Sign-in, appearance and account',
        steps: [
          'Sign in with the account assigned by your administrator. Use the globe icon in the upper right to change the interface language.',
          'Open the account menu and choose Theme settings. Select Automatic, Light or Dark. Automatic follows the operating system’s appearance setting.',
          'Choose Change password and enter the current password, the new password and its confirmation. A successful change ends all sessions for the account. Sign in again with the new password. The synthetic preview demonstrates this flow while keeping the test password available.',
          'Choose Sign out to end the current session. Click the footer logo or TongYanInfo Site to open the official website.',
        ],
      },
      overview: {
        title: 'System overview',
        steps: [
          'Review the totals for received, written and dropped messages, along with the current receiver and processing status. Totals accumulate from process start.',
          'Check the parse results, receive queue and local spool usage. If the backlog or dropped count grows, check the time of the most recent successful write.',
          'Enable Live updates to receive server updates through SignalR. The page reconnects automatically after a disconnection; data stops updating in the meantime. You can pause updates or select Refresh to get the latest status and retry the connection. An enabled source only means receiving is turned on in its configuration.',
        ],
      },
      search: {
        title: 'Log search and raw messages',
        steps: [
          'Select a received time range and optionally filter by source, severity, hostname, application or message keywords. Run the search. Times are shown in Asia/Shanghai and each search covers at most 30 days.',
          'Message keywords match whole words made of English letters or digits, with case sensitivity. All supplied words must match. Use Previous and Next to browse pages.',
          'Open a record’s details to see the parsed fields. Switch between text and hexadecimal previews to inspect the raw message. If you see an encoding or truncation notice, use the hexadecimal view to check the raw bytes shown.',
        ],
      },
      sources: {
        title: 'Log sources',
        steps: [
          'Select Add source and enter an identifiable name and the sender IP as seen by Syslog.',
          'Set the source time zone to interpret device timestamps without one. Set the retention period and enable receiving as needed. Save the source, then configure Syslog forwarding on the device.',
          'Find sources by name, IP or time zone and use Edit to adjust their settings. The enabled status indicates whether the configuration permits receiving.',
        ],
      },
      retention: {
        title: 'Log retention',
        steps: [
          'Find the source and choose Adjust retention. Enter 1 to 3650 days, or select the one-, two- or three-year preset.',
          'Review the old and new durations and confirm the change. The new duration applies to logs received afterward. Existing records keep their original expiry time.',
          'Expired logs are hidden from search results. The current retention setting controls search visibility. Manage disk space separately.',
        ],
      },
    },
    captions: {
      ...accountHelp['en-US'].captions,
      account: 'Account menu and language selector in the upper right',
      overview: 'System overview and receive and processing metrics',
      search: 'Search filters and log results',
      detail: 'Log details and raw message preview',
      source: 'Log sources and edit actions',
      retention: 'Retention settings by source',
    },
  },
} satisfies Record<string, HelpCopy>;
