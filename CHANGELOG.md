# Changelog
모든 주요 변경 사항을 이 파일에 기록합니다.

## [26.9.3] - 2026-09-02

### Added
- **프로그램 종료 시 서버 종료 로그 전송 추가(`ApiManagerBase`):** 시작 로그("Program started"/"Program restarted")와 짝이 되도록, 앱이 종료될 때 `Settings.json`의 `apiUrl`로 "Program exited" 상태 메시지를 전송함. `apiUrl`이 비어 있으면 생략하고, 네트워크 자체가 연결되어 있지 않으면 즉시 포기하며, 실패 시 재시도하는 정책은 `ApiRetryUtil`을 그대로 재사용하므로 시작 로그와 동일함.

  `OnApplicationQuit`이 아니라 `Application.wantsToQuit`을 사용함. `OnApplicationQuit`에서 전송을 시작하면 요청은 나가더라도 응답을 받기 전에 프로세스가 사라져 로그가 유실되기 때문. `wantsToQuit`에서 `false`를 반환해 종료를 한 번 보류시키고, 전송이 끝난 뒤 다시 종료를 진행함(전송 성공·실패·시간 초과와 무관하게 `finally`에서 반드시 종료를 재개하므로 로그 때문에 종료가 막히지 않음).

  다만 종료를 보류하는 시간은 짧아야 함 — 사용자가 종료를 기다리는 상황인 데다, OS가 시작한 종료(작업 스케줄러의 `shutdown`, 시스템 종료 등)에서는 Windows가 앱을 기다려주는 시간이 `WaitToKillAppTimeout`으로 제한되어 그 안에 끝내지 못하면 어차피 강제 종료됨. 그래서 시작 로그(3초 x 10회)보다 훨씬 짧은 기본값(1초 x 3회, 전체 상한 5초)을 쓰고, 프로젝트별로 조정할 수 있도록 `ExitLogMaxAttemptCount`/`ExitLogRetryDelaySeconds`/`ExitLogTimeoutSeconds`를 `protected virtual` 프로퍼티로 노출함. 에디터의 플레이 모드 종료는 `Application.Quit()`으로 재개되지 않으므로 `GameCloser`와 동일하게 `EditorApplication.isPlaying = false`로 분기함. 전송이 동기적으로 즉시 끝나는 경우(에디터·네트워크 미연결) 종료 재개가 `wantsToQuit`의 반환보다 앞서지 않도록 한 프레임 양보한 뒤 진행함.

- **예약 종료 스케줄러(`ShutdownScheduler`) 추가:** `Runtime/Core/ShutdownScheduler.cs`를 신설하고 `RootLifetimeScope.ConfigureOptionalComponents`에 기존 선택 매니저와 동일한 `RegisterIfPresentInScene` 패턴으로 자동 등록함(씬에 배치하지 않으면 조용히 비활성이므로 기존 프로젝트에는 동작 변화가 없음). StreamingAssets의 `ShutdownSettings.json`을 읽어 요일별·날짜별 예정 시각에 도달하면, 서버에 종료 로그를 보낸 뒤 Windows `shutdown` 명령으로 PC를 종료함.

  종료를 작업 스케줄러에 전적으로 맡기지 않고 앱이 직접 거는 이유는 **순서를 통제하기 위함**임. 외부에서 종료 신호가 오면 Windows가 앱의 정리를 기다려주는 시간이 `WaitToKillAppTimeout`으로 제한되어, 종료 로그 전송이 그 안에 끝나지 못하면 유실될 수 있음. 앱이 스스로 시각을 판단하면 `shutdown` 명령의 지연 시간(`-t`)만큼 여유를 확보한 뒤 정상 종료 절차를 태울 수 있어 이 경합이 사라짐.

  동작 순서는 마무리 이벤트 실행 → `shutdown` 명령으로 OS 종료 예약 → `Application.Quit()`임. 종료 로그를 이 클래스가 직접 보내지 않고 위의 `ApiManagerBase` 종료 로그 경로에 맡기는 이유는, 양쪽에서 보내면 예약 종료 때만 로그가 두 번 기록되기 때문. `shutdown`이 지연 시간을 두고 OS 종료를 예약하므로 그 사이에 `ApiManagerBase`가 로그를 끝까지 보낼 시간이 확보됨.

  로드는 `JsonLoader.LoadAsync`를 재사용함. 종료 직전에 페이드아웃·저장 등 프로젝트별 마무리를 끼워 넣을 수 있도록 `UnityEvent onBeforeShutdown`을 인스펙터에 노출하고, 런타임 생성 시에도 연결할 수 있게 `OnBeforeShutdown` 프로퍼티로 공개함(`InactivityTimer`와 동일한 패턴).

  예정 시각이 이미 지난 뒤에 앱이 시작된 경우(운영자가 밤에 키오스크를 재시작한 상황 등)에는 그날을 이미 처리한 것으로 표시해 건너뜀 — 그렇지 않으면 켜자마자 다시 꺼져 현장에서 손을 쓸 수 없게 됨. 확인 주기(기본 15초)는 인스펙터로 조정 가능하되 0 이하로 설정해도 프레임마다 도는 폭주가 되지 않도록 1초 하한을 둠. 에디터와 Windows 이외 플랫폼에서는 실제 종료 대신 무엇을 실행했을지만 로그로 남김.

