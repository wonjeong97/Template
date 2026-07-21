# Changelog
모든 주요 변경 사항을 이 파일에 기록합니다.

## [26.7.21] - 2026-07-21

> **⚠️ Breaking:** `Settings.json`의 `fontMap` 스키마가 `fonts` 배열로 변경되었습니다. 기존 프로젝트는 아래 "마이그레이션" 항목을 참고하여 JSON을 수정해야 폰트가 적용됩니다.

### Added
- **패키지 메타데이터 정비:** `package.json`에 `dependencies`(`com.unity.addressables`, `com.unity.inputsystem`), `license`, `keywords`, 상세 `description`을 추가. 기존에는 의존성 선언이 전혀 없어 새 프로젝트에 설치 시 컴파일 에러가 발생했음.
- **`README.md` 추가:** 설치 절차(OpenUPM 스코프 레지스트리 → 서드파티 5종 → 본 패키지), 구조 개요, `Settings.json` 스키마 예시, WebGL 제약 표를 문서화. VContainer/UniTask/ZLogger/R3/MessagePipe는 스코프 레지스트리 없이는 해석에 실패하므로 `dependencies`에 넣지 않고 수동 설치 절차로 분리함.
- **`LICENSE.md` 추가:** MIT 라이선스 및 내장 서드파티(RuntimeInspector, Reporter) 고지.
- **`AppSettingsProvider` 추가:** `Settings.json` 로드를 일원화하는 싱글톤 제공자. `RootLifetimeScope`에 등록되며 `UniTask.Preserve()`로 결과를 공유하고, 호출자별 취소 토큰은 `AttachExternalCancellation`으로 격리하여 한 소비자의 취소가 다른 소비자에게 전파되지 않도록 함.
- **`UIManager.ClearSpriteCache()` 공개 API 추가:** 스프라이트 캐시를 명시적으로 해제하는 수단. 자동 축출(LRU)은 의도적으로 적용하지 않음 — 화면에 표시 중인 `Image`가 참조하는 스프라이트를 임의 파괴하면 해당 UI가 렌더링되지 않는 더 심각한 문제가 발생하기 때문임.

### Changed
- **`Settings.json` 4중 로드 제거:** `GameManagerBase`, `UIManager`, `SoundManager`, `GameCloser`가 각각 독립적으로 동일 파일을 읽던 구조를 `AppSettingsProvider` 주입으로 통합. WebGL에서 HTTP 요청 4회가 1회로 줄고, 로드 완료 시점이 달라 발생하던 초기화 순서 비결정성이 해소됨.
- **설정 로드 시점을 `Awake` → `Start`로 이전:** VContainer의 의존성 주입은 `Awake` 이후에 완료되므로, 주입받은 `AppSettingsProvider`를 `Awake`에서 사용하면 널 참조가 발생함. `UIManager`/`SoundManager`의 로드 호출을 `Start`로 옮기고, 이 과정에서 `UIManager`의 `_fontsLoadedStarted` 이중 가드가 불필요해져 제거됨.
- **`FontMaps` → `FontSetting[]` 스키마 전환:** `font1`~`font9` 고정 필드 9개 + 9-way switch 구조를 배열 기반으로 변경. 폰트 추가 시 3곳(데이터 클래스·로드 호출·키 검증)을 수정해야 하던 것이 JSON 수정만으로 가능해짐. 키 이름도 `font1` 같은 고정값이 아닌 자유 명명(`title`, `body` 등)이 가능함. 기존 `SoundSetting[]`과 패턴이 통일됨.
- **`Reporter`(LogViewer) 위치 이동:** `Runtime/LogViewer/` → `Runtime/ThirdParty/LogViewer/`. RuntimeInspector와 벤더 코드 배치를 통일함. `.meta`를 함께 이동하여 GUID가 유지되므로 프리팹/씬 참조는 영향 없음.

