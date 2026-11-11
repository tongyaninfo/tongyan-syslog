export default {
  'listeners.legacyConfiguration':
    'В прежней конфигурации сервера несколько слушателей одного протокола. Оставьте на сервере по одному слушателю каждого протокола и перезапустите службу. Текущая рабочая конфигурация не изменена.',
  'listeners.failure.io':
    'Ошибка привязки или сетевого ввода-вывода. Проверьте занятость порта, адрес и права.',
  'listeners.failure.connectionIo':
    'При чтении или записи последнего клиентского соединения произошла ошибка. Порт продолжает принимать новые соединения.',
  'listeners.failure.tls':
    'Не удалось запустить TLS. Проверьте сертификат сервера, закрытый ключ и срок действия.',
  'listeners.failure.sources':
    'Настройки источников недоступны. Приём приостановлен.',
  'listeners.failure.configuration':
    'Не удалось загрузить настройки запуска. Проверьте базу управления и версию конфигурации.',
  'listeners.failure.forcedDisabled':
    'Приём принудительно отключён сервером. Проверьте настройки запуска.',
  'menu.listeners': 'Настройки приёма',
  'listeners.title': 'Настройки приёма',
  'listeners.description': 'Настройте приём журналов по UDP, TCP, TLS и RELP.',
  'listeners.reload': 'Перечитать настройки',
  'listeners.restartHint':
    'После сохранения выключите и снова включите нужный протокол или общий приём, чтобы применить параметры.',
  'listeners.boundary':
    'Отключение общего приёма останавливает все слушатели и закрывает их соединения. Состояние отдельных протоколов можно сохранять и при выключенном приёме.',
  'listeners.loadFailed': 'Не удалось загрузить настройки',
  'listeners.saved': 'Настройки сохранены',
  'listeners.running': 'Текущая рабочая конфигурация',
  'listeners.pending': 'Ожидает применения',
  'listeners.current': 'Конфигурация загружена',
  'listeners.master': 'Приём журналов',
  'listeners.masterHint':
    'Если общий приём включён, переключение протокола затрагивает только этот протокол.',
  'listeners.protocol': 'Протокол',
  'listeners.address': 'Адрес привязки',
  'listeners.port': 'Порт',
  'listeners.state': 'Состояние слушателя',
  'listeners.failure': 'Ошибка',
  'listeners.listenerFailed':
    'Ошибка приёма. Проверьте адрес, порт и настройки сертификата.',
  'listeners.savedConfiguration': 'Сохранённая конфигурация',
  'listeners.enabled': 'Включить протокол',
  'listeners.addressRequired': 'Введите адрес IPv4 или IPv6.',
  'listeners.portRequired': 'Порт должен быть целым числом от 1 до 65535.',
  'listeners.framing': 'Разделение сообщений',
  'listeners.connectionErrors': 'Ошибки подключений клиентов',
  'listeners.clientAddress': 'IP клиента',
  'listeners.errorOccurredAt': 'Время ошибки',
  'listeners.auto': 'Автоопределение (число байтов / перевод строки)',
  'listeners.octet': 'По числу байтов',
  'listeners.newline': 'По строкам (LF / CRLF)',
  'listeners.certificate': 'Сертификат TLS',
  'listeners.certificateRequired':
    'Для TLS выберите доступный сертификат сервера.',
  'listeners.noCertificates':
    'Сертификаты сервера не настроены. Настройте сертификат на сервере и перезапустите службу. Эта страница не собирает закрытые ключи или пароли.',
  'listeners.frameTimeout': 'Таймаут кадра (сек.)',
  'listeners.idleTimeout': 'Таймаут простоя (сек.)',
  'listeners.timeoutRequired':
    'Введите целое число секунд в допустимом диапазоне.',
  'listeners.relpHint':
    'Подтверждение RELP означает принятие события в обработку, а не запись в базу журналов. Для незарегистрированных устройств хранятся лишь последние 100 образцов в памяти до перезапуска.',
  'listeners.save': 'Сохранить настройки',
  'listeners.confirmTitle': 'Подтвердите сохранение',
  'listeners.confirmSave': 'Подтвердить',
  'listeners.confirmHint':
    'Проверьте протокол, адрес и порт назначения на устройствах. После сохранения выключите и снова включите нужный протокол или общий приём.',
  'listeners.saveFailed': 'Не удалось подтвердить результат сохранения',
  'listeners.conflict':
    'Другой администратор изменил настройки. Отмените действие и перечитайте их.',
  'listeners.migrationRequired':
    'Требуется явная миграция таблицы настроек в базе управления. Обратитесь к администратору.',
  'listeners.invalid':
    'Недопустимые настройки. Проверьте адреса, порты и таймауты.',
  'listeners.portConflict':
    'Адреса или порты включённых слушателей конфликтуют. Исправьте их.',
  'listeners.state.disabled': 'Выключен',
  'listeners.state.starting': 'Запускается',
  'listeners.state.listening': 'Прослушивание',
  'listeners.state.failed': 'Ошибка запуска или приёма',
  'listeners.state.stopped': 'Остановлен',
  'listeners.state.unknown': 'Неизвестно',
  'listeners.confirmEnable': 'Включить приём журналов?',
  'listeners.confirmDisable': 'Отключить приём журналов?',
  'listeners.confirmSwitch': 'Подтвердить',
  'listeners.enableHint':
    'Будут загружены последние сохранённые настройки и запущен приём на портах включённых протоколов.',
  'listeners.disableHint':
    'Порты приёма журналов и существующие соединения будут закрыты.',
  'listeners.switchFailed':
    'Не удалось подтвердить состояние приёма журналов. Обновите настройки.',
  'listeners.protocolEnable': 'Включить протокол?',
  'listeners.protocolDisable': 'Выключить протокол?',
  'listeners.protocolEnableHint':
    'Слушатель запустится с последними сохранёнными параметрами.',
  'listeners.protocolDisableHint':
    'Слушатель остановится и закроет свои текущие соединения.',
  'listeners.protocolSavedHint':
    'Общий приём выключен. Будет сохранено только состояние этого протокола.',
  'listeners.streamInterrupted':
    'Соединение для обновлений прервано. Показано последнее полученное состояние.',
};