- **종료 스케줄 데이터 구조(`ShutdownSetting`) 추가:** `Runtime/Data/TemplateData.cs`에 `ShutdownDaySchedule`(요일별 `enabled`/`time`), `ShutdownDateOverride`(특정 날짜 `date`/`enabled`/`time`), 이를 담는 `ShutdownSetting`(월~일 + `dateOverrides` + `shutdownArguments`)을 추가함. `Settings.json`에 합치지 않고 StreamingAssets의 **별도 파일 `ShutdownSettings.json`**(이 클래스가 파일의 루트)로 분리한 이유는, 현장에서 종료 시각만 수정할 때 다른 설정 파일을 건드릴 일이 없도록 하기 위함. `dateOverrides`에 등록된 날짜는 그날의 요일별 기본 스케줄보다 우선 적용됨(특정 월요일 하루만 휴무로 지정하거나 시각을 늦추는 용도). `shutdownArguments`는 기본값 `-s -f -t 45`이며, 지연 시간을 두는 것은 종료 로그 전송이 끝날 시간을 확보하기 위함(`-r`로 바꾸면 재부팅).

- **종료 스케줄 편집기(`Tools~/ShutdownScheduleEditor`) 추가:** `ShutdownSettings.json`을 편집하는 독립 실행형 Windows GUI 도구(.NET 8 WinForms). Unity가 설치되지 않은 전시 현장 PC에서도 쓸 수 있도록 Unity 패키지 어셈블리(Runtime/Editor asmdef)와 완전히 분리된 프로젝트로 두었으며, 런타임에는 포함되지 않음. `파일 > 프로젝트에서 열기`로 Unity 프로젝트 루트·`Assets`·`StreamingAssets` 중 어느 폴더를 선택해도 `StreamingAssets/ShutdownSettings.json` 위치를 찾아내고, 파일이 없으면 확인 후 기본값(전 요일 비활성, 월요일 09:10·나머지 요일 17:35)으로 생성함. 요일별 종료 여부·시각, 특정 날짜 재정의(달력 입력 + 중복 날짜 덮어쓰기 확인 + 날짜순 자동 정렬), 종료 명령 인수를 편집할 수 있음. 잘못된 형식이 들어갈 여지를 없애기 위해 날짜 목록은 직접 편집을 막고 입력 줄로만 등록하도록 했으며, 저장 시 이 도구가 다루지 않는 키는 원본 그대로 보존함(루트를 통째로 교체하지 않고 편집 대상 키만 덮어씀).

- **작업 스케줄러 백업 연동(`도구 > 작업 스케줄러 백업`) 추가:** 앱 자체가 멈춰 `ShutdownScheduler`가 동작하지 못하는 경우를 대비해, 예정 시각 +N분(기본 5분)에 PC를 끄는 Windows 작업 스케줄러 항목을 등록·해제하는 기능. 가드 스크립트는 유니티와 무관한 별도 `powershell.exe` 프로세스로 실행되므로 앱이 프리징·크래시 상태여도 동작함.

  작업 스케줄러의 트리거는 "요일 + 시각" 반복은 표현할 수 있어도 **"특정 날짜는 제외"를 표현할 수 없어**, 요일 트리거만 걸면 휴무일로 지정한 날에도 백업이 PC를 꺼버림. 그래서 트리거는 시각을 잡는 역할만 하고 실제 종료 여부는 실행 시점에 `ShutdownBackupGuard.ps1`(등록 시 설정 파일 옆에 자동 생성)이 `ShutdownSettings.json`을 다시 읽어 판단하도록 함 — 특정 날짜 규칙을 바꿔도 작업을 재등록할 필요가 없음(요일·시각 변경 시에는 재등록 필요). 실행 기록은 설정 파일 옆 `ShutdownBackup.log`에 남음.

  가드가 실행 시점에 파일을 다시 읽는 덕분에 "잘못 꺼지는" 방향의 변경(특정 날짜를 휴무로 지정, 요일 끄기)은 재등록 없이 반영되지만, 반대로 시각을 바꾸거나 새로 종료를 켜는 변경은 트리거 자체가 옛날 값이거나 아예 없어 **백업이 조용히 동작하지 않는 상태**가 됨. 이를 눈치채지 못하는 것이 가장 위험하므로, 백업이 등록된 상태에서 스케줄을 저장하면 작업 스케줄러도 갱신할지 물어보고, 수락 시 기존에 등록된 지연 시간(`-DelayMinutes` 값을 되읽음)을 그대로 유지한 채 재등록함.

  등록되는 작업 이름은 `shutdown-backup`이며, 로그온 세션에 의존하지 않도록 `S4U` 로그온 유형("사용자의 로그온 여부에 관계없이 실행" + "암호를 저장하지 않습니다. 이 작업은 로컬 리소스에만 액세스할 수 있습니다")과 `HighestAvailable` 실행 수준("가장 높은 수준의 권한으로 실행")으로 등록함. 자동 로그인이 풀려 있어도 백업이 동작해야 하고, 로컬에서 `shutdown`만 실행하므로 네트워크 자원 접근 제약은 문제가 되지 않음. 이 두 설정은 승격 없이 등록되지 않으므로 등록·해제는 UAC 승격(`ShellExecute`의 `runas`)으로 수행하며, 사용자가 승격을 취소하면(`ERROR_CANCELLED`) 변경 없이 안내만 함. 승격은 출력 리디렉션을 지원하지 않아 실패 시 종료 코드만 표시됨. 상태 조회는 읽기 전용이라 승격 없이 수행함.

  놓친 트리거를 다음 부팅 직후에 따라잡는 `StartWhenAvailable`은 **끔** — 켜져 있어야 할 트리거를 놓쳤다는 것은 그 시각에 PC가 이미 꺼져 있었다는 뜻이므로, 따라잡으면 아침에 켠 키오스크를 그대로 다시 끄는 사고가 남. 종료 시각 + 지연 시간이 자정을 넘기면 트리거가 다음 날로 밀려 가드가 엉뚱한 요일의 스케줄을 보게 되므로 등록 시 막고 안내함.

