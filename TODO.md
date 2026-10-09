# TODO

<!-- Template 패키지에서 고쳐야 할 것. 다른 Unity 프로젝트에서 작업하다가 Template의 문제나 개선점을 발견하면 "할 일"에 적는다. -->
<!-- 형식: - [ ] 할 일 — 발견일: YYYY-MM-DD. 공개 저장소이므로 프로젝트 이름 대신 증상과 재현 조건을 일반적으로 적는다. -->
<!-- 고칠 때는 Template의 브랜치 → PR → 머지 흐름을 따르고, 끝나면 [x]로 바꾸고 완료일과 PR 번호를 붙여 "완료"로 옮긴다. -->

## 진행 중

## 할 일

- [ ] `JsonLoader.LoadAsync`가 취소(`OperationCanceledException`)도 삼키고 `new T()`를 돌려줘, 호출부가 오브젝트 파괴 뒤에도 기본값으로 초기화를 계속함. 재현: `GetCancellationTokenOnDestroy()` 토큰으로 로드하는 동안 씬을 다시 불러오면, 파괴된 컴포넌트가 이어서 이미 해제된 R3 Subject·StateMachine에 접근해 `ObjectDisposedException`이 나고 null 경고가 연쇄로 남음. 취소는 다시 던지거나(최소한 summary에 "취소돼도 new T()를 반환하므로 호출부가 토큰을 확인해야 함"을 명시) — 발견일: 2026-10-03
- [ ] `JsonLoader.SaveAsync`/`Save`가 쓰기 실패(읽기 전용 폴더, 권한 없음 등)를 로그로만 남기고 호출자에게 알리지 않아, 설정 저장 결과를 사용자에게 보여 주려면 저장 직후 파일을 다시 읽어 비교해야 함. 재현: 쓰기 권한이 없는 위치의 StreamingAssets JSON에 SaveAsync 후 호출부에서는 성공과 구분할 방법이 없음. 성공 여부(bool)를 돌려주거나 예외를 다시 던지는 오버로드 추가 — 발견일: 2026-10-07
- [ ] `TemplateInputActions`의 `System` 디버그 단축키(ToggleDebug `D`·ToggleInspector `I`·ToggleMouse `M`)가 조합 없는 문자 키 하나라, 키보드처럼 문자를 입력하는 USB 바코드·QR 스캐너로 영문 대문자가 든 값을 읽으면 디버그 UI·런타임 인스펙터·커서가 켜짐(바인딩이 Shift를 보지 않음). 빌드 조건 없이 모든 씬에서 켜져 있어 릴리스 빌드에도 해당. 재현: `D`가 든 값을 스캔하면 Reporter 컨트롤이 나타남. 스캐너는 Ctrl을 보내지 않으므로 `OneModifier`(Ctrl+키) 조합 바인딩으로 바꾸는 것을 검토(Shift+키는 스캐너 대문자와 겹쳐 안 됨). 바꾸면 소비 프로젝트가 런타임에 첫 번째 바인딩을 덮어쓰는 방식으로 이미 우회했을 수 있으니 CHANGELOG에 Breaking으로 알릴 것. 26.10.10-1부터 `RootLifetimeScope.ConfigureInputBindings`를 override해 프로젝트별로 바꿀 수 있고, 같은 방식으로 추가된 ToggleFocusRestore `F`도 같은 문제가 있음(기본 바인딩은 아직 단일 키) — 발견일: 2026-10-07
- [ ] `Runtime/ThirdParty/LogViewer/Reporter/Test.meta`가 빈 폴더의 .meta라 git에는 폴더가 없어, git URL로 패키지를 받는 프로젝트가 패키지를 가져올 때마다 ".meta exists but its folder ... can't be found, and has been created" 경고가 콘솔에 남음. 재현: lock 해시를 바꿔 새 커밋을 받거나 PackageCache를 지우고 프로젝트를 열면 경고 1건. 쓰지 않는 폴더면 .meta를 지우고, 필요한 폴더면 파일을 하나 둘 것 — 발견일: 2026-10-09
- [ ] `GameCloser` 의 "제한 시간 안에 N번 누르면 동작" 판정이 `OnClicked` 안에 묶여 있어, 같은 방식의 숨은 버튼(예: 운영자용 설정 화면 진입)을 만드는 프로젝트가 판정 로직을 따로 다시 짜게 됨. 연속 클릭 카운터를 순수 C# 클래스로 분리해 공개하고 GameCloser도 그것을 쓰면 재사용과 단위 테스트가 쉬워짐 — 발견일: 2026-10-09
- [ ] `ApiRetryUtil.SendGetRequestWithRetryAsync` 가 성공 여부(bool)만 돌려주고 응답 본문을 주지 않으며, 에디터·Development 빌드에서는 전송 자체를 건너뜀. 그래서 응답 본문을 해석해야 하는 API(예: 사용자 확인·진행도 조회)는 재시도·시간 초과 로직을 프로젝트에서 따로 구현하게 되고, 개발 중 실제 서버로 확인할 방법도 없음. 본문을 돌려주고 건너뛰기를 선택할 수 있는 오버로드 추가를 검토 — 발견일: 2026-10-09

