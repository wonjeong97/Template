# Changelog
모든 주요 변경 사항을 이 파일에 기록합니다.

## [26.4.29] - 2026-04-29
### Added
- 런타임 디버깅 및 UI 트랜스폼 조작을 위한 Runtime Inspector & Hierarchy 에셋 템플릿 내장

### Changed
- `GameManagerBase`에 'I' 키를 통한 런타임 인스펙터 토글 기능 추가 및 창 닫힘 감지용 `OnInspectorClosed` 이벤트 브로드캐스트 구현 (컨트롤러 자동 저장 결합도 완화)

## [26.4.21] - 2026-04-21
### Changed
- `GameManager`를 타 프로젝트에서 안전하게 상속받아 확장할 수 있도록 `GameManagerBase` 기반 클래스로 구조 변경
- `ArduinoManager`의 통신 로직을 정비하고, 유연한 재사용을 위해 이벤트 기반 설계(Observer Pattern)로 의존성 완전 분리

## [26.4.17] - 2026-04-17
### Fixed
- 로그 데이터가 100개 이상 누적될 시 `Reporter` UI에서 스크롤바 썸(Thumb)이 0픽셀로 수렴하여 사라지는 렌더링 버그 수정

## [26.3.23] - 2026-03-23
### Fixed
- 씬 전환 시 `TimestampLogHandler`가 이중으로 래핑(Decorator Leak)되어 타임스탬프가 중복 출력되는 버그 수정

## [26.3.12] - 2026-03-12
### Added
- 런타임 로그 캡처 및 로컬 파일 덤프 시 각 로그 메시지에 타임스탬프를 자동 기록하는 기능 추가