- **작업 스케줄러 백업 경로에도 종료 로그 전송:** 유니티가 멈춰서 `ApiManagerBase`의 정상 종료 로그 경로를 타지 못한 경우, 가드 스크립트(`ShutdownBackupGuard.ps1`)가 같은 폴더의 `Settings.json`에서 `apiUrl`을 직접 읽어 종료 로그를 전송함. 유니티 프로세스와 무관한 PowerShell 프로세스라 유니티가 멈춘 상태에서도 전송을 시도할 수 있고, 정상 종료 로그와 구분되는 메시지를 쓰므로 두 경로가 겹쳐도 중복 기록되지 않음.

- **종료 로그에 종료 주체 구분 추가(`QuitReason`):** `Runtime/Utils/QuitReason.cs`를 신설함. `Application.wantsToQuit` 자체는 누가 종료를 요청했는지 알려주지 않으므로, 종료를 직접 발동시키는 쪽(`ShutdownScheduler`, `GameCloser`)이 `Application.Quit()` 직전에 자기 이름을 정적 상태에 남기고, `ApiManagerBase`가 종료 로그 전송 시 이를 메시지에 반영함. 아무도 기록하지 않은 채 종료되는 경우(Alt+F4, 창 닫기 버튼처럼 OS가 직접 발생시키는 종료)는 사용자가 직접 닫은 것이므로 기본값 `User`로 남음. 최종적으로 `Program exited (by User)` / `(by GameCloser)` / `(by ShutdownScheduler)` / `(by TaskScheduler)` 네 가지로 구분되어, 서버 로그만 보고도 어떤 경로로 종료됐는지 알 수 있음.

- **종료 스케줄 편집기 exe/창 아이콘 적용:** `Irem_Icon.png`를 16/32/48/256px 멀티 해상도 `app.ico`로 변환해 `ApplicationIcon`으로 등록함. exe 파일 자체의 아이콘(탐색기)과 실행 중인 창의 제목표시줄·작업표시줄 아이콘은 별개라, `Form.Icon`에 exe에 구운 아이콘을 추출해 명시적으로 지정함.

### Fixed
- **종료 스케줄 편집기 레이아웃이 고DPI 모니터에서 깨지던 문제 수정:** PerMonitorV2 DPI 인식이 폰트는 모니터별로 정확히 키워주지만, 코드에 하드코딩된 픽셀 크기(FlowLayoutPanel/GroupBox의 고정 `Height`, `TableLayoutPanel`의 `Absolute` 컬럼, `DateTimePicker`/`NumericUpDown`의 고정 `Width`)는 그대로라 폰트가 커진 모니터에서 라벨이 줄바꿈되거나 값이 잘리는 문제가 있었음(예: "월요일"이 두 줄로 줄바꿈, "09:10"이 "09:1"로 잘림).

  고정 `Height`는 전부 `AutoSize`로 바꾸고, 라벨/체크박스처럼 내용 기반 AutoSize를 지원하는 컨트롤은 `TableLayoutPanel` 컬럼도 `Absolute`에서 `AutoSize`로 바꿔 자동으로 맞춰지게 함. `DateTimePicker`/`NumericUpDown`처럼 내용 기반 AutoSize를 지원하지 않는 컨트롤은 `OnLoad`에서 실제 폰트로 표본 텍스트 폭을 직접 측정(`TextRenderer.MeasureText`)해 `Width`를 계산함.

  다른 DPI의 모니터로 창을 드래그하면 `OnLoad`는 다시 호출되지 않으므로 `OnDpiChanged`를 오버라이드해 폭을 다시 계산하도록 했고, 위쪽 고정 영역(요일별 스케줄 등)이 커진 폰트만큼 늘어나는데 창 자체 크기가 그대로면 `Dock=Fill`인 "특정 날짜" 영역이 밀려 안 보이는 문제가 있어, 고정 영역들의 실측 높이를 합산해 부족하면 창 높이를 직접 늘리도록 함(Windows가 제안하는 `DpiChangedEventArgs.SuggestedRectangle`은 폰트 기준 커스텀 레이아웃까지 정확히 반영하지 못해 그대로 신뢰하지 않음).

  참고로 `AutoScaleMode.Dpi`를 `Program.cs`의 `ApplicationConfiguration.Initialize()`가 이미 설정한 PerMonitorV2와 함께 쓰면 두 스케일링 메커니즘이 충돌해 창이 절반 크기로 줄어드는 것을 실측으로 확인함 — 그래서 `AutoScaleMode`는 건드리지 않고 위 방식으로 해결함.

