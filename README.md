# Wonjeong Template

전시/키오스크형 Unity 앱을 위한 기본 템플릿 패키지입니다.
`Settings.json` 하나로 UI 레이아웃·사운드·종료 조건을 제어하므로, 빌드 후에도 코드 수정 없이 기획 변경에 대응할 수 있습니다.

- **Unity:** 2022.3 이상
- **플랫폼:** Windows (주 타깃) / WebGL (기획 검수용 빌드)
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
| ZLogger | `com.cysharp.zlogger` (2.5.10) | zero-GC 구조적 로깅 |
| ZString | `com.cysharp.zstring` (2.6.0) | zero-allocation 문자열 결합 |
| R3 | `com.cysharp.r3` (1.3.1) | 반응형 스트림 |
| MessagePipe | `com.cysharp.messagepipe` (1.8.2) | 전역 pub/sub |
| MessagePipe.VContainer | `com.cysharp.messagepipe.vcontainer` (1.8.2) | `RegisterMessagePipe()` 확장 |

> **주의**
> - 위 7개는 스코프 레지스트리 없이는 해석에 실패하므로 `dependencies`에 넣지 않고 수동 설치로 분리했습니다. **1번을 건너뛰면 2번이 실패합니다.**
> - `com.cysharp.messagepipe.vcontainer`를 빠뜨리면 `RootLifetimeScope`의 `builder.RegisterMessagePipe()`가 컴파일되지 않습니다.
> - `com.cysharp.zstring`을 빠뜨리면 `JsonLoader`·`SoundManager`의 `ZString.Concat`이 컴파일되지 않습니다.
> - Unity 레지스트리 의존성(Addressables, Input System, uGUI, 각종 모듈)은 이 패키지의 `dependencies`에 선언되어 있어 자동 설치됩니다.

### 3. NuGet 코어 패키지 설치 (NuGetForUnity)

ZLogger와 R3는 UPM 패키지가 Unity 연동 레이어만 제공하고, **실제 구현 DLL은 NuGet에서 받아야 합니다.**

1. [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)를 설치합니다.
   `https://github.com/GlitchEnzo/NuGetForUnity.git?path=src/NuGetForUnity`
2. **NuGet → Manage NuGet Packages**에서 아래를 설치합니다 (의존 패키지는 자동으로 딸려옵니다).

| 패키지 | 버전 |
|---|---|
| `ZLogger` | 2.5.10 |
| `R3` | 1.3.1 |
| `Microsoft.Extensions.Logging` | 10.0.10 |
| `Microsoft.Bcl.AsyncInterfaces` | 10.0.10 |
| `Microsoft.Bcl.TimeProvider` | 10.0.10 |
| `System.Text.Json` | 10.0.10 |
| `System.Threading.Channels` | 10.0.10 |
| `System.Runtime.CompilerServices.Unsafe` | 6.1.2 |
| `System.ComponentModel.Annotations` | 5.0.0 |

> - 위는 **직접 설치**해야 하는 목록입니다. `Microsoft.Extensions.Options`, `System.IO.Pipelines` 등 나머지는 의존 관계로 자동 설치됩니다.
> - **Player Settings → Api Compatibility Level**을 `.NET Standard 2.1`로 두어야 위 DLL들이 로드됩니다. `Microsoft.Extensions.*` 10.x도 netstandard 2.0/2.1 타깃을 제공하므로 Unity 2022.3에서 동작합니다.
> - `R3`는 UPM 패키지(`com.cysharp.r3`)와 **버전을 맞춰야 합니다.** 한쪽만 올리면 런타임에 타입 불일치가 날 수 있습니다.

### 4. 본 패키지 설치

Package Manager → **Add package from git URL** 로 이 저장소 주소를 입력합니다.
로컬 개발 중이라면 `manifest.json`에 파일 참조로 걸어도 됩니다.

```json
"com.wonjeong.template": "file:C:/Projects/Template"
```

---

## 구조

```
Runtime/
├─ App/        RootLifetimeScope   — DI 컨테이너 구성 진입점
├─ Core/       GameManagerBase<T>  — 상속용 게임 매니저 기반 클래스
├─ Data/       Settings 스키마 + AppSettingsProvider
├─ Hardware/   ArduinoManager      — 시리얼 통신 (WebGL은 스텁)
├─ Input/      TemplateInputActions
├─ UI/         UIManager · SoundManager · VideoManager · FadeManager
├─ Utils/      JsonLoader · GameCloser · SystemCanvas
└─ ThirdParty/ RuntimeInspector · LogViewer(Reporter)
```

### 설계 원칙

- **싱글톤 대신 DI** — 전역 `Instance` 접근을 쓰지 않고 `RootLifetimeScope`를 통해 주입합니다.
- **코루틴 대신 UniTask** — 파일 I/O와 연출 로직 전부 async. 프레임 드랍과 GC 할당을 줄입니다.
- **`Settings.json` 단일 로드** — `AppSettingsProvider`가 로드를 일원화하여 공유합니다. 매니저가 개별 로드하지 않습니다.
- **로깅은 ZLogger** — `Debug.Log` 대신 주입받은 `ILogger<T>`를 사용합니다.

> **ZLogger 사용 시 주의:** 보간 문자열 핸들러가 `ref struct`라 널 조건부 연산자(`?.`)와 충돌합니다.
> `_logger?.ZLogInformation(...)`이 아니라 `if (_logger != null) _logger.ZLogInformation(...)` 형태로 작성하세요.

---

## 사용법

### GameManager 확장

```csharp
public class GameManager : GameManagerBase<GameManager>
{
    protected override void Start()
    {
        base.Start();
        // 프로젝트 고유 초기화
    }
}
```

### 단축키 (기본 제공)

| 키 | 동작 |
|---|---|
| `F1` 계열 (ToggleDebug) | Reporter 로그 뷰어 토글 |
| `I` (ToggleInspector) | 런타임 인스펙터 토글 |
| ToggleMouse | 커서 표시 토글 |

> 실제 바인딩은 `Runtime/Input/TemplateInputActions.inputactions`에서 확인·수정하세요.

---

## Settings.json

`StreamingAssets/Settings.json`에 위치합니다.

```json
{
  "warningTime": 60,
  "resetTime": 90,
  "fadeTime": 0.5,
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

- `closeSetting.position`은 **정규화 좌표(0~1)** 입니다. `(0,0)`이 좌하단, `(1,1)`이 우상단.
- `fonts[].address`는 **Addressables 주소**이고, `sounds[].clipPath`는 **StreamingAssets 기준 상대 경로**입니다.
- `fonts[].key`는 자유롭게 명명할 수 있으며 `TextSetting.fontName`에서 이 키로 참조합니다.

---

## 플랫폼별 제약 (WebGL)

WebGL 빌드는 기획 검수 목적이며 다음 기능이 동작하지 않습니다.

| 기능 | WebGL 동작 |
|---|---|
| `ArduinoManager` (시리얼 통신) | 스텁. 호출은 컴파일되지만 경고 로그만 출력 |
| ZLogger 파일 출력 | 비활성 (브라우저 콘솔로만 출력) |
| `GameCloser`의 앱 종료 | 안내 로그만 출력 (브라우저는 `Application.Quit()` 무시) |
| `JsonLoader.SaveAsync` | StreamingAssets가 읽기 전용이라 저장 불가 |
