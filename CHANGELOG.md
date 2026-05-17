# Changelog
모든 주요 변경 사항을 이 파일에 기록합니다.

## [26.5.17] - 2026-05-17
### Added
- **VContainer (DI) 시스템 도입:** 전역 싱글톤(`Instance`) 패턴을 걷어내고 `RootLifetimeScope` 및 `TestLifetimeScope`를 통한 의존성 주입(Dependency Injection) 체계 구축. 아키텍처 결합도 대폭 완화.
- **ZLogger 기반 Zero-GC 로깅 적용:** 기존 `Debug.Log`를 제거하고, 메모리 가비지(GC) 할당이 없는 ZLogger를 코어 시스템 전체에 도입.
- **R3 기반 반응형 스트림 구축:** `ArduinoManager`에 R3(`ObserveOnMainThread`)를 적용하여, 백그라운드 스레드와 유니티 메인 스레드 간의 데이터 전달 시 발생하는 스레드 충돌을 안전하게 방지.

### Changed
- **전면적인 비동기(UniTask) 마이그레이션:** 프레임 드랍의 주범이던 `Coroutine`을 제거하고, `JsonLoader`, `FadeManager`, `UIManager`, `SoundManager`, `VideoManager` 등 파일 I/O 및 연출 로직을 모두 `UniTask`로 전환.
- **`GameCloser` 데이터 주도(Data-Driven) 설계:** 인스펙터 리플렉션(UnityEvent)을 제거하고 Awake 시점에 코드로 이벤트를 직접 바인딩하여 속도 최적화 및 좀비 이벤트 방지.
  - 정규화 좌표(Normalized Coordinates) 기반으로 `Settings.json`을 읽어들여 런타임에 위치, 클릭 횟수, 타임 윈도우, 투명도를 동기화하도록 수정.
- **`SystemCanvas` 캡슐화 및 독립:** 외부 접근을 차단하고 스스로 생명주기(DontDestroyOnLoad)와 최상단 렌더링(Sorting Order 30000)을 관리하도록 변경.
- **입력 시스템(Input System) 충돌 방지:** `GameManagerBase`의 입력을 New Input System으로 통일하고, 씬(Scene)의 구형 EventSystem 모듈과 충돌하지 않도록 처리.
- **리소스 메모리 및 VRAM 누수 방지:**
    - `SoundManager`: 캐시 용량 제한(MAX_CACHE_COUNT) 적용.
    - `VideoManager`: `RenderTexture` 동적 생성 후 파괴 시 완벽한 `Release` 처리.
    - `UIManager`: 비동기 다운로드 이미지(`Sprite`) 및 폰트 `Addressables` 핸들 완전 해제 로직 강화.

### Fixed
- **ZLogger 컴파일 에러 해결:** 보간 문자열 핸들러(`ref struct`)와 Null 조건부 연산자(`?.`) 간의 C# 문법 충돌로 인한 컴파일 에러를 명시적 널 체크(`if (obj != null)`)로 일괄 수정.
- **UI 클릭 이벤트 무시 현상 수정:** 캔버스 동적 생성 시 `GraphicRaycaster` 컴포넌트가 누락되어 클릭(터치)이 씹히는 현상 원천 차단 (`SystemCanvas` 로직 보완).

## [26.4.30] - 2026-05-13
### Changed
- 메서드 복잡도를 개선하여 향후 유지보수를 향상 시킴.
- GetComponent를 TryGetComponent로 변경하여 성능 오버헤드를 방지함.

## [26.4.30] - 2026-04-30
### Changed
- `GameManager`를 `GameManagerBase<T>` 기반의 제네릭 싱글톤 상속 구조로 리팩토링하여 인스턴스 관리 일관성 확보 및 중복 코드 제거

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