## 완료

- [x] `RootLifetimeScope.ConfigureWindowFocus`의 summary 주석이 "Settings.json의 useFocusRestore가 true일 때만 동작(기본값 꺼짐)"이라고 적혀 있으나, 실제로는 `useFocusRestore` 키가 없고 Windows 스탠드얼론 빌드에서 시작 시 켜지며 `F` 키(ToggleFocusRestore)로 끄고 켬. 주석을 보고 설정 파일에 `useFocusRestore`를 찾거나 기본값이 꺼져 있다고 오해할 수 있으니 실제 동작에 맞게 고칠 것 — 완료: 2026-10-10 (#58)
- [x] 패키지 전체 코드 주석을 실제 동작과 대조해 틀린 설명 정리: 삭제된 `GameManagerBase<T>` 참조, 실제와 다른 호출 경로·이벤트 발행 시점·로그 출력 방식, `PacketUtility`의 "박싱 없음" 설명(제네릭 Marshal API도 내부에서 박싱함), `DestroyUtil`의 Destroy 시점·오류 메시지, SoundManager·UIManager의 캐시·볼륨 설명, 테스트 주석 2건, README·package.json의 `ApiRetryUtil` "지수 백오프" 표기(실제는 고정 간격) — 완료: 2026-10-10 (#58)
- [x] Windows 전시 PC에서 알림·업데이트 창·다른 프로그램이 포커스를 가져가면, 키보드형 바코드·QR 스캐너 입력이 누가 화면을 터치할 때까지 앱에 들어오지 않음(Input System Background Behavior로는 해결 불가). 포커스를 잃으면 설정 시간 뒤 창을 다시 앞으로 가져오는 `WindowFocusRestorer` 추가(시작 시 켜짐, `F` 키로 끄고 켬, Settings.json은 타이밍만), 단축키를 프로젝트에서 바꾸는 `ConfigureInputBindings` 추가 — 완료: 2026-10-10 (#57)
- [x] Reporter 로그 뷰어의 `Clear()`가 `cachedString`을 비우지 않아, 줄마다 시각 접두사가 붙는 로그 환경에서 사전이 앱 실행 내내 커짐(로그를 많이 남기는 앱에서 하루 수십 MB, 사전 크기 변경 시 순간 끊김). `Clear()`에서 함께 비우도록 수정 — 완료: 2026-10-10 (#57)
- [x] `TemplateInputActions.cs` 생성 헤더가 Input System 1.14.2 기준으로 남아 있어, 패키지가 요구하는 1.19.0으로 로컬 경로 참조하면 열 때마다 파일이 다시 생성돼 변경으로 잡힘. 1.19.0 기준으로 다시 생성한 파일을 커밋 — 완료: 2026-10-09 (#56)
- [x] `VideoManager.WireRawImageAndRenderTexture`가 영상용 RenderTexture를 24비트 깊이 버퍼와 함께 만들어, 깊이 테스트가 없는 영상 표시에 VRAM만 낭비함(1920x1080 기준 장당 약 8MB 이상). 깊이 0으로 생성하도록 수정 — 완료: 2026-10-09 (#55)
