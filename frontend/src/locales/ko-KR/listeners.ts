export default {
  'listeners.legacyConfiguration':
    '서버의 기존 설정에 동일한 프로토콜의 리스너가 여러 개 있습니다. 서버에서 프로토콜당 하나의 리스너로 정리한 후 서비스를 재시작하세요. 현재 실행 설정은 변경되지 않았습니다.',
  'listeners.failure.io':
    '바인딩 또는 네트워크 입출력에 실패했습니다. 포트 충돌, 주소 및 권한을 확인하세요.',
  'listeners.failure.connectionIo':
    '최근 클라이언트 연결에서 읽기 또는 쓰기 오류가 발생했습니다. 포트는 새 연결을 계속 수신합니다.',
  'listeners.failure.tls':
    'TLS 시작에 실패했습니다. 서버 인증서, 개인 키 및 유효 기간을 확인하세요.',
  'listeners.failure.sources':
    '로그 원본 설정을 사용할 수 없어 수신이 일시 중지되었습니다.',
  'listeners.failure.configuration':
    '시작 설정을 불러오지 못했습니다. 제어 DB와 설정 버전을 확인하세요.',
  'listeners.failure.forcedDisabled':
    '서버에서 수신을 강제로 비활성화했습니다. 시작 설정을 확인하세요.',
  'menu.listeners': '수신 설정',
  'listeners.title': '수신 설정',
  'listeners.description': 'UDP, TCP, TLS, RELP 로그 수신을 구성합니다.',
  'listeners.reload': '설정 다시 읽기',
  'listeners.restartHint':
    '수신 설정을 저장한 후 해당 프로토콜이나 전체 수신 스위치를 껐다 켜면 적용됩니다.',
  'listeners.boundary':
    '전체 스위치를 끄면 모든 리스너와 기존 연결이 중지됩니다. 꺼진 상태에서도 프로토콜별 활성화 상태를 저장할 수 있습니다.',
  'listeners.loadFailed': '수신 설정을 읽지 못했습니다',
  'listeners.saved': '설정이 저장되었습니다',
  'listeners.running': '현재 실행 설정',
  'listeners.pending': '설정 적용 대기',
  'listeners.current': '설정 로드됨',
  'listeners.master': '로그 수신',
  'listeners.masterHint':
    '전체 스위치가 켜진 상태에서 프로토콜을 전환하면 해당 프로토콜만 영향을 받습니다.',
  'listeners.protocol': '프로토콜',
  'listeners.address': '수신 주소',
  'listeners.port': '포트',
  'listeners.state': '리스너 상태',
  'listeners.failure': '문제',
  'listeners.listenerFailed':
    '수신 오류입니다. 수신 주소, 포트 및 인증서 설정을 확인하세요.',
  'listeners.savedConfiguration': '저장된 수신 설정',
  'listeners.enabled': '프로토콜 활성화',
  'listeners.addressRequired': 'IPv4 또는 IPv6 수신 주소를 입력하세요.',
  'listeners.portRequired': '포트는 1~65535의 정수여야 합니다.',
  'listeners.framing': '메시지 프레이밍',
  'listeners.connectionErrors': '클라이언트 연결 오류',
  'listeners.clientAddress': '클라이언트 IP',
  'listeners.errorOccurredAt': '발생 시간',
  'listeners.auto': '자동 감지 (옥텟 카운팅 / 줄 바꿈)',
  'listeners.octet': '옥텟 카운팅',
  'listeners.newline': '줄 바꿈 (LF / CRLF)',
  'listeners.certificate': 'TLS 인증서',
  'listeners.certificateRequired':
    'TLS를 활성화하려면 사용 가능한 서버 인증서를 선택하세요.',
  'listeners.noCertificates':
    '서버 인증서가 구성되지 않았습니다. 서버에서 인증서를 구성한 후 서비스를 재시작하세요. 이 페이지는 개인 키나 비밀번호를 수집하지 않습니다.',
  'listeners.frameTimeout': '프레임 시간 제한(초)',
  'listeners.idleTimeout': '유휴 시간 제한(초)',
  'listeners.timeoutRequired': '허용 범위 내의 정수 초를 입력하세요.',
  'listeners.relpHint':
    'RELP 확인은 수신 측의 처리 수락을 의미하며 로그 DB 저장 완료를 뜻하지 않습니다. 미등록 장치는 최근 100개 메모리 샘플만 보관하며 재시작 시 지워집니다.',
  'listeners.save': '수신 설정 저장',
  'listeners.confirmTitle': '수신 설정 저장 확인',
  'listeners.confirmSave': '저장 확인',
  'listeners.confirmHint':
    '장치의 전송 프로토콜, 대상 주소 및 포트를 확인하세요. 저장한 후 해당 프로토콜이나 전체 스위치를 껐다 켜면 적용됩니다.',
  'listeners.saveFailed': '저장 결과를 확인하지 못했습니다',
  'listeners.conflict':
    '다른 관리자가 설정을 변경했습니다. 취소 후 설정을 다시 읽고 수정하세요.',
  'listeners.migrationRequired':
    '제어 DB에 수신 설정 마이그레이션이 필요합니다. 관리자에게 마이그레이션을 요청한 후 다시 시도하세요.',
  'listeners.invalid':
    '수신 설정이 잘못되었습니다. 주소, 포트 및 시간 제한 범위를 확인하세요.',
  'listeners.portConflict':
    '활성 리스너의 주소 또는 포트가 충돌합니다. 수정 후 저장하세요.',
  'listeners.state.disabled': '비활성화',
  'listeners.state.starting': '시작 중',
  'listeners.state.listening': '수신 중',
  'listeners.state.failed': '시작 또는 수신 실패',
  'listeners.state.stopped': '중지됨',
  'listeners.state.unknown': '알 수 없음',
  'listeners.confirmEnable': '로그 수신을 켤까요?',
  'listeners.confirmDisable': '로그 수신을 끌까요?',
  'listeners.confirmSwitch': '전환 확인',
  'listeners.enableHint':
    '최근에 저장한 설정을 불러와 활성화된 프로토콜 포트에서 수신을 시작합니다.',
  'listeners.disableHint': '로그 수신 포트를 닫고 기존 수신 연결을 종료합니다.',
  'listeners.switchFailed':
    '로그 수신 상태를 확인하지 못했습니다. 설정을 다시 불러오세요.',
  'listeners.protocolEnable': '프로토콜을 활성화할까요?',
  'listeners.protocolDisable': '프로토콜을 비활성화할까요?',
  'listeners.protocolEnableHint':
    '마지막으로 저장된 설정으로 해당 프로토콜의 수신을 시작합니다.',
  'listeners.protocolDisableHint':
    '해당 프로토콜의 수신을 중지하고 기존 연결을 끊습니다.',
  'listeners.protocolSavedHint':
    '전체 스위치가 꺼져 있습니다. 해당 프로토콜의 활성화 상태만 저장합니다.',
  'listeners.streamInterrupted':
    '실시간 연결이 끊겨 마지막으로 받은 상태를 표시합니다.',
};
