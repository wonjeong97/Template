# TODO

<!-- Template 패키지에서 고쳐야 할 것. 다른 Unity 프로젝트에서 작업하다가 Template의 문제나 개선점을 발견하면 "할 일"에 적는다. -->
<!-- 형식: - [ ] 할 일 — 발견일: YYYY-MM-DD. 공개 저장소이므로 프로젝트 이름 대신 증상과 재현 조건을 일반적으로 적는다. -->
<!-- 고칠 때는 Template의 브랜치 → PR → 머지 흐름을 따르고, 끝나면 [x]로 바꾸고 완료일과 PR 번호를 붙여 "완료"로 옮긴다. -->

## 진행 중

## 할 일

## 완료

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
