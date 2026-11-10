# HuliacDev Template

전시/키오스크형 Unity 앱을 위한 기본 템플릿 패키지입니다.
`Settings.json` 하나로 사운드·폰트·비활동 타이머·숨은 종료 버튼 같은 현장 설정을 제어하므로, 빌드 후에도 코드 수정 없이 기획 변경에 대응할 수 있습니다.

- **Unity:** 2022.3 이상
- **플랫폼:** Windows (주 타깃) / Android
- **라이선스:** MIT

---

## 설치

이 패키지는 Unity 레지스트리 밖의 의존성을 사용하므로 **순서대로** 설치해야 합니다.

### 1. 스코프 레지스트리 등록 (OpenUPM)

프로젝트의 `Packages/manifest.json`에 다음을 추가합니다.

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.cysharp",
        "jp.hadashikick"
      ]
    }
  ]
}
```

### 2. UPM 의존성 설치

Package Manager의 **Add package by name**으로 아래를 설치합니다. 괄호는 검증된 버전입니다.

| 패키지 | ID | 용도 |
|---|---|---|
| VContainer | `jp.hadashikick.vcontainer` (1.19.0) | DI 컨테이너 |
| UniTask | `com.cysharp.unitask` (2.5.11) | async/await (zero-allocation) |
| ZLogger | `com.cysharp.zlogger` (2.6.0) | zero-GC 구조적 로깅 |
| ZString | `com.cysharp.zstring` (2.6.0) | zero-allocation 문자열 결합 |
| R3 | `com.cysharp.r3` (1.3.1) | 반응형 스트림 |
| MessagePipe | `com.cysharp.messagepipe` (1.8.2) | 전역 pub/sub |
| MessagePipe.VContainer | `com.cysharp.messagepipe.vcontainer` (1.8.2) | `RegisterMessagePipe()` 확장 |

> **주의**
> - 위 7개는 스코프 레지스트리 없이는 해석에 실패하므로 `dependencies`에 넣지 않고 수동 설치로 분리했습니다. **1번을 건너뛰면 2번이 실패합니다.**
> - `com.cysharp.messagepipe.vcontainer`를 빠뜨리면 `RootLifetimeScope`의 `builder.RegisterMessagePipe()`가 컴파일되지 않습니다.
> - `com.cysharp.zstring`을 빠뜨리면 `JsonLoader`·`SoundManager`의 `ZString.Concat`이 컴파일되지 않습니다.
> - Unity 레지스트리 의존성(Addressables, Input System, uGUI, TextMeshPro, Test Framework, 각종 모듈)은 이 패키지의 `dependencies`에 선언되어 있어 자동 설치됩니다.

### 3. NuGet 코어 패키지 설치 (NuGetForUnity)

ZLogger와 R3는 UPM 패키지가 Unity 연동 레이어만 제공하고, **실제 구현 DLL은 NuGet에서 받아야 합니다.**

1. [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)를 설치합니다.
   `https://github.com/GlitchEnzo/NuGetForUnity.git?path=src/NuGetForUnity`
2. **NuGet → Manage NuGet Packages**에서 아래를 설치합니다 (의존 패키지는 자동으로 딸려옵니다).

| 패키지 | 버전 |
|---|---|
| `ZLogger` | 2.6.0 |
| `R3` | 1.3.1 |
| `Microsoft.Extensions.Logging` | 10.0.14 |
| `Microsoft.Bcl.AsyncInterfaces` | 10.0.14 |
| `Microsoft.Bcl.TimeProvider` | 10.0.14 |
| `System.Text.Json` | 10.0.14 |
| `System.Threading.Channels` | 10.0.14 |
| `System.Runtime.CompilerServices.Unsafe` | 6.1.2 |
| `System.ComponentModel.Annotations` | 5.0.0 |