### Changed
- **저장 시 백업 스케줄러 자동 갱신:** 백업이 이미 등록된 상태에서 저장하면 "갱신할까요?" 확인창 없이 곧바로 함께 갱신하도록 바꿈. 확인창 자체가 깜빡하고 지나치기 쉬운 지점이라, 시각을 바꾸거나 새로 종료를 켠 변경이 백업에 반영되지 않는 사고가 재발했었음. 저장 = 백업도 최신 상태로 확정, 이 규칙 하나만 기억하면 되도록 함(대가로 백업이 등록되어 있으면 저장마다 UAC 승격 창이 뜸).

- **서버 로그 메시지 규칙을 외부 로깅 명세에 맞춰 재정리(Breaking):** 기존 "Program started"/"Program restarted"/"Program exited (by X)" 문자열을 명세가 요구하는 형식으로 전면 교체함. 서버가 이 정확한 문자열로 파싱하므로 이전 값에 의존하는 서버/대시보드 쪽도 함께 갱신해야 함.
  - 시작: `start` / `start (restart)`
  - 종료: `end (by User)` / `end (by GameCloser)` / `end (by Shutdown Scheduler)`
  - 종료(강제): 유니티가 멈춰 작업 스케줄러 백업이 대신 끈 경우만 `end`가 아니라 `end_kill (by Task Scheduler)` — 정상 종료와 강제 종료를 서버 로그만으로 구분하기 위함
  - idle 화면 진입: `move_idle` / `move_idle_timeout`

  `QuitReason`(`Runtime/Utils/QuitReason.cs`)의 문자열 값을 표기 규칙에 맞춰 공백 포함 형태("Shutdown Scheduler")로 바꿨고, `ApiManagerBase`에 idle 로그 전송용 공개 메서드 `SendMoveIdleLogAsync()`/`SendMoveIdleTimeoutLogAsync()`를 추가함. "최초 화면"이 프로젝트마다 다르므로 일반적인 idle 복귀(`move_idle`)는 프로젝트 코드가 해당 지점에서 직접 호출해야 하지만, `InactivityTimer`의 타임아웃(`move_idle_timeout`)은 두 컴포넌트 모두 표준 컴포넌트라 프로젝트마다 다를 이유가 없으므로 `RootLifetimeScope`가 씬에 `InactivityTimer`와 `ApiManagerBase`가 함께 있을 때 자동으로 연결함(`RegisterIfPresentInScene`가 등록 여부를 `bool`로 반환하도록 변경해, 둘 다 있을 때만 연결하도록 판단).

## [26.9.2] - 2026-09-02

### Added
- **비활동 타이머(`InactivityTimer`) 추가:** `Runtime/Core/InactivityTimer.cs`를 신설하고 `RootLifetimeScope.ConfigureOptionalComponents`에 기존 선택 매니저와 동일한 `RegisterIfPresentInScene` 패턴으로 자동 등록함. "최초 화면"이 별도 씬인지 같은 씬의 첫 패널인지는 프로젝트마다 다르므로, 이 컴포넌트는 언제 타임아웃됐는지만 감지하고 실제 복귀 로직은 인스펙터에 노출된 `UnityEvent`(`On Inactivity Timeout`)에 프로젝트가 원하는 메서드를 연결해 구현하도록 함(코드 작성 없이 인스펙터 연결만으로도 사용 가능). 입력 감지는 특정 UI 위에 전체 화면 캐처를 깔아 다른 UI의 입력을 가로채는 부작용을 피하기 위해 New Input System의 전역 이벤트(`InputSystem.onEvent`)를 사용하여, 화면 어디를 눌러도(마우스/터치/키보드) 활동으로 인식함. 타임아웃 이후에는 다음 입력이 들어올 때까지 다시 발동하지 않아 키오스크의 대기(어트랙트) 화면 패턴에 맞음. `Settings.json`에 `useInactivityTimer`(bool) 필드를 추가했으며, 기존에 미사용 상태로 남아있던 `resetTime`(초)을 타임아웃 시간으로 재사용함. `useInactivityTimer`가 `false`이거나 `resetTime`이 0 이하이면 비활성화되어 기존 프로젝트에는 동작 변화가 없음. 같은 그룹으로 예약돼 있던 `warningTime`/`fadeTime`은 이번 범위에 포함하지 않고 향후 경고 단계·전환 연출 기능을 위해 남겨둠. 긴 영상 재생처럼 입력이 없어도 사용자가 실제로는 콘텐츠를 보고 있는 구간까지 타임아웃으로 처리하면 안 되므로, `Pause()`/`Resume()`을 추가해 카운트를 일시 중지할 수 있게 함. `Resume()`은 정지 중 흐른 실제 시간을 그대로 반영하는 대신(재생이 끝나자마자 곧바로 타임아웃되는 것을 막기 위해) 재개 시점을 새 활동으로 취급해 `ResetTimer()`로 카운트를 처음부터 다시 시작함. 설정 로드가 끝나면 타이머 활성 여부와(활성 시) 몇 초로 설정됐는지를 정보 로그로 남겨, 프로그램 시작 시 콘솔만으로 실제 적용된 타임아웃 값을 바로 확인할 수 있게 함. PR 리뷰 반영: (1) `AppSettingsProvider` 미주입 시 `_logger`도 함께 `null`이라 에러 로그 없이 조용히 중단되던 문제를, 다른 매니저와 동일하게 `_logger == null`일 때 `Debug.LogError` 폴백을 추가해 해결함. (2) `onInactivityTimeout`이 `private`이라 코드에서 리스너를 등록할 수 없던 것을, `public UnityEvent OnInactivityTimeout => onInactivityTimeout;` 프로퍼티로 노출함 — 특히 이 컴포넌트를 씬에 미리 배치하지 않고 VContainer로 런타임에 생성해 쓰는 경우 인스펙터에 참조를 미리 꽂아둘 수 없으므로, 이 코드 경로가 유일한 연결 수단이 됨. (3) 리플렉션 없이 외부에서 상태를 확인할 수 있도록 읽기 전용 `IsEnabled`/`IsPaused` 프로퍼티를 추가함. 테스트(`InactivityTimerTests`)도 `onInactivityTimeout` 리플렉션 대신 `OnInactivityTimeout` 프로퍼티를 사용하도록 정리하고, 새로 추가된 미주입 폴백 로그를 각 테스트 코루틴에서 `LogAssert.Expect`로 기대하도록 보강함.

