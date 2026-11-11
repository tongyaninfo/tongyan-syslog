import type source from '../zh-CN/unregistered';

export default {
  'menu.unregistered': '미등록 장치',
  'unregistered.title': '미등록 장치',
  'unregistered.description':
    '소스 설정이 없는 발신 IP와 최근 로그 샘플을 확인하고 한 번의 클릭으로 소스를 등록합니다.',
  'unregistered.boundary':
    'IP당 최근 샘플을 최대 100개까지 메모리에 임시 보관합니다. 재시작하면 초기화되며 메모리 또는 장치 수 한도에 도달하면 가장 오래된 샘플부터 제거합니다.',
  'unregistered.defaults':
    '빠른 추가는 IP를 이름으로 사용하고 시간대 Asia/Shanghai, 보관 기간 365일, 수신 사용으로 등록합니다. 이후 로그 소스에서 변경할 수 있습니다.',
  'unregistered.firstSeen': '최초 수신',
  'unregistered.lastSeen': '최근 수신',
  'unregistered.received': '샘플 수신 누계',
  'unregistered.samples': '현재 샘플',
  'unregistered.viewLogs': '로그 보기',
  'unregistered.add': '빠른 추가',
  'unregistered.added': '소스 {ip} 등록 완료',
  'unregistered.addedHint':
    '소스 정보 갱신 후 정식 수신이 시작됩니다(기본 10초 간격). 임시 이력은 정식 로그로 복사되지 않습니다.',
  'unregistered.addFailed': '소스를 추가하지 못했습니다',
  'unregistered.loadFailed': '미등록 장치를 불러오지 못했습니다',
  'unregistered.state': '임시 보관 설정',
  'unregistered.enabled': '사용',
  'unregistered.disabled': '사용 안 함',
  'unregistered.devices': '장치 / 한도',
  'unregistered.memory': '메모리 / 한도',
  'unregistered.currentSamples': '현재 샘플 합계',
  'unregistered.evicted': '제거된 샘플 누계',
  'unregistered.rejected': '거부 누계',
  'unregistered.networks': '허용 네트워크',
  'unregistered.noNetworks': '허용 네트워크 미설정',
  'unregistered.counterHint':
    '이 수치는 임시 샘플 상태이며 데이터베이스 기록 수가 아닙니다. 누계는 프로세스 시작부터 계산합니다. 등록 후 사용 중지한 소스는 제외됩니다. 임시 보관 사용은 리스너 사용을 의미하지 않습니다.',
  'unregistered.search': '발신 IP 검색',
  'unregistered.count': '검색된 장치 {count}개',
  'unregistered.unavailable': '샘플 데이터를 사용할 수 없습니다',
  'unregistered.empty': '미등록 장치가 없습니다',
  'unregistered.sampleExpired':
    '샘플이 제거되었거나 장치가 등록되었습니다. 로그 목록을 새로 고치세요.',
  'unregistered.byteCount': '원문 크기',
  'unregistered.preview': '원문 요약',
  'unregistered.viewRaw': '원문 보기',
  'unregistered.logsTitle': '{ip} · 임시 로그',
  'unregistered.logsHint':
    '최근 샘플을 최대 100개까지 수신 시각 내림차순으로 표시합니다. 연결 확인용이며 정식 로그 데이터베이스에 기록되지 않았습니다.',
  'unregistered.previewHint':
    '목록은 길이가 제한된 원문 요약입니다. 원문에서 바이트 증거, 인코딩 및 잘림 안내를 확인할 수 있습니다.',
  'unregistered.logsFailed': '임시 로그를 불러오지 못했습니다',
  'unregistered.noLogs': '샘플이 없습니다. 새로 고쳐 확인하세요.',
  'unregistered.helpStep1':
    '미등록 장치를 열면 허용 네트워크 내의 미등록 발신 IP를 볼 수 있습니다. 등록 후 사용 중지한 소스는 다시 발견되지 않습니다.',
  'unregistered.helpStep2':
    'IP당 최근 샘플을 최대 100개까지 메모리에 보관합니다. 기본 전체 한도는 64 MiB와 IP 1000개입니다. 한도에 도달하면 가장 오래된 샘플을 제거하며 재시작하면 초기화됩니다. 로그에서 개별 원문을 확인하세요.',
  'unregistered.helpStep3':
    '빠른 추가는 IP 이름, Asia/Shanghai 시간대, 365일 보관, 수신 사용으로 소스를 생성합니다. 소스 정보 갱신 후(기본 10초) 정식 수신이 시작됩니다. 임시 이력은 복사되지 않습니다. 이후 로그 소스에서 설정을 변경할 수 있습니다.',
} satisfies Record<keyof typeof source, string>;
