import type source from '../zh-CN/liveLogs';

export default {
  'live.cacheId': '임시 캐시 ID',
  'menu.liveLogs': '실시간 로그',
  'live.title': '실시간 로그',
  'live.subtitle':
    '무시된 로그를 포함한 최근 수신 로그를 확인합니다. 메모리 캐시는 재시작 시 초기화됩니다.',
  'live.settings': '캐시 설정',
  'live.settingsHint':
    '저장 즉시 적용됩니다. 기본값은 장치당 5000개, 총 512 MiB입니다. 한도 도달 시 오래된 로그를 제거합니다.',
  'live.perDeviceLimit': '장치당 캐시 한도',
  'live.memoryLimit': '총 메모리 한도',
  'live.limitValidation': '100~50000 사이의 정수를 입력하세요.',
  'live.memoryValidation': '16~2048 MiB를 입력하세요.',
  'live.shrinkHint':
    '한도를 줄이면 오래된 캐시가 즉시 제거되며 복구할 수 없습니다.',
  'live.entries': '현재 캐시 수',
  'live.devices': '장치 수',
  'live.memory': '캐시 메모리',
  'live.budget': '총 한도',
  'live.cacheHint': '등록된 소스별로 별도 보관',
  'live.evicted': '누적 제거',
  'live.rejected': '캐시 거부',
  'live.disposition': '처리 방식',
  'live.disposition.all': '전체',
  'live.disposition.normal': '정상 수신',
  'live.disposition.ignored': '무시됨 · DB 저장 안 함',
  'live.time.live': '전체 캐시 시간 · 실시간',
  'live.time.fixed': '고정 시간 범위',
  'live.apply': '필터 적용',
  'live.dirty': '조건이 변경되었습니다. 적용을 누르세요.',
  'live.stream': '실시간 스트림',
  'live.connection.connecting': '연결 중',
  'live.connection.connected': '실시간 연결 정상',
  'live.connection.reconnecting': '다시 연결 중',
  'live.connection.disconnected': '연결 끊김',
  'live.connection.paused': '일시 정지',
  'live.resume': '실시간 재개',
  'live.pause': '일시 정지',
  'live.refresh': '다시 읽기',
  'live.clear': '현재 화면 지우기',
  'live.evidenceHint':
    '실시간 기록은 DB 쓰기 완료를 의미하지 않습니다. 무시된 로그는 DB에 저장하지 않습니다. 화면 지우기는 이 브라우저에만 적용됩니다. 아래로 스크롤하면 읽기 위치 유지를 위해 자동 정지합니다.',
  'live.pausedHint':
    '이 브라우저 구독과 화면 갱신이 정지되었습니다. 서버 캐시는 계속되며 재개 시 누락분을 요청합니다.',
  'live.gap':
    '캐시가 초기화되었거나 일부 기록이 제거되어 최근 로그를 다시 읽었습니다. 누락된 기록은 실시간 캐시에서 복구할 수 없습니다.',
  'live.budgetLimited':
    '현재 읽기가 개수 또는 용량 한도에 도달했습니다. 이전 기록을 읽거나 다음 업데이트를 기다리세요.',
  'live.spoolFull':
    '디스크 임시 저장 공간이 가득 차 로그가 삭제될 수 있습니다.',
  'live.empty': '일치하는 캐시 로그가 없습니다. 새 로그를 기다립니다.',
  'live.displayed': '표시된 기록',
  'live.heartbeat': '최근 연결 하트비트',
  'live.queued': '처리 대기',
  'live.dropped': '누적 손실',
  'live.loadOlder': '이전 기록 500개 읽기',
  'live.limitHint':
    '처음에는 최근 500개를 읽습니다. 화면은 최대 5000개 요약을 유지하며 새 기록이 오면 가장 오래된 기록을 제거합니다. 상세 정보는 메모리에서 요청 시 읽습니다.',
  'live.truncated': '요약 잘림',
  'live.normalHint':
    '정상 수신: 메모리 캐시 기록이며 ClickHouse 쓰기 완료를 의미하지 않습니다.',
  'live.ignoredHint':
    '소스 심각도 규칙에 따라 무시되었습니다. 실시간 메모리 샘플만 유지하며 DB에는 기록하지 않습니다.',
  'live.error.migration':
    '실시간 캐시 설정을 사용하려면 제어 DB를 명시적으로 업그레이드하세요.',
  'live.error.cacheReset':
    '캐시가 초기화되었습니다. 최근 로그를 다시 읽으세요.',
  'live.error.unavailable':
    '실시간 캐시를 사용할 수 없습니다. 서비스 설정 확인 후 다시 시도하세요.',
  'live.error.busy': '실시간 조회가 혼잡합니다. 잠시 후 다시 시도하세요.',
  'live.error.evicted':
    '기록이 제거되었거나 캐시가 재시작되었습니다. 목록을 다시 읽으세요.',
  'live.error.conflict':
    '다른 작업이 설정을 변경했습니다. 설정을 닫았다가 다시 여세요. 입력은 유지됩니다.',
} satisfies Record<keyof typeof source, string>;