## [26.8.24] - 2026-08-24

### Added
- **프로그램 시작 시 서버 시작 로그 전송(`ApiManagerBase`) 추가:** `Runtime/Network/ApiManagerBase.cs`를 신설하고 `RootLifetimeScope.ConfigureOptionalComponents`에 씬 존재 기반으로 자동 등록함(기존 선택 매니저와 동일한 `RegisterIfPresentInScene` 패턴, 파생 클래스도 다형적으로 탐지됨). `Settings.json`에 `apiUrl` 필드를 추가했으며, 이 값은 `idx_content_device`/`uid` 등 콘텐츠별 쿼리 파라미터가 서버에서 이미 발급되어 `message=` 까지 포함된 URL 형태이므로, 클라이언트는 상태 메시지("Program started"/"Program restarted")만 이어붙여 GET 요청을 보냄. 날짜·시간은 서버가 수신 시점에 자동 기록하므로 클라이언트에서는 보내지 않음. 같은 날 재실행 시 "재시작"으로 구분하기 위해 마지막 전송 성공 날짜를 `PlayerPrefs`에 저장(전송 실패 시에는 갱신하지 않아, 다음 재실행에도 여전히 "시작"으로 기록됨). `apiUrl`이 비어 있으면 아무 동작도 하지 않으며, `Application.internetReachability`로 네트워크 자체가 연결되어 있지 않은 경우(전시장에 인터넷이 아예 없는 콘텐츠) 요청을 시도하지 않고 즉시 포기해 30초(3초 x 10회)를 허비하지 않도록 함. 네트워크는 연결돼 있으나 전송이 실패하는 경우에는 3초 간격으로 최대 10회까지 재시도하고 그래도 실패하면 로그만 남기고 포기하여 전시/키오스크 환경에서 네트워크 장애로 앱 실행이 막히는 일이 없도록 함. 에디터/디벨롭 빌드에서는 매 플레이·테스트마다 서버로 로그가 나가 실제 운영 로그를 오염시키지 않도록 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`로 실제 전송을 생략하고, 대신 무엇을 보냈을지를 동일한 ZLogger 패턴으로 콘솔에 남김. 콘텐츠마다 시작 로그 외에 서로 다른 API를 추가로 호출해야 하는 경우가 있어, 네트워크 확인·재시도·에디터 스킵 정책을 `protected UniTask<bool> SendGetRequestWithRetryAsync(url, logLabel, ct)`로 분리해 `ApiManagerBase`를 상속한 프로젝트별 클래스에서 재사용할 수 있게 함(`Start()`도 virtual이라 override 후 `base.Start()`로 시작 로그를 유지한 채 추가 호출을 덧붙일 수 있음).

- **`JsonLoader`에 동기 `Load<T>`/`Save<T>` 추가:** 비동기 컨텍스트를 쓸 수 없는 초기화 극초반이나 에디터 툴링 코드를 위한 동기 버전. 원격 경로(WebGL/Android URL 기반 StreamingAssets)에서는 동기 I/O 자체가 불가능하므로 `IsRemotePath`로 막고 에러 로그만 남긴 뒤 기본값을 반환함. 그 외 동작(파일 없음 경고, 파싱 실패 시 폴백)은 기존 `LoadAsync`/`SaveAsync`와 동일하게 유지.
- **`UIManager`의 `ButtonSetting.buttonSound` 사운드 연동:** 버튼 클릭 시 `buttonSound`가 비어 있지 않고 `SoundManager`가 씬에 존재하면 `SoundManager.PlaySFX(key)`를 자동 호출하도록 연결함. `SoundManager`는 선택 매니저라 씬에 없을 수 있으므로, `UIManager.Construct`에서 `IObjectResolver.ResolveOrDefault<SoundManager>()`로 조회함(예외 없이 null이 되고, 클릭 시 사운드 재생만 건너뜀). VContainer의 요청/필수 의존성 목록(`ILogger`, `VideoManager`, `AppSettingsProvider`)에 새 필수 파라미터를 추가하는 대신, 컨테이너 자체(`IObjectResolver`)를 주입받아 선택적으로 조회하는 방식이라 `RootLifetimeScope`는 수정할 필요가 없었음.
- **`AppSettingsProvider.GetAsync`의 스레드 안전성 보강:** `_isLoadStarted` 확인과 `_loadTask` 생성이 원자적이지 않아, 서로 다른 스레드에서 동시에 최초 호출이 들어오면 둘 다 확인을 통과해 `LoadAsync`를 중복 시작할 수 있었음. 동기적인 확인·대입 구간만 `lock`으로 묶어 해결함(`await`는 `lock` 블록 안에 쓸 수 없으므로 대기는 잠금 밖에서 수행).
- **에디터/테스트 모드 안전 파괴 유틸리티(`DestroyUtil.SafeDestroy`) 추가:** `Runtime/Utils/DestroyUtil.cs` 신설. `Application.isPlaying` 여부에 따라 `Object.Destroy`/`Object.DestroyImmediate`를 분기해, EditMode 테스트나 에디터 툴링에서 `Destroy`를 호출할 때 발생하던 "Destroy can only be called from the main thread or from Play mode" 경고(및 실제로는 파괴되지 않는 문제)를 방지함. `UIManager.ClearSpriteCache`/`SoundManager.ClearCache`/`VideoManager.ReleaseOrphanedRenderTextures` 등 캐시 해제 경로의 `Destroy` 호출을 모두 교체함.
- **테스트 보강:** `JsonLoaderTests.cs` 신설(동기 Load/Save 정상 동작, 파일 없음, 원격 경로 방어 4건). `UIManagerTests`에 버튼 클릭 시 `buttonSound`가 `SoundManager.PlaySFX`로 실제 전달되는지(로드 실패 로그로 간접 검증), `SoundManager`가 없어도 예외가 나지 않는지 2건 추가. 테스트 어셈블리(`Wonjeong.Template.Tests.asmdef`)가 ZLogger 콘솔 로거를 직접 구성할 수 있도록 `ZLogger.Unity` 참조와 `Microsoft.Extensions.Logging(.Abstractions)`/`ZLogger` 사전컴파일 DLL 참조를 추가함.

### Fixed
- **`SoundManager`의 사운드 로드 실패 처리가 실제로는 도달 불가능했던 문제 수정:** `ExecuteDownloadAsync`가 `www.SendWebRequest().ToUniTask()`의 결과가 실패이면 `www.result` 값을 확인하는 대신 `UnityWebRequestException`을 던지는데, 기존 코드는 이를 잡지 않고 `www.result != Success`만 검사했음 — 즉 의도했던 "실패 시 에러 로그를 남기고 null 반환" 경로가 한 번도 실행되지 못하고, 대신 처리되지 않은 예외가 fire-and-forget 경로에서 콘솔에 그대로 노출되고 있었음(버튼 사운드 연동 테스트를 작성하며 발견함). `UnityWebRequestException`을 잡아 동일한 로그로 남기도록 수정.
- **`UIManagerTests`에 남아있던 오래된 한국어 로그 정규식 수정:** 이전 변경(런타임 로그 영어 통일)으로 `UIManager`의 의존성 누락 메시지가 영어로 바뀌었는데, 회귀 테스트의 `LogAssert.Expect` 정규식(`"의존성이 주입되지 않았습니다"`)이 갱신되지 않아 실패하고 있었음. `"Dependencies were not injected"`로 수정.

### Changed
- **런타임 로그 메시지를 전부 영어로 통일:** `ApiManagerBase`, `GameManagerBase`, `GameCloser`, `UIManager`, `SoundManager`, `FadeManager`의 `ZLog*`/`Debug.Log*` 출력 문자열을 영어로 변경함. 템플릿은 여러 콘텐츠/클라이언트가 소비하므로 콘솔·파일 로그가 국제적으로 읽힐 수 있어야 하기 때문. 코드 주석(XML 문서 포함)은 기존과 동일하게 한국어를 유지함.

### Fixed
- **`ApiManagerBase` 코드 리뷰 반영:** (1) 재시도 대기(`UniTask.Delay`)가 `Time.timeScale`에 종속돼 있어 일시정지 중 재시도 루프가 무한정 멈추던 문제를 `DelayType.UnscaledDeltaTime`으로 수정(FadeManager에서 겪었던 동일한 종류의 소프트락). (2) `FadeManager`/`SoundManager`/`UIManager`/`VideoManager`와 달리 파괴 방지 로직이 없어 씬 리로드마다 시작 로그가 실제로 중복 전송되던 문제를 `Awake()`의 `DontDestroyOnLoad`로 수정. (3) `AppSettingsProvider` 미주입 시 예외가 완전히 무음으로 삼켜지던 문제를 다른 매니저와 동일한 null 체크·폴백 로그 패턴으로 수정. (4) `#pragma warning disable/restore CS1998`이 메서드 전체를 감싸 향후 다른 실수까지 가려질 수 있던 범위를 에디터/디벨롭 빌드 컴파일 변형에만 한정. (5) 재사용 가능한 재시도 로직(`SendGetRequestWithRetryAsync`)을 시작 로그 기능과 분리해 `Runtime/Network/ApiRetryUtil.cs`(정적 유틸리티)로 추출 — 시작 로그를 원치 않는 소비자도 `ApiManagerBase`를 상속하지 않고 바로 호출 가능. (6) `GameManagerBase<T>`와의 일관성을 위해 `ApiManagerBase`를 `abstract`로 변경(프로젝트별 파생 클래스를 씬에 배치하는 것이 정식 사용법). 이에 따라 `Template_Dev`에도 `GameManager`와 동일한 패턴으로 빈 파생 클래스 `ApiManager : ApiManagerBase`를 추가하고, 씬에서 구체 클래스가 없는 `ApiManagerBase`를 직접 참조하던 컴포넌트를 새 클래스로 교체함.