### Fixed
- **`GameManagerBase`의 정적 플래그 미복구 수정:** `_isInstantiated`가 `OnDestroy`에서 해제되지 않아, Enter Play Mode Options에서 Domain Reload를 비활성화한 경우 재생 2회차부터 `GameManager`가 자기 자신을 파괴하던 문제. `SystemCanvas`에 이미 적용되어 있던 `_isOriginal` 패턴을 동일하게 이식함.
- **`JsonLoader`의 널 폴백이 실제로 동작하지 않던 문제 수정:** `where T : new()` 제약과 "Null 발생 방지" 주석에도 불구하고 `JsonUtility.FromJson<T>`의 반환값이 그대로 전달되어, JSON 내용이 비어 있거나 `"null"`일 때 널이 반환될 수 있었음. 두 로드 경로 모두에 `?? new T()` 폴백을 적용함.
- **`GameCloser`의 `_logger?.` 잔재 수정:** 26.5.17에서 ZLogger의 보간 문자열 핸들러(`ref struct`)와 널 조건부 연산자 충돌 때문에 명시적 널 체크로 일괄 변경했으나 1곳이 누락되어 있었음.
- **`SoundManager`의 재생 중 오디오 파괴 문제 수정:** 캐시가 `MAX_CACHE_COUNT`(20)를 넘으면 `Destroy(oldClip)`을 호출했는데, 해당 클립이 `AudioSource.clip`으로 재생 중인지 확인하지 않아 **BGM이 재생 도중 무음으로 끊길 수 있었음**(에러 로그 없이 발생). BGM은 보통 가장 먼저 캐시에 등록되어 축출 1순위가 되므로 실제로 걸리기 쉬운 구조였음. 또한 `Dictionary.Keys.First()`는 열거 순서를 보장하지 않아(`Remove` 후 빈 슬롯 재사용) "가장 오래된 항목 삭제"라는 주석과 달리 사실상 임의 항목을 축출하고 있었음.
  - `_clipCache`에 담기는 키는 `Settings.json`의 `sounds[]` 목록으로 이미 상한이 정해져 있어 무한 증가하지 않으므로, 자동 축출 로직(`ManageCacheCapacity`, `MAX_CACHE_COUNT`)을 제거하고 기존 `ClearCache()` 공개 API로 해제 시점을 호출자가 결정하도록 변경. `UIManager.ClearSpriteCache()`와 동일한 방침.

### Migration
`StreamingAssets/Settings.json`의 폰트 설정을 아래와 같이 변경해야 합니다.

```jsonc
// 변경 전
"fontMap": { "font1": "Fonts/Bold", "font2": "Fonts/Regular" }

// 변경 후 — key는 자유롭게 명명 가능
"fonts": [
  { "key": "font1", "address": "Fonts/Bold" },
  { "key": "font2", "address": "Fonts/Regular" }
]
```
`TextSetting.fontName`이 참조하는 값은 `fonts[].key`입니다. 기존 키 이름(`font1` 등)을 그대로 두면 씬/JSON의 다른 부분은 수정할 필요가 없습니다.

### Removed
- **`LogSaver` 제거:** 더 이상 사용하지 않는 기능이므로 삭제. 로그 파일 저장은 `RootLifetimeScope`의 ZLogger 파일 출력(`AddZLoggerFile`)이 대체하며, SMTP 메일 발송 기능은 폐기됨. 자격증명(`senderEmail`/`senderPassword`)을 `[SerializeField]`로 보관하던 구조가 함께 제거되어 씬/프리팹 직렬화 및 빌드 산출물에 평문 노출되던 위험도 해소됨.
- **`CoroutineData` 제거:** 26.5.17의 UniTask 전면 마이그레이션으로 코루틴이 사라지면서 `WaitForSeconds` 캐시가 불필요해짐. 참조처 없음.
- **`TimestampLogHandler` 제거:** ZLogger의 `SetPrefixFormatter` 기반 타임스탬프 출력과 역할이 중복되며 `Attach()` 호출처가 없어 삭제.
- **`JsonLoader.Load<T>` (동기 버전) 제거:** WebGL에서 원리상 동작 불가하여 에러 로그만 반환하던 데드 코드. 모든 호출부가 이미 `LoadAsync`를 사용 중이므로 API 표면에서 제외.

