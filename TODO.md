# TODO

<!-- Template 패키지에서 고쳐야 할 것. 다른 Unity 프로젝트에서 작업하다가 Template의 문제나 개선점을 발견하면 "할 일"에 적는다. -->
<!-- 형식: - [ ] 할 일 — 발견일: YYYY-MM-DD. 공개 저장소이므로 프로젝트 이름 대신 증상과 재현 조건을 일반적으로 적는다. -->
<!-- 고칠 때는 Template의 브랜치 → PR → 머지 흐름을 따르고, 끝나면 [x]로 바꾸고 완료일과 PR 번호를 붙여 "완료"로 옮긴다. -->

## 진행 중

## 할 일


## 완료

- [x] `ArduinoManager` 가 포트를 열 때 리셋되는 보드(Uno·Nano 계열)에 연결하지 못하던 것(핸드셰이크 0.5초 대기 < 부팅 약 1.7초). 실제 장치로 확인 — 완료: 2026-11-10
- [x] `VideoManager` 가 프로젝트가 연결한 RenderTexture까지 파괴하던 것 — 완료: 2026-11-10
- [x] `Reporter.Update` 가 화면을 닫아 둔 동안에도 매 프레임 FPS 문자열을 만들던 것 — 완료: 2026-11-10
- [x] 코딩 스킬 기준 패키지 점검 결과 정리: SoundManager 페이드 취소 NRE, 외부 API 로그가 실제 호출을 늦춤, ArduinoManager 재연결 간격·수동 해제 뒤 재연결, UIManager 버튼 효과음 캐시·Graphic 충돌·raycastTarget, 조용한 실패 로그, ProfilerMarker, 필드 순서·summary·테스트 코드 규칙. JsonLoaderTests 간헐 실패(File.Replace 잠금)는 교체 재시도로 고침 — 완료: 2026-11-10 (#74)
- [x] 감시가 다시 켠 실행인지·이유를 알려 주는 공개 API(`AutoRestartWatchdog.WasRestarted`·`RestartReason`) 추가. 환경 변수 이름을 복사해 읽지 않아도 됨 — 완료: 2026-11-10 (#73)
- [x] 감시가 앱을 지켜보는지 알려 주는 `IsWatching`·`WaitUntilWatchingAsync` 추가(감시가 앱 핸들을 연 뒤 이벤트로 신호), 지켜볼 때만 끝내는 재시작 요청 `TryRequestRestart` 추가 — 완료: 2026-11-10 (#73)
- [x] `ApiManagerBase` 가 종료 로그를 보낸 뒤 인수 없는 `Application.Quit()` 으로 끝내 종료 코드가 0으로 바뀌던 것. `QuitReason.ExitCode` 로 끝내도록 고침 — 완료: 2026-11-10 (#73)
- [x] `VideoManager.PrepareAndPlayAsync` 가 재생을 시작했는지 `UniTask<bool>` 로 돌려주도록 바꿈 — 완료: 2026-11-10 (#73)
- [x] 더 이상 빌드하지 않는 WebGL 코드 삭제(`ArduinoManager` 스텁, `GameCloser` 분기, 파일 로깅 조건, 주석). Android도 쓰는 URL 경로 처리는 유지 — 완료: 2026-11-04 (#72)
- [x] README를 코드와 대조해 정리: Api Compatibility Level 안내를 `.NET Framework`로, 빠져 있던 DOTween 설치 단계와 예약 종료·`apiUrl` 설명 추가, 없는 `Runtime/Logging` 폴더와 `AppSettings` 타입, 프로젝트 스코프 예시, 키를 생략했을 때의 동작 등 수정 — 완료: 2026-11-04
- [x] 어느 코드도 읽지 않아 `Settings.json` 에 적어도 효과가 없던 `Settings.warningTime`·`fadeTime` 제거 — 완료: 2026-11-04 (#71)
- [x] `targetFrameRate` 를 지정하면 수직 동기화가 꺼져 영상처럼 화면 전체가 움직이는 장면에 찢어짐이 보일 수 있고, 0 이하면 아무것도 바꾸지 않아 품질 레벨의 `vSyncCount` 가 0일 때 프레임 제한이 전혀 없던 것(빌드에서 1000프레임 이상). 0 이하면 수직 동기화를 켜도록 바꿈 — 완료: 2026-11-04 (#70)
- [x] Build 메뉴 항목(Windows 빌드 후 보관, 설정...)과 빌드·압축·복사 중에 뜨는 대화상자·진행 표시·복사 확인 창이 한국어였던 것. 메뉴를 Build > Windows Build / Build > Setting으로, 창 문구를 영어로 바꿈(Preferences 화면과 BuildInfo.txt는 그대로) — 완료: 2026-11-03 (#69)
- [x] 런타임 로그는 영어인데 에디터 메뉴(`[BuildMenu]`·`[PackageUpdater]`·`[ShutdownScheduleEditorBuilder]`) 콘솔 로그, Reporter 경고 한 줄, 작업 스케줄러 백업 스크립트 로그가 한국어로 섞여 있던 것. 영어로 통일(UI 문구와 BuildInfo.txt 항목 이름은 그대로) — 완료: 2026-11-03 (#68)
- [x] Build 메뉴가 BuildInfo.txt의 git 상태를 빌드가 끝난 뒤 읽어, 빌드 중 URP가 다시 쓴 URP 에셋의 셰이더 필터 값(`m_Prefilter…`)까지 "커밋하지 않은 변경"으로 적던 문제. 빌드 전에 읽어 둔 값을 쓰도록 바꾸고, 그 값은 한 번 커밋하면 다시 생기지 않는다는 안내를 README에 적음 — 완료: 2026-11-03 (#67)
- [x] README 자동 재시작 절에 재시작을 기다리는 동안 앱을 직접 켜면 어떻게 되는지가 없던 것. Force Single Instance 때문에 감시가 실행한 두 번째 앱이 바로 정상 종료되고 예전 감시도 끝나며, 끄면 앱이 두 개 뜬다는 설명을 추가(실제 빌드로 종료 코드 0 확인) — 완료: 2026-11-03 (#66)
- [x] 빌드를 Build Settings에서 손으로 만들고 버전 관리용 보관과 현장 PC로 옮길 복사를 따로 하던 것을 **Build > Windows 빌드 후 보관** 메뉴로 묶음(빌드 보관 폴더에 빌드마다 `<YYMMDD>-<N>` 폴더로 zip·`BuildInfo.txt`, 복사 폴더에 항상 최신 빌드 복사, 두 경로와 복사 방식은 Edit > Preferences > HuliacDev Build에서 PC별로 정함) — 완료: 2026-11-03 (#65)
- [x] `Tools > Update Stack Packages`가 UPM 패키지만 올려 ZLogger·R3의 UPM 연동 레이어와 NuGet 구현 DLL 버전이 어긋나고, 패키지를 하나씩 올리다 도메인 리로드로 순서가 끊겨 여러 번 실행해야 하던 문제. UPM은 한 번에 올리고 이어서 NuGet Updates 탭의 패키지도 올림(의존성이 내려가는 경우를 위해 반복 확인) — 완료: 2026-11-03 (#65)

- [x] 현장용 빌드가 크래시·강제 종료·응답 없음으로 끝나도 다시 켜지지 않던 문제. 앱이 시작할 때 숨은 PowerShell 감시 스크립트를 띄워 종료 코드가 0이 아니거나 응답 없음이 이어지면 다시 실행하는 `AutoRestartWatchdog` 추가(`Settings.json`의 `autoRestart`로 켬, 숨은 종료 버튼·예약 종료·창 닫기는 다시 켜지 않음) — 완료: 2026-11-03 (#64)

- [x] 전송 전 인터넷 확인이 `Application.internetReachability`(네트워크 어댑터 연결 여부)만 봐서, 공유기에는 연결됐지만 인터넷이 끊긴 경우(작업 표시줄 지구본 아이콘)에도 `Reachable`로 나오고 로그 전송이 재시도를 다 돈 뒤에야 실패하던 문제. Windows의 인터넷 판정(`GetNetworkConnectivityHint`)을 함께 보고, 판정이 틀리는 망을 위해 시작 로그가 판정과 다르게 서버에 닿으면 그 판정을 무시하도록 함. 시작 로그가 실패하면 idle·외부 API 로그를 5분에 한 번 거르지 않고 보내 다시 확인함 — 완료: 2026-10-30 (#63)

- [x] `SoundManager.PlaySFX`가 처음 트는 클립은 StreamingAssets에서 읽어 디코드한 뒤에야 재생되고 미리 불러오는 API가 없어 앱을 켠 뒤 첫 재생이 읽는 시간만큼 늦던 문제와, 읽는 중에 `StopSFX`를 불러도 읽기가 끝나면 그대로 재생되던 문제. 클립을 미리 읽어 캐시에 넣는 `PreloadAsync(key, token)`을 추가하고, `StopSFX` 호출 횟수로 로드를 기다리던 SFX 요청을 건너뛰도록 함 — 완료: 2026-10-19 (#62)

- [x] 여러 프로그램을 함께 켜면 창이 뜨는 순서가 보장되지 않아 나중에 뜬 다른 프로그램 창이 앱 화면을 가리던 문제. `Settings.json`의 `alwaysOnTop`으로 앱 창을 항상 위로 두는 옵션을 추가하고(기본 끔), `F` 키가 포커스 복구와 함께 끄고 켜도록 함 — 완료: 2026-10-16 (#61)
- [x] Settings.json을 읽지 못해 대체 설정에 `closeSetting`이 없으면, SystemCanvas 프리팹의 숨은 종료 버튼 기본값(화면 가운데, 흰색 불투명 150x150)이 최상단 캔버스에서 모든 화면의 가운데 터치를 가리고 연타하면 앱이 꺼지던 문제. 프리팹 기본값을 README 예시와 같은 좌상단·투명으로 바꿈 — 완료: 2026-10-12 (#60)
- [x] `JsonLoader.TryLoadAsync`(그리고 `LoadAsync`)가 로컬 파일이 없으면 취소 토큰을 확인하지 않고 기본값을 돌려주던 문제. 메서드 시작에서 `cancellationToken.ThrowIfCancellationRequested()`로 확인하도록 수정 — 완료: 2026-10-12 (#60)
- [x] `ApiRetryUtil.GetTextWithRetryAsync`가 요청에 시간 제한이 없어 응답하지 않는 서버를 무기한 기다리고, 성공할 때마다 Information 로그를 남기던 문제. 시도별 제한 시간 `attemptTimeoutSeconds`(기본 10초)와 성공 로그 수준 `successLogLevel`을 추가함(`SendGetRequestWithRetryAsync`는 중복 로그 우려로 그대로 둠) — 완료: 2026-10-12 (#60)
- [x] `WindowFocusRestorer`를 꺼 둔 상태에서도 "Will bring the window back" 로그가 남고 켜짐·꺼짐을 화면에서 알 수 없던 문제. 꺼진 상태의 로그 문구를 따로 두고, 토글하면 화면 위쪽에 상태를 2초 동안 표시함(`StatusMessageOverlay`) — 완료: 2026-10-12 (#60)
- [x] (추정) `WindowFocusRestorer`가 응답 없는 포그라운드 창의 스레드에 `AttachThreadInput`을 붙이면 앱 메인 스레드도 함께 멈출 수 있던 문제. 붙이기 전에 `IsHungAppWindow`로 확인해 응답 없는 창이면 붙이지 않음(현장 재현은 못 함) — 완료: 2026-10-12 (#60)
- [x] `RootLifetimeScope.ConfigureMessagePipe`가 `options`를 파생 클래스에 넘기지 않아 프로젝트 브로커를 더하려면 등록 목록 전체를 복사해야 했던 문제. 템플릿 브로커 등록 뒤 호출하는 훅 `ConfigureProjectMessageBrokers(IContainerBuilder, MessagePipeOptions)`를 추가함 — 완료: 2026-10-12 (#60)
- [x] `JsonLoader.LoadAsync`가 파일 없음·형식 오류에도 `new T()`를 돌려줘, `Settings.json`이 깨지면 비활동 복귀·효과음이 조용히 꺼지고 일부러 끈 것과 구별되지 않던 문제. 읽기 결과를 `(bool isSuccess, T data)`로 돌려주는 `TryLoadAsync`를 추가하고, `AppSettingsProvider`가 실패 시 오류 로그와 비활동 타이머(90초)를 켠 대체 설정을 쓰도록 함 — 완료: 2026-10-10 (#59)
- [x] `JsonLoader.SaveAsync`가 대상 파일을 바로 덮어써 저장 도중 끊기면 파일이 비거나 잘리던 문제. 임시 파일(`.tmp`)에 쓰고 `Flush(true)` 뒤 `File.Replace`로 교체하도록 수정(실패·취소 시 기존 파일 유지) — 완료: 2026-10-10 (#59)
- [x] `JsonLoader.LoadAsync`가 취소를 삼키고 `new T()`를 돌려줘 파괴된 호출부가 초기화를 이어가던 문제. `LoadAsync`·`TryLoadAsync`·`SaveAsync`가 `OperationCanceledException`을 다시 던지도록 수정 — 완료: 2026-10-10 (#59)
- [x] `JsonLoader.SaveAsync`/`Save`가 쓰기 실패를 호출자에게 알리지 않던 문제. `UniTask<bool>`/`bool`로 성공 여부를 반환하도록 수정 — 완료: 2026-10-10 (#59)
- [x] `Runtime/ThirdParty/LogViewer/Reporter/Test.meta`가 빈 폴더의 .meta라 git URL로 패키지를 받을 때마다 경고가 나던 문제. 쓰지 않는 폴더라 .meta 삭제 — 완료: 2026-10-10 (#59)
- [x] `GameCloser`의 "제한 시간 안에 N번 누르면 동작" 판정을 공개 순수 클래스 `ConsecutiveClickCounter`로 분리해 운영자용 숨은 버튼에 재사용할 수 있게 함(GameCloser 동작은 같음) — 완료: 2026-10-10 (#59)
- [x] `ApiRetryUtil`에 응답 본문을 돌려주는 `GetTextWithRetryAsync` 추가. 기본값으로 에디터·Development 빌드에서도 실제 전송하고, `skipInEditorAndDevelopmentBuild`로 건너뛸 수 있음 — 완료: 2026-10-10 (#59)
- [x] `TemplateInputActions`의 디버그 단축키(`D`·`I`·`M`·`F`)가 조합 없는 문자 키라 키보드형 바코드·QR 스캐너 입력과 겹치는 문제. 스캐너를 쓰는 특수한 경우에만 해당하므로 기본 바인딩은 유지하고, 해당 프로젝트는 `RootLifetimeScope.ConfigureInputBindings`를 override해 Ctrl 조합 등으로 바꾸기로 함(26.10.10-1에서 추가, README에 예시) — 완료: 2026-10-10 (코드 변경 없음)
- [x] `RootLifetimeScope.ConfigureWindowFocus`의 summary 주석이 "Settings.json의 useFocusRestore가 true일 때만 동작(기본값 꺼짐)"이라고 적혀 있으나, 실제로는 `useFocusRestore` 키가 없고 Windows 스탠드얼론 빌드에서 시작 시 켜지며 `F` 키(ToggleFocusRestore)로 끄고 켬. 주석을 보고 설정 파일에 `useFocusRestore`를 찾거나 기본값이 꺼져 있다고 오해할 수 있으니 실제 동작에 맞게 고칠 것 — 완료: 2026-10-10 (#58)
- [x] 패키지 전체 코드 주석을 실제 동작과 대조해 틀린 설명 정리: 삭제된 `GameManagerBase<T>` 참조, 실제와 다른 호출 경로·이벤트 발행 시점·로그 출력 방식, `PacketUtility`의 "박싱 없음" 설명(제네릭 Marshal API도 내부에서 박싱함), `DestroyUtil`의 Destroy 시점·오류 메시지, SoundManager·UIManager의 캐시·볼륨 설명, 테스트 주석 2건, README·package.json의 `ApiRetryUtil` "지수 백오프" 표기(실제는 고정 간격) — 완료: 2026-10-10 (#58)
- [x] Windows 전시 PC에서 알림·업데이트 창·다른 프로그램이 포커스를 가져가면, 키보드형 바코드·QR 스캐너 입력이 누가 화면을 터치할 때까지 앱에 들어오지 않음(Input System Background Behavior로는 해결 불가). 포커스를 잃으면 설정 시간 뒤 창을 다시 앞으로 가져오는 `WindowFocusRestorer` 추가(시작 시 켜짐, `F` 키로 끄고 켬, Settings.json은 타이밍만), 단축키를 프로젝트에서 바꾸는 `ConfigureInputBindings` 추가 — 완료: 2026-10-10 (#57)
- [x] Reporter 로그 뷰어의 `Clear()`가 `cachedString`을 비우지 않아, 줄마다 시각 접두사가 붙는 로그 환경에서 사전이 앱 실행 내내 커짐(로그를 많이 남기는 앱에서 하루 수십 MB, 사전 크기 변경 시 순간 끊김). `Clear()`에서 함께 비우도록 수정 — 완료: 2026-10-10 (#57)
- [x] `TemplateInputActions.cs` 생성 헤더가 Input System 1.14.2 기준으로 남아 있어, 패키지가 요구하는 1.19.0으로 로컬 경로 참조하면 열 때마다 파일이 다시 생성돼 변경으로 잡힘. 1.19.0 기준으로 다시 생성한 파일을 커밋 — 완료: 2026-10-09 (#56)
- [x] `VideoManager.WireRawImageAndRenderTexture`가 영상용 RenderTexture를 24비트 깊이 버퍼와 함께 만들어, 깊이 테스트가 없는 영상 표시에 VRAM만 낭비함(1920x1080 기준 장당 약 8MB 이상). 깊이 0으로 생성하도록 수정 — 완료: 2026-10-09 (#55)