## [26.8.21] - 2026-08-21

### Added
- **`Settings.json`을 통한 목표 프레임 레이트(`targetFrameRate`) 설정 추가:** `Settings` 클래스에 `targetFrameRate` 필드를 추가하고, `GameManagerBase.LoadSettingsAsync`에서 설정 로드가 완료된 시점에 `ApplyFrameRateSettings`(virtual)를 호출해 적용함. 값이 0 이하(미설정)면 아무것도 바꾸지 않아 기존 `QualitySettings` 기본값(품질 레벨별 `vSyncCount`)을 그대로 따르므로 기존 프로젝트에는 동작 변화가 없음. 값을 지정하면 `QualitySettings.vSyncCount = 0`으로 끈 뒤 `Application.targetFrameRate`를 적용함(`vSyncCount`가 켜진 상태로는 `targetFrameRate`가 무시되고 디스플레이 주사율에 종속되기 때문). 전시/키오스크처럼 장시간 구동되는 환경에서 디스플레이 주사율을 넘는 프레임을 그리며 낭비되는 GPU 자원·발열·전력 소모를 줄이는 것이 목적이며, 프로젝트별로 다른 정책이 필요하면 `ApplyFrameRateSettings`를 override하여 재정의할 수 있음.

## [26.7.24] - 2026-07-24