## [26.7.6] - 2026-07-06
### Added
- **WebGL 플랫폼 지원:** 목표 플랫폼(Windows)의 기존 동작은 유지하면서, 기획 검수용 WebGL 빌드(Play.unity.com 업로드)가 가능하도록 템플릿 전체를 Windows/WebGL 동시 호환 구조로 전환.
- **`ArduinoManager` WebGL 스텁 구현:** WebGL에서 빌드 자체가 불가능한 `System.IO.Ports`(SerialPort)와 `Thread` 사용 코드를 전처리기로 분리하고, 동일한 공개 API를 제공하는 스텁 클래스를 추가. 호출부 코드 수정 없이 컴파일되며, 연결 시도 시 경고 로그만 출력함.

### Changed
- **`JsonLoader` 플랫폼 분기 로드:** StreamingAssets 경로가 URL인 플랫폼(WebGL, Android)에서는 `UnityWebRequest`, 로컬 경로에서는 기존 `File` I/O로 JSON을 로드하도록 분기 처리. 동기 `Load`는 WebGL에서 원리상 불가능하므로 에러 로그 후 기본값을 반환하며, `SaveAsync`는 StreamingAssets가 읽기 전용인 플랫폼에서 저장 불가 가드 추가.
- **`UIManager` / `SoundManager` 비동기 설정 로드 전환:** 동기 `JsonLoader.Load` 호출을 `LoadAsync` 기반으로 전환. `UIManager`는 설정 로드 완료 시점에 폰트 프리로드를 시작하도록 변경.
- **`UIManager` 이미지 로드 WebGL 대응:** Sprite 로드 시 URL 경로면 `UnityWebRequest`, 로컬 경로면 `File` I/O를 사용하도록 `ReadSpriteBytesAsync`로 분리.
- **`RootLifetimeScope` 로깅 분기:** ZLogger 파일 로깅(`AddZLoggerFile`)을 WebGL에서 제외하고 Unity 콘솔 로그만 사용하도록 변경 (브라우저 개발자 도구에서 확인 가능).
- **`LogSaver` WebGL 비활성화:** 로컬 파일 저장 및 SMTP 메일 발송(소켓/스레드 기반)이 WebGL에서 불가능하므로 해당 기능을 전처리기로 비활성화.
- **`GameCloser` WebGL 종료 처리:** 브라우저에서 `Application.Quit()`이 동작하지 않으므로 WebGL 분기에서는 안내 로그만 출력.

### Fixed
- **`SoundManager` 오디오 URI 생성 오류 수정:** 오디오 로드 시 무조건 `file://` 접두어를 붙여 WebGL(이미 `https://` URL)에서 경로가 깨지던 문제를 로컬 경로일 때만 접두어를 붙이도록 수정.
- **`LogSaver` WebGL 메모리 누적 방지:** WebGL에서는 로그를 저장하거나 발송할 수 없어 버퍼가 소비될 곳이 없음에도 `HandleLog`가 계속 등록되어 `_logBuffer`가 세션 내내 무제한으로 쌓이던 문제를, 로그 수집 구독 자체를 건너뛰도록 수정.

## [26.6.2] - 2026-06-02
### Changed
- **`ArduinoManager` DTR 활성화:** `dtrEnable`을 `true`로 변경하여 시리얼 연결 시 DTR 신호를 활성화.
- **씬 로드 로그 출력 주체 이전:** 기존 `Reporter`에서 로드된 씬 이름을 출력하던 방식을 제거하고, `GameManagerBase`의 `SceneManager.sceneLoaded` 이벤트에서 ZLogger로 씬 이름과 로드 모드를 출력하도록 변경.

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