> - 위는 **직접 설치**해야 하는 목록입니다. `Microsoft.Extensions.Options`, `System.IO.Pipelines` 등 나머지는 의존 관계로 자동 설치됩니다.
> - **Player Settings → Api Compatibility Level**을 `.NET Framework`로 둡니다. `ArduinoManager`가 쓰는 `System.IO.Ports`(SerialPort)가 이 프로필에만 있어, `.NET Standard 2.1`로 두면 Windows 빌드에서 컴파일되지 않습니다. 위 DLL들은 이 설정에서도 로드되며, `Microsoft.Extensions.*` 10.x도 netstandard 2.0/2.1 타깃을 제공하므로 Unity 2022.3에서 동작합니다.
> - `R3`·`ZLogger`는 UPM 패키지(`com.cysharp.r3`·`com.cysharp.zlogger`)와 **버전을 맞춰야 합니다.** 한쪽만 올리면 런타임에 타입 불일치가 날 수 있습니다. 설치한 뒤에는 `Tools > Update Stack Packages`가 UPM과 NuGet을 함께 올립니다([에디터 메뉴](#에디터-메뉴) 참고).

### 4. DOTween 설치

`FadeManager`·`SoundManager`의 페이드가 DOTween을 씁니다. DOTween은 UPM 배포가 없어 `dependencies`로 선언할 수 없으므로 프로젝트마다 직접 설치합니다.

1. 에셋스토어에서 DOTween(무료판)을 임포트하고 `Tools > Demigiant > DOTween Utility Panel`에서 셋업합니다.
2. 같은 패널의 **Create ASMDEF**로 `DOTween.Modules` 어셈블리를 만듭니다.
3. **Player Settings → Scripting Define Symbols**에 `UNITASK_DOTWEEN_SUPPORT`를 추가합니다. 플랫폼별 값이므로 Android도 빌드한다면 Android 탭에도 넣습니다.

> 2번과 3번을 빠뜨리면 이 패키지의 트윈 코드(`ToUniTask(TweenCancelBehaviour...)`)가 컴파일되지 않습니다.

### 5. 본 패키지 설치

Package Manager → **Add package from git URL** 로 이 저장소 주소를 입력합니다.
로컬 개발 중이라면 `manifest.json`에 파일 참조로 걸어도 됩니다.

```json
"com.huliacdev.template": "file:C:/Projects/Template"
```

### 6. VContainer 소스 제너레이터 (선택, 권장)

**하지 않아도 정상 동작합니다.** 다만 DI 주입이 런타임 리플렉션으로 처리됩니다.

VContainer는 주입 코드를 찾을 때 소스 제너레이터가 만든 클래스를 먼저 보고, 없으면 리플렉션으로 폴백합니다.

```csharp
// VContainer/Runtime/Internal/InjectorCache.cs
var generatedType = key.Assembly.GetType($"{key.FullName}GeneratedInjector", false);
if (generatedType != null) return (IInjector)Activator.CreateInstance(generatedType);
...
return ReflectionInjector.Build(key);   // 제너레이터가 없으면 여기로
```

UPM 패키지에는 제너레이터가 포함되어 있지 않으므로, 기본 상태에서는 이 템플릿의 `[Inject]` 지점 전부가 리플렉션 경로를 탑니다.

**설치**

1. [VContainer 릴리즈 페이지](https://github.com/hadashiA/VContainer/releases)에서 **설치된 UPM 패키지와 같은 버전 태그**의 `VContainer.SourceGenerator.dll`을 받습니다.
   - Unity 2022.3은 Roslyn 4.x이므로 `VContainer.SourceGenerator.dll`을 받습니다 (`...Roslyn3.dll`은 구버전 Unity용).
2. `Assets/` 아래 아무 곳에나 둡니다. 예: `Assets/Plugins/VContainerSourceGenerator/`
3. Project 창에서 DLL을 선택 → 인스펙터 하단 **라벨(태그) 아이콘** 클릭 → `RoslynAnalyzer` 입력
4. 인스펙터의 **Select platforms for plugin**에서 `Any Platform` 해제 → **Include Platforms**에서 Editor/Standalone 해제

> **버전을 반드시 맞추세요.** 생성되는 코드가 런타임의 `IInjector` 인터페이스와 맞물리므로, UPM 패키지와 제너레이터 버전이 어긋나면 컴파일 에러나 주입 실패가 발생할 수 있습니다.

**적용 범위**

`Assets/` 아래에 두면 `Assembly-CSharp`뿐 아니라 **이 패키지 어셈블리(`HuliacDev.Template`)에도 적용**됩니다. 패키지 안에 DLL을 넣을 필요는 없습니다.

**확인 방법**

리플렉션 폴백이 남아 있는지 코드로 확인할 수 있습니다.

```csharp
VContainer.IInjector injector = VContainer.Internal.InjectorCache.GetOrBuild(typeof(HuliacDev.UI.UIManager));
Debug.Log(injector.GetType().Name);
// 생성됨    -> UIManagerGeneratedInjector
// 폴백 중   -> ReflectionInjector
```

검증 환경(Unity 2022.3.62f3 / VContainer 1.19.0)에서 당시 템플릿의 `[Inject]` 지점이 모두 생성분으로 나오는 것을 확인했습니다. 그 뒤에 추가된 클래스는 다시 확인하지 않았으므로, 적용한 프로젝트에서 위 코드로 확인하세요.

---

## 검증 프로젝트 구성

이 패키지를 **개발·수정할 때** 쓰는 별도 Unity 프로젝트를 만드는 방법입니다. 패키지 자체는 UPM 패키지라 Unity로 직접 열 수 없으므로, 이를 `file:` 참조로 물린 프로젝트가 필요합니다.

위 설치 1~6단계를 마친 뒤 아래를 추가합니다.

### 테스트 노출

Test Runner는 기본적으로 `Assets/` 안의 테스트만 스캔합니다. 패키지 테스트를 보려면 프로젝트의 `Packages/manifest.json`에 다음을 추가해야 합니다.

```json
"testables": [ "com.huliacdev.template" ]
```

> 이걸 빠뜨리면 `AppSettingsProviderTests`가 Test Runner 창에 **아예 나타나지 않습니다.** 테스트가 없는 것처럼 보여 원인을 찾기 어려우니 주의하세요.

### 씬 구성

```
SampleScene
├─ GameManager        : GameManagerBase를 상속한 프로젝트 고유 클래스
├─ TestLifetimeScope  : RootLifetimeScope를 상속한 씬 스코프
├─ SystemCanvas       : Runtime/Prefabs/SystemCanvas.prefab (필수)
│   └─ (하위) GameCloser  : Button + Image + GameCloser (필수)
├─ Reporter           : Reporter + ReporterMessageReceiver
├─ Managers           : UIManager + SoundManager + VideoManager
└─ EventSystem        : InputSystemUIInputModule
```

```csharp
public class TestLifetimeScope : RootLifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        base.Configure(builder);   // 로깅 / MessagePipe / 설정 / 입력 / 공통 컴포넌트 / 씬에 있는 선택 매니저

        builder.RegisterComponentInHierarchy<GameManager>();
    }
}
```

> `RootLifetimeScope`가 `SystemCanvas`·`GameCloser`를 등록하고, 씬에 있는 `FadeManager`·`UIManager`·`SoundManager`·`VideoManager`·`ArduinoManager`·`ApiManagerBase`·`InactivityTimer`·`ShutdownScheduler`도 자동으로 찾아 등록하므로, 프로젝트 스코프에서는 `GameManager`만 등록합니다. `UIManager`는 `VideoManager`를 주입받으므로 씬에 함께 두어야 합니다.

> **소비자를 전부 올려두는 것이 중요합니다.** `Settings.json`을 요청하는 컴포넌트가 여럿이어야 같은 프레임에 동시 요청이 발생하고, 그 경합에서만 드러나는 결함을 잡을 수 있습니다. 실제로 `AppSettingsProvider`의 동시 요청 예외가 이 구성 덕분에 발견되었습니다. 소비자를 하나만 올리면 해당 유형의 문제는 재현되지 않습니다.

### 실제 로드 경로까지 검증하려면

폰트·오디오·비디오는 대응 에셋이 있어야 로드 경로가 실행됩니다. 없으면 설정 로드까지만 확인됩니다.

| 대상 | 준비물 | 설정 항목 |
|---|---|---|
| 폰트 | `.ttf`를 Addressable로 등록하고 주소 지정 | `Settings.json`의 `fonts[].address` |
| 오디오 | `StreamingAssets/`에 `.wav`/`.mp3`/`.ogg` | `Settings.json`의 `sounds[].clipPath` |
| 비디오 | `StreamingAssets/`에 `.mp4` | `UIManager.SetVideo`에 넘기는 `VideoSetting.fileName` |

> Addressables는 Play Mode Script를 **Use Asset Database (fastest)** 로 두면 콘텐츠 빌드 없이 동작합니다.

---

## 구조

```
Runtime/
├─ App/        RootLifetimeScope    — DI 컨테이너 구성 진입점
├─ Core/       GameManagerBase      — 순수 DI 게임 매니저 기반 클래스
│              InactivityTimer      — 무입력 감지 및 복귀 이벤트
│              ShutdownScheduler    — 자동 종료 스케줄러
│              WindowFocusRestorer  — 포커스를 잃은 앱 창 복구·항상 위 (Windows)
│              AutoRestartWatchdog  — 비정상 종료·응답 없음 시 자동 재시작 (Windows)
│              StateMachine · IState — 상태 머신
├─ Data/       Settings 스키마 + AppSettingsProvider
├─ Hardware/   ArduinoManager       — 시리얼 통신 및 자동 재연결
├─ Input/      TemplateInputActions
├─ Network/    NetworkStatusService — R3 기반 실시간 네트워크 모니터링
│              ApiManagerBase       — 서버 상태 로깅 베이스
│              ApiRetryUtil         — 네트워크 예외 격리 재시도
│              PacketUtility        — 바이너리 패킷과 구조체 변환
│              UdpCommandClient     — 랜 장치에 UDP 명령 전송(응답 확인·재전송)
│              TcpLineClient        — 랜 장치와 TCP 줄 단위 통신(재접속·keepalive)
├─ Prefabs/    SystemCanvas         — 숨은 종료 버튼이 들어 있는 시스템 캔버스
├─ UI/         UIManager · SoundManager · VideoManager · FadeManager
├─ Utils/      SingletonGuard<T> · JsonLoader · GameCloser · ConsecutiveClickCounter · SystemCanvas
│              LogRetentionService  — 로그 파일 회전 및 만료 정리
└─ ThirdParty/ RuntimeInspector · LogViewer(Reporter)
Editor/
├─ PackageUpdater          — Tools > Update Stack Packages (스택 UPM 패키지와 NuGet 업데이트)
├─ BuildMenu               — Build > Windows Build (빌드, 버전 관리용 zip 보관, 복사 폴더에 복사)
├─ BuildMenuPreferences    — Edit > Preferences > HuliacDev Build (보관·복사 경로, PC별)
├─ BuildCopyPromptWindow   — 빌드 뒤 복사 확인 창
└─ ShutdownScheduleEditorBuilder — Tools > Build Shutdown Scheduler Exe
Tools~/
└─ ShutdownScheduleEditor  — ShutdownSettings.json 편집 도구 (독립 Windows 프로그램)
Tests/
└─ Runtime/                — PlayMode 테스트
```

### 주요 클래스 및 기능

| 모듈 | 클래스 | 주요 기능 및 역할 |
|---|---|---|
| **Core** | `GameManagerBase` | 앱 수명주기 총괄, VContainer DI 기반 비동기 초기화, `OnSettingsLoaded` 가상 훅 제공 |
| | `InactivityTimer` | 키오스크 무입력 시간 감지 및 자동 복귀 이벤트(`InactivityTimeoutEvent`) 발행 |
| | `ShutdownScheduler` | 일별/요일별 자동 종료 스케줄링 및 사전 알림(`BeforeShutdownEvent`) 발행 |
| | `WindowFocusRestorer` | 앱 창이 포커스를 잃으면 다시 앞으로 가져와 키보드형 스캐너 입력 유지, 선택적으로 창을 항상 위로 둠 (Windows 스탠드얼론, `F` 키로 끄고 켬) |
| | `AutoRestartWatchdog` | 크래시·강제 종료·응답 없음으로 앱이 끝나면 숨은 감시 스크립트가 다시 실행, 정상 종료(숨은 종료 버튼·예약 종료·창 닫기)는 다시 켜지 않음 (Windows 스탠드얼론, `autoRestart`로 켬) |
| | `StateMachine<TContext>` | `IState<TContext>` 기반 상태 머신. 전환할 때 상태를 새로 만들지 않고 미리 만든 인스턴스의 참조만 바꿈 |
| **UI & Media** | `UIManager` | uGUI/TextMeshPro 비동기 폰트 프리로드/일괄 적용, 텍스처 VRAM 압축(`compress`) 제어. `Set*`가 새로 붙이는 이미지·글자·영상은 클릭을 받지 않도록 `raycastTarget`을 끄고(대상에 Button 등 Selectable이 있거나 버튼 배경이면 켬), 대상에 다른 Graphic이 이미 있으면 붙이지 않고 경고를 남김 |
| | `FadeManager` | 화면 페이드 인/아웃(DOTween/UniTask), 커스텀 색상(`Color? color`) 및 `SortingOrder` 제어 |
| | `SoundManager` | BGM/SFX 비동기 재생, 미리 불러오기(`PreloadAsync`), 마스터/BGM/SFX 개별 볼륨 및 음소거(`Mute`), DOTween 페이드 충돌 방어 |
| | `VideoManager` | 비디오 비동기 프리로드/재생(`PrepareAndPlayAsync`는 재생을 시작했는지 bool로 돌려줌), 고아 RenderTexture 메모리 최적 해제 ($O(N+M)$) |
| **Network** | `NetworkStatusService` | R3 기반 실시간 네트워크 도달성/끊김/복구 모니터링, Windows의 인터넷 판정 반영 (오프라인 키오스크 초기 알림 억제) |
| | `ApiManagerBase` | 시작/종료/비활동/외부 API 호출 이력 사내 서버 전송 추상 베이스 클래스 |
| | `ApiRetryUtil` | 고정 간격 재시도 및 소켓/DNS 네트워크 예외 격리를 지원하는 WebRequest 안정적 재시도, 응답 본문 반환(`GetTextWithRetryAsync`), 인터넷이 없으면 전송 생략 |
| | `PacketUtility` | 바이너리 패킷과 구조체 변환(`FromBytes<T>`, `ToBytes<T>`, `GetPacketSize<T>`) |
| | `UdpCommandClient` | 랜으로 연결한 장치에 UDP로 명령을 보내고, 장치가 같은 문자열을 돌려주는지로 확인. 응답이 없으면 정한 횟수까지 다시 보내고 명령은 순서대로 하나씩 보냄 |
| | `TcpLineClient` | 랜으로 연결한 장치(TCP 서버)에 접속을 유지하며 줄 단위로 받고 문자열을 보냄. 끊기면 간격을 늘려 가며 다시 접속, keepalive로 랜선 빠짐 감지, 이벤트는 메인 스레드에서 발행 |
| **Logging** | `LogRetentionService` | 일별 로그 파일 회전(`AddZLoggerRollingFile`) 및 30일 보관 주기 만료 파일 자동 정리 |
| **Hardware** | `ArduinoManager` | 시리얼 통신, 읽기 스레드 예외 격리 및 메인 스레드 마샬링 기반 자동 재연결(`AutoReconnect`, 연결된 뒤 끊겼을 때만 동작하며 `AutoReconnectIntervalMs`(기본 3초)에서 두 배씩 늘려 `AutoReconnectMaxIntervalMs`(기본 30초)에서 멈춤). 연결 변화는 이벤트(`OnConnected`·`OnDisconnected`)라 현재 상태는 `IsConnected`로 읽음. 포트를 열면 Uno·Nano 계열은 리셋돼 첫 줄까지 약 1.7초가 걸리므로 핸드셰이크를 `HandshakeTimeoutMs`(기본 3초) 동안 기다림. 모든 포트를 동시에 확인해 포트 수와 상관없이 약 1.7초에 연결됨 |
| **Data & Utils**| `AppSettingsProvider` | `Settings.json` 단일 로드/캐싱 및 런타임 핫 리로드(`ReloadAsync`) 지원, 읽기 실패 시 비활동 타이머를 켠 대체 설정 사용 |
| | `JsonLoader` | `StreamingAssets` 및 `PersistentData` 지원, `.json` 확장자 자동 보정 직렬화, 읽기 성공 여부 반환(`TryLoadAsync`), 임시 파일을 거친 저장과 저장 성공 여부 반환 |
| | `ConsecutiveClickCounter` | "N초 안에 M번 누르기" 연속 클릭 판정 (`GameCloser`가 사용, 운영자용 숨은 버튼에 재사용) |
| | `SingletonGuard<T>` | `DontDestroyOnLoad` 중복 방어 일원화, 즉시 비활성화 및 에디터 도메인 리로드 자가 복구 |

### 설계 원칙

- **싱글톤 대신 DI** — 전역 `Instance` 접근을 쓰지 않고 `RootLifetimeScope`를 통해 주입합니다.
- **코루틴 대신 UniTask** — 파일 I/O와 연출 로직 전부 async. 프레임 드랍과 GC 할당을 줄입니다.
- **`Settings.json` 단일 로드** — `AppSettingsProvider`가 로드를 일원화하여 공유합니다. 매니저가 개별 로드하지 않습니다.
- **로깅은 ZLogger** — `Debug.Log` 대신 주입받은 `ILogger<T>`를 사용합니다.
- **리플렉션 최소화** — 런타임 코드에 `System.Reflection` 직접 사용이 없습니다. DI 주입도 소스 제너레이터로 대체할 수 있습니다([설치 6단계](#6-vcontainer-소스-제너레이터-선택-권장)). 인스펙터 `UnityEvent` 바인딩 대신 코드로 직접 연결하는 것(`GameCloser`)도 같은 맥락입니다.

> **예외:** `ThirdParty/Plugins/RuntimeInspector`는 런타임 필드 조사가 존재 이유이므로 리플렉션을 사용합니다.
> 디버그 전용이라 릴리즈 빌드에서 제외하면 해당 비용도 사라집니다.

> **참고:** `JsonUtility`는 Unity의 네이티브 직렬화 백엔드를 쓰므로 관리 코드 리플렉션이 아닙니다.
> 다만 런타임 타입 주도 방식이라 IL2CPP 코드 스트리핑에 취약합니다. 빌드에서 설정이 비어 나온다면
> `link.xml`이나 `[Preserve]`로 `HuliacDev.Data` 타입을 보존하세요.

> **ZLogger 사용 시 주의:** 보간 문자열 핸들러가 `ref struct`라 널 조건부 연산자(`?.`)와 충돌합니다.
> `_logger?.ZLogInformation(...)`이 아니라 `if (_logger != null) _logger.ZLogInformation(...)` 형태로 작성하세요.

---

## 사용법

### GameManager 확장

```csharp
public class GameManager : GameManagerBase
{
    protected override void Start()
    {
        base.Start();
        // 프로젝트 고유 시작 로직
    }

    protected override void OnSettingsLoaded(Settings loadedSettings)
    {
        // 설정 로드 완료 직후 초기화 로직
    }
}
```

### 프로젝트 이벤트 브로커 등록

프로젝트 고유 MessagePipe 이벤트는 `RootLifetimeScope` 파생 클래스에서 `ConfigureProjectMessageBrokers`를 override해 등록합니다. 템플릿 브로커를 모두 등록한 뒤 `RegisterMessagePipe()`가 돌려준 `options`를 그대로 넘겨 주므로, 템플릿 목록을 복사할 필요가 없습니다.

```csharp
public class GameLifetimeScope : RootLifetimeScope
{
    protected override void ConfigureProjectMessageBrokers(IContainerBuilder builder, MessagePipeOptions options)
    {
        builder.RegisterMessageBroker<QuizAnsweredEvent>(options);
    }
}
```

> `ConfigureMessagePipe`를 override해 목록을 복사하지 마세요. `RegisterMessagePipe()`는 한 번만 부를 수 있어 복사본이 템플릿 목록 전체를 대신하게 되고, 템플릿이 나중에 브로커를 추가하면 그 이벤트를 구독하는 템플릿 컴포넌트가 컨테이너 빌드에서 해석에 실패합니다.

### InactivityTimer (비활동 타이머)

`Settings.json`의 `useInactivityTimer`/`resetTime`에 따라 일정 시간 입력이 없으면 `HuliacDev.App.InactivityTimeoutEvent`(MessagePipe 메시지)를 발행합니다. "최초 화면"이 별도 씬인지, 같은 씬의 첫 패널인지는 프로젝트마다 다르므로, 실제로 되돌아가는 로직(씬 로드, 패널 전환 등)은 이 이벤트를 `ISubscriber<InactivityTimeoutEvent>`로 구독해 프로젝트가 직접 구현합니다.

다른 선택 매니저와 동일하게, 씬에 `InactivityTimer` 컴포넌트를 두면(배치 자체가 사용 선언) `RootLifetimeScope`가 자동으로 찾아 등록합니다. VContainer로 런타임 생성하는 경우도 동작 방식은 동일하며, 두 경우 모두 이벤트 연결은 코드로만 가능합니다(런타임 스폰 특성상 인스펙터 연결은 지원하지 않음).

```csharp
public class PanelManager : MonoBehaviour
{
    private IDisposable _subscription;

    [Inject]
    public void Construct(ISubscriber<InactivityTimeoutEvent> subscriber)
    {
        _subscription = subscriber.Subscribe(_ => ReturnToFirstPanel());
    }

    private void OnDestroy()
    {
        _subscription?.Dispose();
    }

    private void ReturnToFirstPanel()
    {
        // 프로젝트별 복귀 로직
    }
}
```

`ApiManagerBase`를 함께 쓰는 프로젝트라면, 타임아웃으로 복귀했을 때 `move_idle_timeout` 로그가 자동으로 전송됩니다(`ApiManagerBase`가 `InactivityTimeoutEvent`를 직접 구독함). 종료 버튼처럼 사용자가 직접 첫 화면으로 돌아가는 일반적인 경우에는 `IPublisher<MoveIdleEvent>.Publish(new MoveIdleEvent())`를 호출하면 동일하게 `move_idle` 로그가 자동 전송됩니다(`ApiManagerBase`를 직접 참조할 필요 없음). 두 경우 모두 실제 화면 전환 로직과는 무관하므로, 위 `ReturnToFirstPanel` 같은 복귀 함수 안에서 로그를 직접 호출할 필요는 없습니다(아웃트로 등 특정 씬에서 타임아웃 시 정상 관람 완료로 집계하는 등 분기가 필요하면 `ApiManagerBase.OnInactivityTimeout()` / `OnMoveIdle()` 가상 메서드를 override).

긴 영상 재생처럼 입력 없이도 사용자가 실제로는 콘텐츠를 보고 있는 구간에서는 재생 시작/종료 시점에 `Pause()`/`Resume()`을 호출해 그동안 카운트가 멈추도록 합니다.

```csharp
[Inject] private InactivityTimer _inactivityTimer; // 씬 배치형이라면 선택 매니저처럼 IObjectResolver.ResolveOrDefault로 조회하는 것을 권장

void OnVideoStarted() => _inactivityTimer?.Pause();
void OnVideoFinished() => _inactivityTimer?.Resume();
```

### 효과음 미리 불러오기 (SoundManager.PreloadAsync)

`SoundManager`는 처음 트는 클립을 `StreamingAssets`에서 읽고 디코드한 뒤에야 재생합니다. 나레이션처럼 긴 효과음을 화면 연출과 같은 순간에 시작해야 하면, 앱을 켤 때나 그 장면에 들어가기 전에 `PreloadAsync`로 미리 읽어 두세요. 캐시에 있는 클립은 `PlaySFX`·`PlayBGM`이 읽기를 기다리지 않고 바로 재생합니다.

```csharp
// 클립이 캐시에 들어 있으면 true, 없는 키이거나 읽기에 실패하면 false (원인은 로그로 남음)
bool isLoaded = await _soundManager.PreloadAsync("narration", this.GetCancellationTokenOnDestroy());

// 나중에 연출을 시작할 때 같은 프레임에 재생됨
_soundManager.PlaySFX("narration");
```

- 키는 `Settings.json`의 `sounds[].key`입니다. BGM과 효과음이 같은 캐시를 쓰므로 BGM 키도 미리 읽을 수 있습니다.
- `SoundManager`가 사운드 목록을 읽기 전에 불러도(예: `GameManagerBase.OnSettingsLoaded`) 목록을 읽을 때까지 기다린 뒤 진행합니다. `SoundManager` 오브젝트가 꺼져 있어 목록을 읽지 못하면 넘긴 토큰이 취소될 때까지 기다리므로, 호출하는 쪽의 파괴 토큰(`this.GetCancellationTokenOnDestroy()`)을 넘기세요.
- 이미 캐시에 있으면 바로 `true`를 돌려주고, 같은 클립을 읽는 중이면 그 읽기를 함께 기다립니다.
- 넘긴 토큰이 취소되면 `OperationCanceledException`을 던집니다. 이미 시작한 읽기는 같은 클립을 기다리는 재생 요청과 공유되므로 멈추지 않고 끝까지 진행되어 캐시에 들어갑니다(사운드 목록을 기다리던 중에 취소되면 읽기를 시작하지 않습니다).
- 미리 읽은 클립은 다른 클립처럼 `ClearCache()`를 부를 때까지 메모리에 남습니다.
- `StopSFX()`는 재생 중인 효과음과 함께, 읽기를 기다리던 `PlaySFX` 요청도 취소합니다(읽기가 끝나도 재생하지 않음). 미리 불러오기는 취소하지 않습니다.

### 외부 API 호출 로깅 (wavespeed, GPT 등)

프로젝트 내에서 외부 API를 호출할 때 사내 로깅 서버(`Settings.json`의 `apiUrl`)로 호출 및 반환 상태를 전송해야 하는 경우 세 가지 방식 중 상황에 맞춰 사용할 수 있습니다.
- **호출 규격:** `Call API {http://apiaddress+parameter}`
- **반환 규격:** `Return OK` (성공) / `Return fail` 또는 `Return fail: {실패 사유}` (실패, 사유를 알 수 있을 때만 덧붙음)

```csharp
// 방법 1: 래퍼 메서드로 자동 전송 (호출 전 "Call API ~", 성공 시 "Return OK",
// 예외 발생 시 "Return fail: {예외 메시지}" — 예외 메시지는 자동으로 채워지며,
// 메시지에 URL 쿼리 스트링이 있으면 API 키/토큰 유출 방지를 위해 제거 후 전송됨).
// 로그는 기다리지 않고 뒤에서 보내므로 API 호출이 늦어지지 않고, Return은 Call 전송이 끝난 뒤 보냄.
WavespeedResult response = await _apiManager.ExecuteWithExternalApiLoggingAsync(
    "https://api.wavespeed.ai/v1/task?user=123",
    async () => await CallWavespeedAsync());

// 방법 2: ApiManagerBase 직접 호출 (실패 사유는 두 번째 인자로 직접 전달).
// 로그 전송은 재시도로 수십 초 걸릴 수 있어 기다리면 API 호출이 그만큼 늦어지므로 Forget으로 보냄.
// 이때 두 로그가 서버에 도착하는 순서는 보장되지 않으므로, 순서가 중요하면 방법 1을 씀.
_apiManager.SendExternalApiCallLogAsync("https://api.openai.com/v1/chat/completions").Forget();
(bool success, string errorReason) = await CallGptAsync();
_apiManager.SendExternalApiReturnLogAsync(success, errorReason).Forget();

// 방법 3: MessagePipe 이벤트 발행 (ApiManagerBase를 직접 참조하지 않는 경우)
_externalApiCallPublisher.Publish(new ExternalApiCallEvent("https://api.openai.com/v1/chat/completions"));
_externalApiReturnPublisher.Publish(new ExternalApiReturnEvent(true));
_externalApiReturnPublisher.Publish(new ExternalApiReturnEvent(false, "HTTP 500"));
```

### 전송 전 인터넷 연결 확인

`ApiRetryUtil`은 보내기 전에 인터넷에 나갈 수 있는지 확인하고, 나갈 수 없으면 재시도 없이 바로 실패를 돌려줍니다. `NetworkStatusService`도 같은 판정을 씁니다.

- 네트워크 어댑터가 끊겼으면 보내지 않습니다.
- Windows에서는 작업 표시줄의 지구본 아이콘을 정하는 Windows의 판정도 읽습니다. 공유기에는 연결됐지만 인터넷이 끊긴 경우("인터넷 없음")에도 보내지 않습니다. 네트워크 요청 없이 Windows가 가진 값을 읽기만 합니다.
- Windows가 아직 판정하지 못했거나 판정을 읽을 수 없는 환경(Windows 10 2004 이전, Windows가 아닌 환경)에서는 막지 않고 어댑터만 봅니다.
- `ApiManagerBase`의 시작 로그(`start`)는 Windows 판정으로 거르지 않고 보냅니다. Windows가 "인터넷 연결됨"이라고 하지 않는 동안(인터넷 없음 또는 판정 전) 시작 로그가 성공하면 그 현장에서는 Windows 판정을 믿을 수 없는 것으로 보고, Windows가 "인터넷 연결됨"이라고 할 때까지 어댑터만 봅니다. 외부 확인 주소를 막은 망이나 같은 LAN 안 서버로만 보내는 현장을 위한 동작이며, `apiUrl`로 보내는 로그(시작 로그와 아래 확인 전송)가 아닌 요청의 성공은 이 근거로 쓰지 않습니다.
- 시작 로그가 실패했거나(부팅 때 네트워크 어댑터가 아직 연결되지 않았으면 재시도 없이 바로 실패), 보정이 풀린 뒤 다시 막히면 idle·외부 API 로그 하나를 `InternetProbeIntervalSeconds`(기본 300초)마다 한 번 Windows 판정으로 거르지 않고 보내 봅니다. 이 확인 전송은 재시도하지 않으며, 닿으면 시작 로그와 같은 방식으로 보정합니다.
  - 종료 로그는 종료를 늦추지 않도록 확인 전송에 쓰지 않습니다. 보낼 idle·외부 API 로그가 없으면 확인하지 않습니다.
  - 간격을 바꾸려면 `ApiManagerBase`를 상속한 클래스에서 `InternetProbeIntervalSeconds`를 override하세요. 인터넷이 정말 없으면 확인할 때마다 실패 로그가 남습니다.
  - 에디터·Development 빌드는 로그를 실제로 보내지 않으므로 보정되지 않습니다.
- 같은 LAN 안 서버처럼 인터넷 없이 닿는 서버로 보내는 요청은 `apiUrl` 여부와 관계없이 `SendGetRequestWithRetryAsync`·`GetTextWithRetryAsync`에 `requireInternetAccess: false`를 넘기세요. 넘기지 않으면 인터넷이 없는 망에서는 보정되기 전까지 요청을 보내지 않습니다.
- `NetworkStatusService`의 `IsConnected`도 같은 판정이라, 인터넷 없이 LAN 서버만 쓰는 현장에서는 시작 로그나 확인 전송으로 보정되기 전까지 끊긴 것으로 보입니다. 그런 현장에서 LAN 연결 여부가 필요하면 `Application.internetReachability`를 직접 보세요.

### 응답이 필요한 API 호출 (ApiRetryUtil.GetTextWithRetryAsync)

사용자 확인·진행도 조회처럼 응답 본문을 해석해야 하는 API는 `GetTextWithRetryAsync`로 호출합니다. 재시도 간격·횟수와 네트워크 미연결 판정(위 "전송 전 인터넷 연결 확인")은 로그 전송용 `SendGetRequestWithRetryAsync`와 같고, 성공하면 `(true, 본문)`, 네트워크 미연결이거나 재시도를 모두 소진하면 `(false, null)`을 돌려줍니다.

```csharp
(bool isSuccess, string responseText) result = await ApiRetryUtil.GetTextWithRetryAsync(
    url, "user check", _logger, this.GetCancellationTokenOnDestroy(), maxAttemptCount: 3);

if (result.isSuccess)
{
    UserCheckResponse response = JsonUtility.FromJson<UserCheckResponse>(result.responseText);
}
```

- `SendGetRequestWithRetryAsync`와 달리 기본값으로 **에디터·Development 빌드에서도 실제로 전송**해 개발 중에 서버 응답을 확인할 수 있습니다. 운영 서버에 기록이 남는 요청이라 개발 중에는 보내면 안 되면 `skipInEditorAndDevelopmentBuild: true`를 넘기세요.
- HTTP 4xx를 포함한 모든 실패를 재시도합니다. '없는 사용자'를 404로 알리는 API라면 `maxAttemptCount`를 줄여 응답을 오래 기다리지 않게 하세요.
- 시도 한 번이 `attemptTimeoutSeconds`(기본 10초, `ApiRetryUtil.DefaultAttemptTimeoutSeconds`)를 넘기면 그 시도를 실패로 보고 재시도합니다. 연결만 받고 응답하지 않는 서버에서도 무기한 기다리지 않으며, 0 이하를 넘기면 시간 제한을 두지 않습니다.
- 성공 로그는 기본적으로 Information으로 남습니다. 사용자 확인처럼 자주 부르는 API는 `successLogLevel: LogLevel.Debug`(또는 `LogLevel.None`)로 낮추세요. 실패·재시도 로그는 그대로 남습니다.

### 랜 장치 연결 (UdpCommandClient · TcpLineClient)

랜선으로 연결한 아두이노 같은 장치와 주고받는 클라이언트입니다. USB 시리얼 장치는 `ArduinoManager`를 씁니다. 장치가 명령을 받기만 하고 받았다는 표시로 같은 문자열을 돌려주면 `UdpCommandClient`, 장치가 TCP 서버로 접속을 받아 상태를 줄 단위로 보내면 `TcpLineClient`를 씁니다. 둘 다 MonoBehaviour가 아닌 일반 클래스라 LifetimeScope에 등록해 주입받고, 컨테이너가 정리될 때 `Dispose`됩니다.

```csharp
builder.Register<UdpCommandClient>(Lifetime.Singleton);
builder.Register<TcpLineClient>(Lifetime.Singleton);
```

주소·포트처럼 현장에서 바꾸는 값은 `Settings.json`이 아니라 프로젝트의 목적별 JSON(예: `StreamingAssets/DeviceSettings.json`)에 두고 읽어서 넘깁니다.

```csharp
// UDP: 장치가 받은 명령을 보낸 쪽 IP·포트로 그대로 돌려줘야 함(이 클라이언트의 포트는 실행마다 바뀜)
_udp.SetTarget(settings.host, settings.port);   // IP 주소만
bool isAcknowledged = await _udp.SendAsync("1_enter");   // 결과가 필요 없으면 .Forget()

// TCP: 앱 시작 때 한 번 Connect하면 끊겨도 알아서 다시 접속함
_tcp.OnDataReceived.Subscribe(line => HandleLine(line)).AddTo(ref _disposables);
_tcp.OnConnected.Subscribe(_ => ShowDeviceReady()).AddTo(ref _disposables);
_tcp.Connect(settings.host, settings.port);
_tcp.Send("S2\n");   // 줄 끝 문자는 붙이지 않으므로 장치가 줄 단위로 받으면 직접 붙임
```

- 명령은 ASCII로 보내고 받습니다(한글 등은 '?'로 바뀜).
- `UdpCommandClient`는 메인 스레드에서만 부릅니다. 받기가 처음 보낸 호출의 스레드 문맥으로 이어지므로, 그래야 `OnDataReceived`도 메인 스레드에서 옵니다. 응답은 앞뒤 공백을 빼고 비교하므로 줄 끝 문자가 붙어 돌아와도 되지만, 같은 명령을 연달아 보내면 앞 명령의 늦은 응답을 뒤 명령의 응답으로 볼 수 있습니다.
- `UdpCommandClient`는 응답이 없으면 `ReplyTimeoutMs`(기본 3초)마다 `MaxSendAttempts`(기본 3번)까지 보내므로, 장치가 같은 명령을 두 번 받아도 문제없게 만들거나(같은 상태로 두는 명령 등) 대기 시간을 장치의 처리 시간보다 넉넉히 잡습니다. 앱이 끝날 때 장치를 원래 상태로 돌릴 명령은 `Dispose` 전에 `SendAsync`로 넣어 두면 응답을 기다리지 않고 한 번 보냅니다.
- `TcpLineClient`는 첫 접속이 실패해도 같은 간격 규칙으로 계속 다시 접속하므로 `Connect`를 한 번만 부릅니다(다시 부르면 간격이 처음으로 돌아감). 랜선이 빠진 것은 약 12초 안에 알아챕니다.
- `TcpLineClient`는 끊긴 동안의 `Send`를 쌓아 두지 않고 false를 돌려줍니다. 다시 접속한 뒤 예전 명령으로 장치가 움직이지 않게 하려는 것이므로, 접속 뒤에 필요한 명령은 `OnConnected`에서 다시 보냅니다.
- `OnConnected`·`OnDisconnected`는 다음 프레임에 오므로, 버튼 입력처럼 그보다 먼저 올 수 있는 호출에서 장치를 움직이는 명령을 보내기 전에는 `IsConnected`와 `ConnectionId`(접속될 때마다 1씩 늘어나는 번호)를 읽어 처리한 접속과 같은지 확인합니다. 메인 스레드가 오래 멈춘 사이 끊겼다가 다시 접속까지 끝나면 `IsConnected`만으로는 알 수 없습니다.

### 창 포커스 복구 (WindowFocusRestorer, Windows 전용)

전시 앱을 전체 화면으로 켜 두고 USB 바코드·QR 스캐너를 키보드 장치로 읽는 경우를 위한 기능입니다. Windows는 키보드 입력을 포커스가 있는 창에만 보내므로, 알림·업데이트 창·다른 프로그램이 포커스를 가져가면 누가 화면을 터치할 때까지 스캐너 입력이 앱에 들어오지 않습니다. Input System의 Background Behavior로는 해결되지 않습니다(키보드 장치는 백그라운드 입력을 받지 못함).

`RootLifetimeScope`가 `WindowFocusRestorer`를 자동으로 만들므로 씬 배치나 등록 코드는 필요 없습니다. 앱이 포커스를 잃으면 `focusRestoreDelay`초 뒤 앱 창을 다시 앞으로 가져오고, 포커스가 돌아올 때까지 `focusRestoreRetryInterval`초마다 다시 시도합니다.

- **Windows 스탠드얼론 빌드에서만 동작합니다.** 에디터와 다른 플랫폼에서는 아무것도 하지 않습니다.
- **앱을 켜면 켜진 상태로 시작하고, `F` 키(ToggleFocusRestore)로 끄고 켭니다.** 유지보수 중 다른 프로그램을 써야 하면 앱에서 `F`를 눌러 끄고, 끝나면 다시 눌러 켭니다. 앱을 다시 시작하면 켜진 상태로 돌아갑니다. 끄고 켤 때 로그가 남고, 화면 위쪽에 켜짐·꺼짐이 2초 동안 표시됩니다(끌 때는 다시 켜는 키도 함께 표시). 꺼 둔 동안 포커스를 잃으면 "꺼져 있어 창을 되돌리지 않는다"는 로그가 남습니다.
- `Settings.json`에서는 타이밍만 조정합니다(아래 Settings.json 절 참고). 파일을 읽지 못하면 기본 타이밍(3초/3초)으로 동작합니다.
- Player Settings의 **Run In Background**가 켜져 있어야 합니다. 꺼져 있으면 포커스를 잃은 동안 플레이어가 멈춰 복구할 수 없으며, 시작 시 경고 로그가 남습니다.
- Windows의 포그라운드 잠금 때문에 `SetForegroundWindow`만으로는 거부되는 경우가 많아, 현재 포그라운드 창의 스레드에 입력을 잠시 붙여(`AttachThreadInput`) 창을 올립니다. 단 포그라운드 창이 응답 없음 상태면 붙이지 않습니다(멈춘 창과 입력을 공유하면 앱도 함께 멈출 수 있음). 그래도 잠금 화면, UAC 확인 창, 관리자 권한으로 실행된 창이 앞에 있으면 Windows가 막을 수 있습니다. 실패하면 포커스를 잃을 때마다 처음 한 번만 경고 로그를 남기고 계속 재시도합니다.
- 앱 종료 중에는 시도하지 않습니다.

#### 항상 위 (alwaysOnTop, 선택)

시작 프로그램 폴더 등으로 여러 프로그램을 함께 켜면 실행 순서와 창이 뜨는 순서가 보장되지 않아, 나중에 뜬 다른 프로그램 창이 앱 화면을 가릴 수 있습니다. 포커스 복구는 `focusRestoreDelay`초 뒤에야 창을 되돌리고, 포그라운드 잠금으로 거부되면 계속 가려질 수 있습니다. `Settings.json`에 `"alwaysOnTop": true`를 적으면 앱 창을 항상 위(Topmost)로 두어 다른 창이 앱 위로 올라오지 못하게 합니다.

- 기본값은 꺼짐(`false`)이며, 생략하면 기존 동작과 같습니다. `Settings.json`을 읽지 못해 대체 설정으로 동작할 때도 꺼집니다.
- 앱을 시작할 때 한 번 읽으므로, 값을 바꾸면 앱을 다시 시작해야 합니다(`AppSettingsProvider.ReloadAsync`로 다시 읽어도 반영되지 않으며, 포커스 복구 타이밍도 같습니다).
- 포커스 복구와 함께 동작합니다. 항상 위는 화면 가림만 막고 키보드 포커스는 옮기지 않으므로, 스캐너 입력은 계속 포커스 복구가 되찾습니다.
- **`F` 키(ToggleFocusRestore)로 포커스 복구와 함께 끄고 켭니다.** 켜 두면 작업 관리자·오류 창·다른 프로그램도 앱 뒤에 가려지므로, 점검할 때는 `F`로 끄고 작업한 뒤 다시 켜세요. 화면 위쪽에는 "Focus restore and always on top ON/OFF"가 표시되고, 앱을 다시 시작하면 켜진 상태로 돌아갑니다. 앱을 끌 때는 Alt+F4나 숨은 종료 버튼(`GameCloser`)을 씁니다.
- `Display.Activate`로 연 보조 디스플레이 창에도 함께 적용하고, `F`로 끌 때도 함께 풉니다. 보조 창을 열면 메인 창의 항상 위가 풀리기도 하므로, 1초마다 확인해 항상 위가 아닌 창만 다시 적용합니다.
- 이미 항상 위인 다른 창(화상 키보드, '항상 위' 옵션을 켠 작업 관리자 등)과는 순서를 다투지 않으므로, 그런 창이 나중에 뜨면 앱 위에 보일 수 있습니다. 잠금 화면과 UAC 확인 창도 앱 위에 뜹니다.
- Windows 스탠드얼론 빌드에서만 동작하며, 에디터에서는 적용하지 않습니다.

> **바코드·QR 스캐너 주의:** 스캐너는 읽은 값을 키 입력으로 보내므로, 값에 대문자 `F`가 있으면 포커스 복구(항상 위를 켰으면 항상 위도 함께)가 꺼집니다(`D`·`I`·`M` 단축키도 같은 문제가 있습니다). 스캐너를 쓰는 프로젝트는 아래처럼 Ctrl 조합으로 바꾸세요. 스캐너는 Ctrl을 보내지 않습니다.

### 자동 재시작 (AutoRestartWatchdog, Windows 전용)

현장용 빌드가 크래시하거나 강제로 꺼지거나 '응답 없음'으로 멈췄을 때 앱을 다시 실행합니다. `Settings.json`에 아래처럼 적으면 켜집니다. 키가 없으면 꺼져 있어 기존 동작과 같습니다.

```json
"autoRestart": { "enabled": true, "hangRestartSeconds": 60, "restartDelaySeconds": 5 }
```

`RootLifetimeScope`가 `AutoRestartWatchdog`를 자동으로 만들므로 씬 배치나 등록 코드, 현장 PC의 설치·작업 스케줄러 등록은 필요 없습니다. 앱이 시작할 때 감시 스크립트(`AutoRestartWatchdog.ps1`, `persistentDataPath`에 매번 새로 씀)를 창 없는 PowerShell로 띄우고, 감시 스크립트가 앱 프로세스를 지켜봅니다.

- **종료 코드로 구분합니다.** 숨은 종료 버튼(`GameCloser`), 예약 종료(`ShutdownScheduler`), Alt+F4·창 닫기는 모두 `Application.Quit` 경로로 끝나 종료 코드가 0이므로 다시 켜지지 않고 감시도 함께 끝납니다. 크래시(예: `0xC0000005`)와 `taskkill /F`처럼 프로세스를 바로 끝낸 경우(1)는 0이 아니므로 `restartDelaySeconds`초 뒤 같은 실행 파일과 인수로 다시 켭니다. 앱이 스스로 다시 켜져야 할 때는 아래 "프로젝트 코드에서 쓰기"의 `TryRequestRestart`를 씁니다. 씬에 `ApiManagerBase`를 둔 프로젝트에서 `Application.Quit(코드)`만 부르면 `ApiManagerBase`가 종료 로그를 보낸 뒤 종료 코드 0으로 끝내 다시 켜지지 않습니다. 직접 0이 아닌 코드로 끝내려면 `QuitReason.Set(사유, 코드)`를 먼저 부릅니다.
- **응답 없음:** 창이 Windows에 '응답 없음'으로 판정된 상태(메시지를 5초 넘게 처리하지 못함)가 `hangRestartSeconds`초 이어지면 강제로 끄고 다시 켭니다. 큰 씬·영상 로딩으로 잠깐 막혀도 걸리지 않도록 기본 60초이며, 0이면 이 감지를 끕니다.
- **켜자마자 계속 끝나면** 앱이 60초 안에 끝나는 일이 이어질 때 두 번째부터 재시작 간격을 두 배씩 늘립니다(5초 → 10초 → 20초 …, 최대 10분). 크래시마다 Unity가 덤프를 남기므로 설정 오류 등으로 시작하자마자 죽는 상태에서 디스크가 차지 않게 하기 위함입니다. 60초 넘게 실행되면 간격이 원래대로 돌아옵니다.
- Windows가 종료·로그오프 중이면 다시 켜지 않습니다.
- 다시 켠 앱도 같은 감시 스크립트가 계속 지켜봅니다. 다시 켜진 앱은 시작 로그에 "Restarted by the watchdog ... (exit code -1073741819 (0xC0000005))"처럼 이유를 경고로 남기고(코드에서는 `RestartReason`), 감시를 또 띄우지 않습니다.
- 감시 스크립트가 시작·재시작·종료한 시각과 이유는 앱 로그 폴더(`persistentDataPath/Logs`)의 `AutoRestart.log`에 남습니다.
- 값은 앱을 시작할 때 감시 스크립트에 넘기므로, 바꾸면 앱을 정상 종료(Alt+F4 등)한 뒤 다시 켜야 반영됩니다. 크래시로 다시 켜진 동안에는 처음 띄운 감시의 값이 그대로 쓰입니다.
- 점검 등으로 앱을 끄려면 Alt+F4나 숨은 종료 버튼을 씁니다.
- 작업 관리자의 '작업 끝내기'는 앱이 응답하면 창 닫기와 같은 종료 요청을 보내는 것으로 보여(같은 Windows API `EndTask`로 시험), 앱이 정상 종료하면 다시 켜지지 않습니다. 앱이 응답하지 않을 때는 강제로 끝나므로 다시 켜지고, 응답 없음 감지도 따로 동작합니다.
- 크래시가 이어져 재시작을 기다리는 동안에는 끌 앱 창이 없습니다. 이때 재시작을 멈추려면 감시 스크립트를 끝냅니다. 작업 관리자 **세부 정보** 탭의 열 머리글을 오른쪽 클릭해 **명령줄** 열을 켜고, 명령줄에 `AutoRestartWatchdog.ps1`이 있는 `powershell.exe`를 끝냅니다. `autoRestart.enabled`를 false로 바꾸는 것만으로는 이미 떠 있는 감시가 멈추지 않습니다.
- 재시작을 기다리는 동안 앱을 직접 켜도 됩니다. 직접 켠 앱이 감시를 새로 띄우고, 대기가 끝나 예전 감시가 실행한 앱은 Player Settings의 **Force Single Instance** 때문에 곧바로 정상 종료(종료 코드 0)되므로 예전 감시도 함께 끝납니다. 이때 이미 떠 있는 앱 창이 한 번 앞으로 올 수 있습니다. 감시는 기다리는 동안 앱이 켜졌는지 확인하지 않으므로, Force Single Instance를 끈 프로젝트에서는 앱이 두 개 뜹니다.
- 그룹 정책으로 PowerShell 스크립트 실행을 막아 둔 PC에서는 감시가 뜨지 않을 수 있습니다. 앱을 켠 뒤 `AutoRestart.log`에 "Watching" 줄이 생기는지 확인합니다.
- `Settings.json`을 읽지 못해 대체 설정으로 동작할 때는 감시를 띄우지 않습니다.
- PC 재부팅 뒤 앱을 켜는 일은 하지 않습니다. 기존처럼 시작 프로그램 등으로 켭니다.
- 감시 스크립트만 작업 관리자에서 끄면 그 앱 실행 동안에는 감시가 없습니다. 다음에 앱이 시작될 때 다시 띄웁니다.
- Windows 스탠드얼론 빌드에서만 동작하며, 에디터에서는 띄우지 않습니다. 기능을 아예 빼려면 `ConfigureAutoRestart`를 빈 본문으로 override합니다.

**프로젝트 코드에서 쓰기:** `AutoRestartWatchdog`의 정적 API로 상태를 읽고 재시작을 요청합니다. 환경 변수 이름을 복사해 읽지 않습니다(템플릿이 이름을 바꾸면 컴파일 오류 없이 어긋납니다).

| API | 설명 |
|-----|------|
| `WasRestarted` / `RestartReason` | 이번 실행이 감시가 다시 켠 실행인지와 그 이유(`"exit code -1073741819 (0xC0000005)"`, `"not responding for 60s"`, `"restart requested by the app (exit code 75)"`). 다시 켠 실행이 아니면 false / null |
| `IsWatching` | 감시가 지금 이 앱을 지켜보는지. 감시를 띄운 직후 감시가 앱을 잡을 때까지(보통 몇 초), 꺼져 있거나 에디터이거나 감시 실행에 실패했거나 감시가 끝났으면 false |
| `WaitUntilWatchingAsync(token)` | 감시를 띄울지와 띄운 감시가 지켜보기 시작했는지가 정해질 때까지 기다린 뒤 `IsWatching`을 돌려줌. `ConfigureAutoRestart`를 빈 본문으로 override하면 끝나지 않으므로 시간 제한 토큰을 넘김. 토큰이 취소되면 `OperationCanceledException`이고, 그 뒤 코드는 취소가 일어난 스레드에서 이어지므로 시간 제한은 `CancelAfterSlim`(PlayerLoop에서 취소)으로 검 |
| `TryRequestRestart(reason)` | 감시가 지켜보고 있으면 종료 코드 75(`RestartRequestedExitCode`)로 끝내 `restartDelaySeconds`초 뒤 다시 켜지게 하고 true. 지켜보고 있지 않거나 다른 종료(숨은 종료 버튼·예약 종료·Alt+F4)가 이미 진행 중이면 끄지 않고 false. 종료 로그에는 `end (by {reason})`로 남음 |

```csharp
// 예: 모니터가 다시 연결되면 앱을 다시 켜서 해상도를 새로 잡음. 앱 시작 직후에도 부를 수 있으면 먼저 감시 결과를 기다림.
// 시간 제한은 CancelAfterSlim으로 걸어 메인 스레드(PlayerLoop)에서 취소되게 함. CancelAfter는 스레드 풀 타이머에서 취소하므로
// 시간이 다 되면 아래 코드가 메인 스레드 밖에서 이어지고, 그 뒤에 화면을 바꾸는 등 Unity API를 부르면 오류가 남.
using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
using IDisposable timeoutTimer = timeout.CancelAfterSlim(TimeSpan.FromSeconds(30), DelayType.Realtime);
(bool isCanceled, bool isWatching) = await AutoRestartWatchdog.WaitUntilWatchingAsync(timeout.Token).SuppressCancellationThrow();

// 다시 켜진 뒤 같은 이유로 계속 재시작하지 않도록, 감시가 다시 켠 실행이면 한 번 더 요청하지 않음.
if (!isCanceled && isWatching && !AutoRestartWatchdog.WasRestarted)
{
    AutoRestartWatchdog.TryRequestRestart("Monitor Reconnect");
}
```

앱이 60초 안에 다시 요청하는 일이 이어지면 크래시와 같이 재시작 간격을 두 배씩 늘립니다. 프로젝트가 직접 `Application.wantsToQuit`에서 false를 돌려 종료를 취소(확인 창 등)한다면 취소할 때 `QuitReason.Clear()`를 부릅니다. 부르지 않으면 취소된 재시작 요청의 종료 코드 75가 남아, 이후 Alt+F4로 끌 때 감시가 다시 켭니다.

### 예약 종료 (ShutdownScheduler)

`StreamingAssets/ShutdownSettings.json`(`Settings.json`과 별개 파일)에 적은 시각이 되면 Windows `shutdown` 명령으로 PC 종료를 예약하고 앱을 정상 종료합니다. 씬에 `ShutdownScheduler`를 두면 `RootLifetimeScope`가 자동으로 찾아 등록하고, 두지 않으면 동작하지 않습니다.

- 예정 시각은 기본 15초마다 확인합니다(인스펙터의 `checkIntervalSeconds`). 시각이 되면 `BeforeShutdownEvent`를 발행하므로, 페이드아웃 같은 마무리는 이 이벤트를 구독해 연결합니다.
- 요일별 기본 스케줄이 있고, `dateOverrides`에 등록한 날짜는 그날의 요일 스케줄 대신 적용됩니다.
- `shutdownArguments`가 비어 있으면 `-s -f -t 45`(45초 뒤 강제 종료)를 씁니다. 지연 시간은 종료 로그를 보낼 여유입니다.
- 예정 시각이 지난 뒤에 앱을 켜면 그날은 건너뜁니다. 켜자마자 다시 꺼지는 것을 막기 위함입니다.
- 파일은 `Tools > Build Shutdown Scheduler Exe`로 만든 편집 도구로 고칩니다. 메뉴를 실행하면 도구를 게시해 현재 프로젝트의 `StreamingAssets`로 복사하며, 약 150MB라 git에 커밋하지 않습니다. 파일 형식과 앱이 멈췄을 때를 대비한 작업 스케줄러 백업은 `Tools~/ShutdownScheduleEditor/README.md`를 참고하세요.

### 단축키 (기본 제공)

| 키 | 동작 (System 맵 액션) |
|---|---|
| `D` | Reporter 로그 뷰어 토글 (ToggleDebug) |
| `I` | 런타임 인스펙터 토글 (ToggleInspector) |
| `M` | 커서 표시 토글 (ToggleMouse) |
| `F` | 창 포커스 복구 끄기/켜기, `alwaysOnTop`을 켰으면 항상 위도 함께 (ToggleFocusRestore) |

> 기본 바인딩은 `Runtime/Input/TemplateInputActions.inputactions`에 있습니다. 패키지 파일을 고치지 말고, 프로젝트의 `RootLifetimeScope` 파생 클래스에서 `ConfigureInputBindings`를 override해 바꾸세요. 컨테이너가 `TemplateInputActions`를 만든 직후, 어떤 컴포넌트가 받아 켜기 전에 호출됩니다.

```csharp
public class GameLifetimeScope : RootLifetimeScope
{
    protected override void ConfigureInputBindings(TemplateInputActions inputActions)
    {
        // 다른 키 하나로 바꾸기
        inputActions.System.ToggleDebug.ApplyBindingOverride("<Keyboard>/f12");

        // Ctrl+F 조합으로 바꾸기 (스캐너 입력과 겹치지 않게)
        InputAction toggleFocus = inputActions.System.ToggleFocusRestore;
        toggleFocus.ChangeBinding(0).Erase();
        toggleFocus.AddCompositeBinding("OneModifier")
            .With("Modifier", "<Keyboard>/ctrl")
            .With("Binding", "<Keyboard>/f");
    }
}
```

> `RootLifetimeScope`를 쓰지 않거나 `ConfigureCoreComponents`에서 `TemplateInputActions` 등록을 뺀 프로젝트는 `GameManagerBase`가 입력 액션을 직접 만들므로 이 override가 적용되지 않고, 포커스 복구 토글 키도 동작하지 않습니다(포커스 복구는 켜진 채로 동작). 이때 `alwaysOnTop`을 켜면 항상 위도 끌 수 없으므로 시작할 때 경고 로그가 남습니다.

---

## 에디터 메뉴

### 스택 패키지 업데이트 (Tools > Update Stack Packages)

이 템플릿 스택의 UPM 패키지(VContainer, UniTask, MessagePipe, R3, ZLogger, ZString, NuGetForUnity, MCP for Unity, 이 패키지)만 골라 최신으로 올리고, 이어서 NuGetForUnity 창의 **Updates** 탭에 뜨는 NuGet 패키지(정식 버전)를 모두 올립니다. URP·Input System·Addressables처럼 프로젝트가 버전을 고정한 UPM 패키지는 건드리지 않습니다.

- UPM 패키지는 한 번의 요청으로 함께 올립니다. 하나씩 올리면 패키지가 바뀔 때마다 다시 컴파일되어 다음 요청이 "exclusive access" 오류로 실패하거나 남은 순서가 사라졌습니다.
- UPM 업데이트로 다시 컴파일되더라도 NuGet 업데이트는 컴파일이 끝난 뒤 이어서 실행됩니다.
- NuGetForUnity는 패키지를 다시 설치할 때 의존성을 최소 버전으로 다시 깔아, 먼저 올린 의존성이 도로 내려갈 수 있습니다(예: ZLogger 2.6.0을 설치하며 `System.IO.Hashing`이 8.0.0으로). 그래서 업데이트가 없을 때까지 최대 3번 올리고, 마지막에 한 번 더 확인합니다. 그래도 남으면 경고를 남깁니다.
- NuGetForUnity가 없는 프로젝트에서는 NuGet 단계를 건너뜁니다.
- 결과는 콘솔에 `[PackageUpdater]`로 남습니다. 업데이트 뒤에는 컴파일 오류와 테스트를 확인하세요.

### 빌드와 보관 (Build > Windows Build)

Build Settings의 씬 목록과 **Development Build** 설정으로 Windows 64비트 빌드를 프로젝트의 `Build/` 폴더(`<Product Name>.exe`)에 만들고, 성공하면 이어서 아래를 합니다. 이전 빌드의 파일이 섞이지 않도록 빌드 전에 `Build/` 폴더를 비우므로, 이 폴더에는 다른 파일을 두지 않습니다(빌드한 앱이 실행 중이라 비우지 못하면 알리고 멈춥니다).

1. **빌드 보관 폴더**가 정해져 있으면 `<보관 폴더>/<프로젝트 폴더 이름>/<YYMMDD>-<N>/`에 버전 관리용 zip(`<프로젝트 폴더 이름>_v<bundleVersion>.zip`)과 `BuildInfo.txt`(버전, 빌드 시각과 그날 몇 번째 빌드인지, git 브랜치·커밋, 커밋하지 않은 변경이 있었는지. git 값은 빌드 전에 읽음)를 둡니다. N은 그날 몇 번째로 보관한 빌드인지이며(예: `261103-1`, `261103-2`), 빌드마다 새 폴더를 만들어 이전 보관본을 덮어쓰지 않습니다.
2. **복사 폴더**가 정해져 있고 그 폴더가 있으면(드라이브가 연결돼 있으면) '빌드 뒤 복사' 설정에 따라 묻거나 바로 `<복사 폴더>/<프로젝트 폴더 이름>/`에 빌드 폴더를 압축 없이 복사합니다(현장 PC로 옮길 빌드, 항상 최신 빌드만 둠). 복사 전에 `<복사 폴더>/<프로젝트 폴더 이름>/`을 비우므로 그 폴더에 따로 넣어 둔 파일도 지워집니다. 다른 프로그램이 그 폴더의 파일을 쓰고 있어 비우지 못하면 복사하지 않고 오류 로그를 남깁니다. 묻는 창(**Copy Build**)에서 **Don't ask again**을 체크하면 누른 버튼(**Copy** / **Skip**)에 따라 '항상 복사'나 '복사 안 함'으로 바뀝니다. 보관이 실패해도 복사는 합니다.

Unity가 함께 만드는 현장에 보내면 안 되는 폴더(`*_BurstDebugInformation_DoNotShip`, IL2CPP의 `*_BackUpThisFolder_ButDontShipItWithYourGame`)는 zip과 복사에서 모두 뺍니다.

두 폴더와 '빌드 뒤 복사'(매번 묻기 / 항상 복사 / 복사 안 함)는 **Edit > Preferences > HuliacDev Build**(또는 **Build > Setting**)에서 정합니다. 팀원·PC마다 다를 수 있어 프로젝트가 아니라 그 PC의 Unity 환경설정(EditorPrefs)에 저장되며, 그 PC의 모든 프로젝트가 같은 값을 씁니다. 보관 폴더가 정해지지 않았거나 찾을 수 없으면(드라이브가 연결되지 않음) 빌드 전에 **Open Settings** / **Build Without Archiving** / **Cancel** 중에서 고릅니다. 두 경로는 저장소에 들어가지 않으므로, 저장소를 받은 팀원은 처음 빌드할 때 자기 경로를 정합니다.

- 설치 태그는 붙이지 않습니다. 현장에 설치한 버전은 `BuildInfo.txt`의 커밋을 보고 따로 태그합니다.
- 빌드할 때 URP가 URP 에셋의 셰이더 필터 값(`m_Prefilter…`)을 프로젝트에서 쓰는 기능에 맞춰 다시 쓰므로, 처음 빌드하면 URP 에셋이 바뀐 것으로 보일 수 있습니다. 설정이 같으면 Development 여부와 관계없이 다음 빌드도 같은 값을 쓰므로 한 번 커밋해 두면 다시 생기지 않습니다. 되돌리면 빌드할 때마다 다시 생기고, 다음 빌드의 `BuildInfo.txt`에 커밋하지 않은 변경으로 남습니다.
- 프로젝트의 `.gitignore`에 `/[Bb]uild/`가 있어야 빌드 결과가 git에 들어가지 않습니다.

---

## Settings.json

`StreamingAssets/Settings.json`에 위치합니다.

```json
{
  "useInactivityTimer": true,
  "resetTime": 90,
  "targetFrameRate": 60,
  "focusRestoreDelay": 3,
  "focusRestoreRetryInterval": 3,
  "alwaysOnTop": false,
  "autoRestart": { "enabled": true, "hangRestartSeconds": 60, "restartDelaySeconds": 5 },
  "apiUrl": "https://example.com/api/log?device=1&message=",
  "closeSetting": {
    "position": { "x": 0, "y": 1 },
    "numToClose": 10,
    "resetClickTime": 3,
    "imageAlpha": 0
  },
  "fonts": [
    { "key": "title", "address": "Fonts/NotoSans-Bold" },
    { "key": "body",  "address": "Fonts/NotoSans-Regular" }
  ],
  "sounds": [
    { "key": "bgm",   "clipPath": "Sounds/bgm.mp3",   "volume": 0.6 },
    { "key": "click", "clipPath": "Sounds/click.wav", "volume": 1.0 }
  ]
}
```

- 키를 생략해도 오류는 나지 않고 `false`, `0`, 빈 값으로 읽히며, 각 키의 동작은 아래와 같습니다. 모르는 키는 무시합니다. 키 이름은 대소문자를 구분하므로 `ResetTime`처럼 잘못 적으면 오류 없이 생략한 것과 같습니다.
- `Settings.json`을 읽지 못하면(파일 없음, 빈 파일, 끝 쉼표 같은 형식 오류) 오류 로그를 남기고 **비활동 타이머만 켠 대체 설정**(`resetTime` 90초, `AppSettingsProvider.FallbackResetTimeSeconds`)으로 동작합니다. 효과음·폰트·`closeSetting`·`alwaysOnTop`·`autoRestart`·`apiUrl`은 적용되지 않고 `targetFrameRate`도 값이 없어 수직 동기화가 켜지므로, 콘솔이나 로그 파일에 "Failed to load Settings.json"이 보이면 파일을 고치고 앱을 다시 시작하세요. 이때 숨은 종료 버튼은 `SystemCanvas` 프리팹 기본값(좌상단 150x150, 투명, 3초 안에 10번)으로 동작합니다.
- `useInactivityTimer`가 `true`이고 `resetTime`(초)이 0보다 크면 `InactivityTimer`가 활성화됩니다. 둘 중 하나라도 생략하면 꺼집니다(`useInactivityTimer`만 켜고 `resetTime`을 생략하면 경고 로그가 남습니다).
- `targetFrameRate`는 값을 지정하면 `vSyncCount`를 0으로 끄고 해당 FPS로 제한합니다. 0 이하(또는 생략)면 `vSyncCount`를 1로 켜서 프레임을 고정하지 않고 모니터 주사율을 따르며, 이때 품질 레벨의 `vSyncCount`는 쓰지 않습니다. 수직 동기화가 꺼져 있으면 영상처럼 화면 전체가 움직이는 장면에서 찢어짐이 보일 수 있으므로, 이런 앱은 0을 쓰세요. 고주사율 모니터에서는 0일 때 그만큼 더 많이 그리므로, 찢어짐보다 발열·전력 소모가 더 중요하면 30~60 사이 값을 지정하세요. 수직 동기화가 켜져 있으면 `Application.targetFrameRate`가 무시되어 다른 제한이 없으므로, 그래픽 드라이버가 수직 동기화를 강제로 끄면 프레임이 제한되지 않습니다.
- `focusRestoreDelay`(초)는 포커스를 잃은 뒤 첫 복구 시도까지 기다리는 시간, `focusRestoreRetryInterval`(초)은 포커스가 돌아오지 않았을 때 다시 시도하는 간격입니다. 0 이하(또는 생략)면 기본값 3초를 쓰고, 1초 미만 양수는 1초로 올려 적용합니다. 기능을 켜고 끄는 키는 Settings.json에 없고 `F` 키로 전환합니다([창 포커스 복구](#창-포커스-복구-windowfocusrestorer-windows-전용) 참고).
- `alwaysOnTop`이 `true`면 Windows 스탠드얼론 빌드에서 앱 창을 항상 위로 둡니다. 생략하면 `false`(끔)이며, 점검할 때는 `F` 키로 포커스 복구와 함께 끕니다([항상 위](#항상-위-alwaysontop-선택) 참고).
- `autoRestart.enabled`가 `true`면 Windows 스탠드얼론 빌드에서 크래시·강제 종료·응답 없음 뒤 앱을 다시 켭니다. 생략하면 꺼짐입니다. `hangRestartSeconds`는 생략하면 60초, 0이면 응답 없음 감지를 끄고, 10초 미만은 10초로 올립니다. `restartDelaySeconds`는 0 이하(또는 생략)면 5초, 1초 미만은 1초로 올리고 600초보다 크면 600초로 줄입니다([자동 재시작](#자동-재시작-autorestartwatchdog-windows-전용) 참고).
- `apiUrl`은 `ApiManagerBase`가 로그를 보낼 서버 주소입니다. `message=`까지 포함한 전체 주소를 적으면 로그 내용을 뒤에 이어 붙여 GET으로 보냅니다. 생략하면 보내지 않습니다.
- `closeSetting.numToClose`를 생략하거나 0 이하로 적으면 `closeSetting` 전체를 무시하고 `GameCloser` 인스펙터 값을 씁니다. `position`·`resetClickTime`·`imageAlpha`는 생략한 항목만 인스펙터 값을 유지합니다.
- `closeSetting.position`은 **정규화 좌표(0~1)** 입니다. `(0,0)`이 좌하단, `(1,1)`이 우상단.
- `fonts`·`sounds`를 생략하면 등록되는 폰트·사운드가 없습니다. `sounds[].volume`을 생략하면 1.0입니다.
- `fonts[].address`는 **Addressables 주소**이고, `sounds[].clipPath`는 **StreamingAssets 기준 상대 경로**입니다.
- `fonts[].key`는 자유롭게 명명할 수 있으며 `TextSetting.fontName`에서 이 키로 참조합니다.