### Added
- **로그 타입별 스택 트레이스 정책(`LogStackTraceConfig`) 전역 적용:** 부팅 시(`RuntimeInitializeOnLoadMethod`) `Application.SetStackTraceLogType`을 로그 타입별로 설정하여, 일반 로그(Info/`LogType.Log`)는 스택 트레이스를 제거(메시지 자체는 콘솔·player.log·파일 로그에 그대로 유지)하고 Warning/Error/Exception/Assert는 스택 트레이스(`ScriptOnly`)를 유지함. 정보성 로그(플레이 기록 등)에 붙던 노이즈 스택을 없애고, 로그마다 발생하던 스택 캡처 비용(CPU 스택 워크 + GC 할당)을 일반 로그에서 제거해 로그가 잦거나 WebGL/모바일 타깃일 때의 프레임 히칭 요인을 줄임. 모든 프로젝트에서 수동 호출 없이 자동 적용되며, 특정 프로젝트가 다른 정책이 필요하면 이후 `SetStackTraceLogType`을 다시 호출해 덮어쓸 수 있음.
- **패키지 일괄 업데이터(`PackageUpdater`) 추가:** 에디터 메뉴 `Tools > Update All Packages`로 프로젝트에 설치된 UPM 패키지를 한 번에 최신화함. Git 패키지는 동일 URL로 재추가해 최신 커밋으로 갱신하고, 레지스트리 패키지는 현재 에디터와 호환되는 최신 버전(`versions.latestCompatible`)으로만 올림(`versions.latest`는 호환성을 무시해 Unity 6 전용 버전까지 끌어오므로 사용하지 않음). BuiltIn/Embedded 패키지는 제외. 이를 담기 위해 Editor 전용 어셈블리 `Wonjeong.Template.Editor`(`Editor/` 폴더)를 신설함.

### Changed
- **ZLogger Unity 콘솔 출력의 수동 스택(`PrettyStacktrace`) 비활성화:** ZLogger의 Unity 콘솔 프로바이더는 스택을 자체적으로 캡처해 메시지에 붙이며, 그 여부를 `RuntimeInitializeLoadType.SubsystemRegistration`(가장 이른 RuntimeInitialize 단계) 시점에 한 번 캐싱해 결정함. 이 때문에 `LogStackTraceConfig`의 실행 시점을 아무리 앞당겨도 ZLogger의 캐싱을 앞지를 수 없어, ZLog* 일반 로그에는 스택 제거가 반영되지 않았음. `RootLifetimeScope.ConfigureLogging`에서 `ZLoggerUnityDebugOptions.PrettyStacktrace = false`로 ZLogger의 수동 스택을 끄고 스택 정책을 Unity 네이티브 `SetStackTraceLogType`에 위임하여 해결함(Unity는 이 값을 런타임에 매 로그마다 읽으므로 캐싱 타이밍과 무관). 결과적으로 `ZLogInformation`은 스택 없이 메시지만, `ZLogWarning`/`ZLogError`는 실제 호출부까지의 스택을 출력함. Warning/Error의 스택은 ZLogger의 정리된 형식 대신 Unity 네이티브 형식으로 표시되며 상단에 ZLogger 프레임이 일부 포함되지만, 실제 호출 라인은 그대로 추적·하이퍼링크됨.

## [26.7.23] - 2026-07-22

