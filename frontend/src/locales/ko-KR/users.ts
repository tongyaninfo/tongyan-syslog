import type source from '../zh-CN/users';

export default {
  'menu.users': '사용자 관리',
  'users.title': '사용자 관리',
  'users.description': '관리 콘솔 계정, 활성화 상태 및 비밀번호를 설정합니다.',
  'users.permissionHint':
    '현재 모든 계정은 관리자이며 전체 로그를 조회하고 시스템 설정을 변경할 수 있습니다.',
  'users.search': '사용자 이름 검색',
  'users.filter': '계정 상태',
  'users.all': '모든 상태',
  'users.enabled': '활성',
  'users.disabled': '비활성',
  'users.username': '사용자 이름',
  'users.role': '역할',
  'users.admin': '관리자',
  'users.createdAt': '생성 시각',
  'users.actions': '작업',
  'users.current': '현재 계정',
  'users.add': '사용자 추가',
  'users.edit': '사용자 편집',
  'users.enable': '활성화',
  'users.disable': '비활성화',
  'users.resetPassword': '비밀번호 재설정',
  'users.refresh': '새로 고침',
  'users.total': '계정 {total}개',
  'users.empty': '조건에 맞는 계정이 없습니다',
  'users.unavailable': '계정 목록을 일시적으로 사용할 수 없습니다',
  'users.loadFailed': '계정을 불러오지 못했습니다',
  'users.stale': '이전 결과를 표시하고 있습니다. 새로 고친 후 작업하세요.',
  'users.usernameRule':
    '영문, 숫자, 마침표, 하이픈, 밑줄을 3~64자로 입력하세요.',
  'users.passwordRule':
    '비밀번호는 8~1024자이며 공백으로만 구성할 수 없습니다.',
  'users.confirmPassword': '비밀번호 확인',
  'users.passwordMismatch': '입력한 비밀번호가 일치하지 않습니다.',
  'users.saveFailed': '저장하지 못했습니다. 안내를 확인한 후 다시 시도하세요.',
  'users.created': '사용자를 생성했습니다.',
  'users.updated': '계정 설정을 저장했습니다.',
  'users.passwordReset':
    '비밀번호를 재설정했습니다. 해당 계정은 새 비밀번호로 다시 로그인해야 합니다.',
  'users.selfChanged': '계정 정보가 변경되었습니다. 다시 로그인하세요.',
  'users.editHint': '사용자 이름을 변경하면 이 계정의 모든 세션이 종료됩니다.',
  'users.selfHint':
    '현재 계정은 비활성화할 수 없습니다. 본인의 비밀번호는 오른쪽 상단 계정 메뉴에서 변경하세요.',
  'users.disableTitle': '계정 비활성화 확인',
  'users.disableHint':
    '{username} 계정을 비활성화하면 로그인할 수 없으며 모든 기존 세션이 즉시 종료됩니다.',
  'users.resetTitle': '비밀번호 재설정 확인',
  'users.resetHint':
    '{username} 계정의 비밀번호를 재설정하면 해당 계정의 모든 세션이 종료됩니다.',
  'users.confirm': '확인',
  'users.back': '편집으로 돌아가기',
  'users.previewHint':
    '이 합성 미리보기는 계정 작업만 시연하며 실제 비밀번호를 저장하지 않습니다. 예제 계정은 preview-only로 로그인합니다.',
  'setup.title': '관리자 초기 설정',
  'setup.description':
    'Tongyan Syslog에 오신 것을 환영합니다. 관리자 계정을 생성하여 로그 센터 설정을 시작하세요.',
  'setup.cardTitle': '첫 관리자 생성',
  'setup.cardHint':
    '아직 계정이 설정되지 않았습니다. 사용자 이름과 비밀번호를 설정한 후 로그인 페이지로 이동합니다.',
  'setup.submit': '관리자 생성 후 계속',
  'setup.restricted': '서버에서 직접 초기 설정을 완료하세요',
  'setup.restrictedHint':
    '첫 관리자는 서버의 로컬 HTTPS 접속으로만 생성할 수 있습니다. 원격 설치에는 통제된 터널을 사용할 수 있습니다. 설정 후 일반적인 원격 로그인이 가능합니다.',
  'setup.unavailable': '초기 설정 상태를 확인할 수 없습니다',
  'setup.unavailableHint':
    '시스템 상태가 아직 확인되지 않았습니다. 다시 시도하거나 서비스 설정을 확인하세요. 이 단계에서는 계정이나 저장소를 변경하지 않습니다.',
  'setup.completed':
    '관리자를 생성했습니다. 방금 설정한 계정으로 로그인하세요.',
  'setup.previewCompleted':
    '초기 설정 미리보기를 완료했습니다. 설정한 예제 사용자 이름과 preview-only로 로그인하세요.',
  'setup.previewHint':
    '합성 초기 설정 데모입니다. 예제 사용자 이름만 입력하세요. 입력한 비밀번호는 전송하거나 저장하지 않습니다. 설정 후 예제 사용자 이름과 preview-only로 로그인하세요.',
  'setup.loginHint':
    '처음 사용할 때 관리자 생성을 안내합니다. 이후 계정은 관리자가 설정합니다.',
  'error.usernameConflict':
    '이미 사용 중인 사용자 이름입니다. 다른 이름을 사용하세요.',
  'error.userVersion':
    '다른 작업으로 계정이 변경되었습니다. 목록을 새로 고친 후 다시 편집하세요.',
  'error.lastAdmin': '활성 관리자 계정을 최소 하나 유지해야 합니다.',
  'error.selfDisable': '현재 로그인한 계정은 비활성화할 수 없습니다.',
  'error.selfReset': '본인의 비밀번호는 오른쪽 상단 계정 메뉴에서 변경하세요.',
  'error.setupCompleted':
    '관리자 설정이 이미 완료되었습니다. 로그인 페이지로 돌아가세요.',
  'error.setupLocal': '서버의 안전한 로컬 접속을 통해 초기 설정을 완료하세요.',
} satisfies Record<keyof typeof source, string>;