### Added
- **`RootLifetimeScope` 공통 씬 컴포넌트 등록 일원화:** 모든 프로젝트에 공통으로 포함되는 `SystemCanvas`, `GameCloser`를 루트 스코프의 `ConfigureCoreComponents`(virtual)에서 등록. 프로젝트별 파생 스코프마다 반복하던 등록 코드를 제거함. 예외적으로 이 컴포넌트들을 쓰지 않는 프로젝트는 해당 메서드를 override하여 제외 가능.
- **선택 매니저 씬 존재 기반 자동 등록:** `FadeManager`/`UIManager`/`SoundManager`/`VideoManager`/`ArduinoManager`는 프로젝트에 따라 쓰거나 안 쓰므로, 스코프가 속한 씬에 존재할 때만 자동 등록하는 `RegisterIfPresentInScene<T>`를 도입(`ConfigureOptionalComponents`). "씬에 배치하는 행위 = 사용 선언"이 되어, 파생 스코프에서 등록을 누락해 발생하던 미주입 NRE와 씬에 없는 컴포넌트를 등록해 컨테이너 빌드가 실패하는 문제를 모두 방지함. 존재 검사는 전역 검색(`FindAnyObjectByType`)이 아닌 스코프 씬의 루트만 대상으로 하여 `RegisterComponentInHierarchy`의 탐색 범위와 일치시킴.
  - **주의:** `UIManager`는 `VideoManager`를 필수 주입받으므로, `UIManager`를 배치하는 씬에는 `VideoManager`도 함께 배치해야 함.

## [26.7.22] - 2026-07-22

> **⚠️ 설치 요구사항 추가:** 본 버전부터 **DOTween**(에셋스토어)이 필수 의존성입니다. DOTween은 UPM/Git 배포가 없어 `package.json`의 `dependencies`로 선언할 수 없으므로, 소비 프로젝트에서 아래 절차를 수동으로 수행해야 합니다.
> 1. 에셋스토어에서 DOTween 임포트 후 셋업 (`Tools > Demigiant > DOTween Utility Panel`)
> 2. 동일 패널에서 **Create ASMDEF** 실행 (`DOTween.Modules` 어셈블리 생성)
> 3. `Player Settings > Scripting Define Symbols`에 `UNITASK_DOTWEEN_SUPPORT` 추가

### Added
- **DOTween 도입:** 연출(트윈) 라이브러리로 DOTween을 채택. `Wonjeong.Template.asmdef`에 `DOTween.Modules`, `UniTask.DOTween` 참조를 추가하여 패키지 코드에서 트윈과 UniTask 연동(`ToUniTask`, `CancellationToken` 전파)을 사용할 수 있게 함. 이후 신규 연출은 `DOxxx(...).SetUpdate(true).ToUniTask(cancellationToken: ct)` 패턴을 표준으로 함.

### Changed
- **`FadeManager` 페이드 로직을 DOTween 트윈으로 전환:** `unscaledDeltaTime` 수동 누적 + `Mathf.Lerp` 루프를 `CanvasGroup.DOFade(...)`로 대체. timeScale=0 소프트락 방지는 `SetUpdate(true)`(독립 업데이트)가 담당하며, `TweenCancelBehaviour.KillAndCancelAwait`로 취소 시 `OperationCanceledException`을 던지게 하여 기존 취소 처리 흐름(페이드아웃 취소 시 입력 차단 해제 등)을 그대로 유지함. 이징은 기존 동작과 동일한 `Ease.Linear`. 기존 회귀 테스트(`FadeManagerTests` — timeScale=0 완료 보장 3건) 통과 확인.
- **`SoundManager` BGM 페이드아웃을 DOTween 트윈으로 전환:** 동일한 방식으로 `AudioSource.DOFade(0f, duration).SetUpdate(true)` 적용. CTS 생성/파기 및 취소 처리 구조는 변경 없음.
- **`FadeManager`의 불필요한 `CanvasScaler` 제거:** 페이드 캔버스에 기준 해상도 1920x1080의 `CanvasScaler`를 붙이고 있었으나, 페이드 이미지는 풀스트레치 앵커(0~1)로 캔버스 전체를 따라가므로 스케일러 유무와 무관하게 모든 해상도에서 화면 전체를 덮음. "특정 해상도 전용"이라는 오해를 부르는 무의미한 설정이므로 제거하고 의도를 주석으로 명시함.

## [26.7.21] - 2026-07-21

> **⚠️ Breaking:** `Settings.json`의 `fontMap` 스키마가 `fonts` 배열로 변경되었습니다. 기존 프로젝트는 아래 "마이그레이션" 항목을 참고하여 JSON을 수정해야 폰트가 적용됩니다.

### Added
- **패키지 메타데이터 정비:** `package.json`에 `dependencies`(`com.unity.addressables`, `com.unity.inputsystem`), `license`, `keywords`, 상세 `description`을 추가. 기존에는 의존성 선언이 전혀 없어 새 프로젝트에 설치 시 컴파일 에러가 발생했음.
- **`README.md` 추가:** 설치 절차(OpenUPM 스코프 레지스트리 → 서드파티 5종 → 본 패키지), 구조 개요, `Settings.json` 스키마 예시, WebGL 제약 표를 문서화. VContainer/UniTask/ZLogger/R3/MessagePipe는 스코프 레지스트리 없이는 해석에 실패하므로 `dependencies`에 넣지 않고 수동 설치 절차로 분리함.
- **`LICENSE.md` 추가:** MIT 라이선스 및 내장 서드파티(RuntimeInspector, Reporter) 고지.
- **`AppSettingsProvider` 추가:** `Settings.json` 로드를 일원화하는 싱글톤 제공자. `RootLifetimeScope`에 등록되며, 공유 소스로 `Task<Settings>`를 사용해 여러 소비자가 같은 프레임에 동시 요청해도 안전하게 결과를 공유함. 호출자별 취소 토큰은 `AttachExternalCancellation`으로 격리하여 한 소비자의 취소가 다른 소비자에게 전파되지 않음.
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