# HANDOFF

Claude가 Antigravity(agy)와 리뷰 서브에이전트에 맡긴 검증 기록. 새 항목을 맨 위에 추가한다. 항목 형식은 공통 스킬 `handoff` 를 따른다.

---

### [2026-11-03 17:20] Claude → Antigravity · BuildInfo의 git 상태를 빌드 전에 읽기 (fix/buildinfo-before-build)
- 요청: 빌드 뒤 URP 설정이 바뀌어 BuildInfo.txt에 매번 '커밋하지 않은 변경'이 들어감 → Template 수정과 프로젝트 URP 값 커밋 둘 다(사용자 결정).
- 변경: `BuildMenu` 가 커밋·브랜치·커밋하지 않은 변경을 빌드 전에 읽어 두는 `GitState` 로 바꿈(보관 폴더가 없으면 읽지 않음), BuildInfo 항목 이름에 "빌드 전". README에 URP `m_Prefilter…` 값은 한 번 커밋하면 다시 생기지 않는다는 안내. 26.11.3-3.
- Claude 확인: 검증 프로젝트 컴파일 오류 0. `GitState.Read`·`BuildInfoText` 를 git 저장소에 넘겨 커밋하지 않은 변경 목록이, 기본값으로 "알 수 없음"이 나오는 것을 확인. 사용 프로젝트에서 연속 빌드·Development 켬/끔 빌드가 모두 같은 URP 값을 쓰는 것을 확인.
- agy (gemini-4-argon-high, 1묶음): 빌드 전 읽기와 전달 경로·기존 흐름 유지 → 통과. `GitState` 기본값·문구·코딩 규칙 → 통과. CHANGELOG·README·버전 → 통과.

### [2026-11-03 16:05] Claude → Antigravity · README 자동 재시작: 대기 중 직접 켜기 (docs/autorestart-manual-start)
- 요청: 재시작을 기다리는 동안(최대 10분) 앱을 직접 켜면 나중에 또 켜지는지 문의 → 결과를 README에도 적기로 함(사용자 결정).
- 변경: README 자동 재시작 절에 한 줄 추가. 대기 중 직접 켜도 Force Single Instance 때문에 감시가 실행한 두 번째 앱이 바로 정상 종료되고 예전 감시도 끝남, 끄면 앱이 두 개 뜸. 코드 변경 없음(CHANGELOG·버전 그대로).
- Claude 확인: 검증 프로젝트 빌드(Force Single Instance 켬)를 실행 중일 때 같은 exe를 또 실행하면 0.1초 만에 종료 코드 0으로 끝남을 직접 확인.
- agy (gemini-4-argon-high, 1묶음): 설명과 감시 스크립트 코드 일치(대기 중 실행 여부 확인 안 함, 종료 코드 0이면 감시 종료, 직접 켠 앱은 새 감시) → 통과. 주변 문장과 모순·개인 정보 → 통과.

### [2026-11-03 12:22] Claude → Antigravity + 서브에이전트 · Build 메뉴·NuGet 업데이트 (feat/update-nuget-packages)
- 요청: Update Stack Packages가 NuGet Updates도 올리게, 에디터에 Build 메뉴를 만들어 빌드 뒤 버전 관리용 보관과 현장용 복사를 자동으로(경로는 팀원·PC마다 설정, "USB" 표현 없이, 복사 전에 묻되 다시 묻지 않음 체크박스, 보관은 날짜-번호 폴더, 복사는 비우고 최신만).
- 변경: `PackageUpdater`가 UPM을 `AddAndRemove` 한 번으로 올리고 NuGet Updates를 최대 3번 올린 뒤 다시 확인(리로드 뒤 `SessionState`로 이어서 실행, asmdef versionDefines로 NuGetForUnity 없으면 건너뜀). `BuildMenu`·`BuildMenuPreferences`·`BuildCopyPromptWindow` 추가.
- Claude 확인: Unity 컴파일 에러 0, PlayMode 151/151(검증 프로젝트를 ZLogger 2.6.0·NuGet 10.0.14로 올린 뒤). 검증 프로젝트에서 직접 시험: 메뉴 한 번으로 UPM·NuGet이 함께 올라가고 1차에서 내려간 의존성(Immutable·Hashing 8.0.0, Utf8StringInterpolation 1.3.1)이 다음 실행에서 최신으로 돌아옴, 보관 zip 207항목(`_DoNotShip` 제외), 같은 날 두 번 빌드하면 `-1`·`-2` 폴더, 보관 폴더 없음 → 3갈래 창, 복사 폴더 없음 → 건너뜀, 묻는 창이 답할 때까지 기다림·Unity 창 가운데, 다시 묻지 않음 → 항상 복사 저장, 설정 화면에서 매번 묻기로 되돌림, 복사 전에 옛 파일을 지움.
- 서브에이전트 (1묶음):
  - 설정 화면·묻는 창 → 통과(ShowModalUtility가 막는지는 직접 시험으로 확인함)
- 교차 검증 (agy gemini-4-argon-high + 서브에이전트, 2묶음):
  - PackageUpdater → agy는 5분 시간 제한으로 빈 결과. 서브에이전트만 지적: 마지막 업데이트 뒤 다시 확인하지 않아 잘못된 경고(낮음) → 고침
  - BuildMenu → 둘 다 지적: 묻는 창 위에 진행 표시줄이 남음, 실패 때 임시 zip이 남음, 새 zip 복사 전에 이전 zip을 지움, git 출력 앞 공백 잘림·한글 이스케이프 → 고침. agy만: 지우는 zip 패턴이 넓음, stderr 교착 가능성, Win32Exception 외 예외 → 고침. 서브에이전트만: IL2CPP `_ButDontShipItWithYourGame` 폴더 포함, 빌드 전 `Build/`를 비우지 않음, 보관 실패 때 복사도 건너뜀 → 고침
- 문서(서브에이전트, 1묶음) → 지적(낮음): 보관 폴더 "찾을 수 없음" 경우, 반복 횟수 설명, 빼는 폴더 범위, "정해져 있고 있으면" 표현 → 고침
- 재검증: agy(고친 BuildMenu·RunGit·NuGet 반복, 3묶음으로 나눠) → git 읽기를 끝까지 동기로 해 시간 제한이 듣지 않음, NuGetForUnity가 없을 때 쓰이지 않는 using → 고침, 그 밖 통과. 마지막 재검증(agy, 빈 폴더 정리·복사 폴더 비우기·git 시간 제한) → 통과, 정리 단계 예외가 원래 오류를 덮을 수 있음(낮음) → 고침. 서브에이전트(ExitGUI·문서) → 통과(붙여 넣은 경로의 큰따옴표, "N번째 보관" 문구는 고침).
- 남은 것: 같은 날 두 PC에서 동시에 빌드하면 드라이브 동기화 전이라 같은 번호 폴더를 만들 수 있음(낮음, 기록만). 실행 중에 Unity 패키지 해석과 NuGet 단계가 겹치는 경우는 코드로 확인하지 못함.

### [2026-11-03 11:12] Claude → Antigravity + 서브에이전트 · 비정상 종료 시 자동 재시작 (feat/auto-restart)
- 요청: 현장용 빌드가 크래시·강제 종료·응답 없음으로 끝나면 다시 실행하고, 숨은 종료 버튼·예약 종료·운영자가 끈 경우는 다시 켜지 않기. 켜고 끄기와 주요 값은 Settings.json으로.
- 변경: `AutoRestartWatchdog`가 시작할 때 숨은 PowerShell 감시 스크립트를 띄우고, 감시가 종료 코드(0 = 일부러 끔)와 `IsHungAppWindow`로 재시작을 정함. 켜자마자 끝나는 일이 이어지면 간격을 늘림. 설정 해석은 `AutoRestartPolicy`. `Settings.autoRestart` 추가. 테스트 5건.
- Claude 확인: Rider 빌드 에러 0, Unity 컴파일 에러 0, PlayMode 151/151(로컬 경로로 패키지를 참조하는 검증 프로젝트). 같은 프로젝트의 Windows 빌드로 직접 시험: `taskkill /F`(1)·크래시(0xC0000005)·응답 없음 10초는 다시 켜짐, 숨은 종료 버튼 10번·예약 종료(종료 명령을 `-a`로 바꿔 시험)·`taskkill`(WM_CLOSE)은 다시 켜지지 않고 감시도 끝남, 연속으로 일찍 끝날 때 5→10→20초, 다시 켜진 앱은 이유를 남기고 감시를 또 띄우지 않음. `EndTask` API(작업 관리자가 쓰는 것으로 알려짐)로 끄면 응답하는 앱은 종료 코드 0이라 다시 켜지지 않음.
- 서브에이전트 (2묶음):
  - 계산 클래스·테스트 → 지적(낮음): 기본값을 상수 자신과 비교, 인수 값 순서·param 선언을 확인하지 않음 → 숫자로 단언하고 서로 다른 값·`]$` 선언 형태로 확인하도록 고침
  - 문서(README·CHANGELOG·package.json) → 지적(낮음): 대기 시간 600초 상한이 문서·시작 로그와 다름, package.json 설명 목록 누락, 재시작 반복을 멈추는 방법이 없음 → 상한을 해석에서 자르고(테스트 1줄) 문서에 멈추는 방법·실행 정책 주의를 적음
- 교차 검증 (agy gemini-4-argon-high + 서브에이전트, 2묶음):
  - 감시 스크립트(종료 코드 분기·문법·간격 계산) → 둘 다 지적: Kill 실패 뒤 앱이 살아나 정상 종료해도 다시 켬 → Kill이 끝난 것을 확인했을 때만 표시하도록 고침. agy만 지적: 창 핸들을 읽다 앱이 끝나면 예외로 감시가 끝남, 따옴표 없는 공백 경로·탭에서 인수를 잘못 자름, 실행 시간 반올림, 대기 0이면 NaN → 모두 고침
  - 실행부(중복 실행·#if·설정 해석) → agy는 5분 시간 제한으로 빈 결과. 서브에이전트만 지적: 에디터에서 `_logDirectory` 미사용 경고(중간), 스레드 풀 직전 취소 때 플래그만 남음(낮음) → 둘 다 고침
  - 재검증(고친 부분, 2묶음으로 나눠 agy gemini-4-argon-high) → 둘 다 통과
- 남은 것: 작업 관리자 "작업 끝내기"가 응답하는 앱에 창 닫기를 보내는지(그러면 다시 켜지지 않음)는 작업 관리자가 관리자 권한 창이라 직접 시험하지 못함. 그룹 정책으로 PowerShell 스크립트를 막은 PC에서는 감시가 뜨지 않을 수 있음(문서에 확인 방법을 적음). IL2CPP 빌드의 `Process` API 동작은 시험하지 않음(Mono로 시험).

### [2026-10-30 11:15] Claude → Antigravity + 서브에이전트 · 전송 전 인터넷 연결 확인 (fix/internet-connectivity-check)
- 요청: 작업 표시줄이 지구본 아이콘인데도 시작 확인이 Reachable로 나와, 인터넷이 없을 때 전송을 거르지 못하던 문제 고치기(시작 로그로 Windows 판정을 보정).
- 변경: `InternetConnectivity`(Windows `GetNetworkConnectivityHint`)와 `InternetAccessJudge`를 추가하고, `ApiRetryUtil` 전송 전 확인과 `NetworkStatusService`가 같은 판정을 씀. 시작 로그만 판정으로 거르지 않으며, 판정이 "인터넷 연결됨"이 아닐 때 시작 로그가 닿으면 판정을 무시함. 보정하지 못한 채 막혀 있으면 idle·외부 API 로그 하나를 300초마다 한 번 거르지 않고 재시도 없이 보내 다시 확인함(사용자 요청으로 추가). `requireInternetAccess` 매개변수 추가. 테스트 11건.
- Claude 확인: Unity 프로젝트가 없는 패키지 저장소라 Unity 밖에서 확인함. csc(C# 10)로 Runtime 전체를 Windows 플레이어·Development·비Windows·에디터 조건으로, 테스트 어셈블리까지 컴파일해 에러 0. 판정기 테스트 11/11 통과(NUnit 직접 실행). 변이 사본에서 실패 확인: 어댑터만 보던 예전 동작 6건, 확인 전송 간격 확인 제거 1건, 막혀 있는지 확인 제거 2건. 같은 P/Invoke 선언을 이 PC에서 호출해 InternetAccess(3)를 받음. 로컬 경로로 패키지를 참조하는 검증 프로젝트의 복사본을 명령줄 Unity(2022.3.62f3)로 실행해 컴파일 에러 0, PlayMode 146/146(PR 리뷰 반영 뒤 다시 146/146). 사용자가 빌드로 인터넷 있음·실행 중 끊김과 복구(확인 전송 포함)·처음부터 없음·없이 종료(`skipping send`) 로그를 확인함.
- agy: gemini-4-argon-high가 할당량 초과(429)라 gemini-3.8-flash-high로 넘김.
- agy (gemini-3.8-flash-high, 2묶음):
  - NetworkStatusService 이벤트·시작 로그·테스트 기대값 → 통과
  - 확인 전송 문서(CHANGELOG·README·TODO) → 통과
- 서브에이전트 (1묶음):
  - 문서(CHANGELOG·README·package.json) → 지적: LAN 요청에 false가 필요한 조건을 좁게 적음, 테스트하지 않은 이유가 틀림, NetworkStatusService Breaking 누락, 부팅 직후 판정 전(Unknown)에 시작 로그가 닿으면 보정되지 않음 → 모두 고침
- 교차 검증 (agy gemini-3.8-flash-high + 서브에이전트, 3묶음):
  - 판정기 상태 전이·값 변환·테스트, P/Invoke 선언·실패 처리·정적 초기화, 전송 경로·매개변수 호환·Assume 조건 → 둘 다 통과
  - LAN 요청(`requireInternetAccess: false`)이 성공해도 판정 무시가 켜짐 → 서브에이전트만 지적(2묶음) → 시작 로그가 성공했을 때만 보고하도록 고침
  - 판정을 읽지 못한 이유가 ZLogger 파일 로그에 남지 않음 → 서브에이전트만 지적(낮음) → NetworkStatusService 시작 로그에 Warning으로 남김
  - 재검증(고친 부분 3가지) → 코드는 둘 다 통과. 문서에 보정되지 않는 조건 누락(에디터·Development, 보정 해제 뒤, 어댑터 미연결) → 서브에이전트만 지적(낮음) → 고침
  - 확인 전송(판정기 TryStartProbe, ApiManagerBase 연결, 테스트 3건) → 둘 다 통과. 시작 로그 주석·문서의 "다른 요청의 성공은 근거로 쓰지 않음"이 확인 전송과 어긋남 → 서브에이전트만 지적(낮음) → 범위를 apiUrl 로그가 아닌 요청으로 고침
  - PR 리뷰(Claude `/code-review` xhigh, 7건) 반영분: 판정 읽기에서 예상 못 한 예외가 DI 빌드까지 올라감 → 모든 예외를 Unknown으로 처리하도록 고침. `ApiRetryUtilTests`가 인터넷 없는 PC에서 판정 불가 → `requireInternetAccess: false`와 어댑터 가정으로 고침. CHANGELOG 굵은 머리에 클래스 이름 → 고침. 재검증 → 둘 다 통과
- 남은 것: 보정이 풀린 뒤 마지막 확인 전송에서 300초 안에 다시 막히면 간격이 지날 때까지 확인하지 않음(서브에이전트 지적, 판정이 자주 바뀌는 망에서 확인 전송이 늘지 않게 하려는 의도라 그대로 둠). 보낼 idle·외부 API 로그가 없으면 확인하지 않음. `NetworkStatusService`를 빼고 등록하면 판정 무시가 늦게 풀릴 수 있음(낮음, 기록만). 시작 뒤 처음 생긴 판정 읽기 실패는 Unity 로그에만 남음(낮음, 기록만). PR 리뷰에서 기록만 한 것: 캡티브 포털이 http 요청에 200을 돌려주면 보정이 켜짐(이 PR 전에도 거짓 성공은 같았음, 응답 내용 검사가 필요해 범위 밖), 0이 아닌 반환값 한 번에 그 실행 동안 판정을 읽지 않음(일시 오류가 있는지 근거 없음, 어댑터만 보는 쪽으로 떨어짐), 확인 전송 실패가 5분마다 Error로 남음(다른 전송 실패와 같은 수준).

### [2026-10-19 17:10] Claude → Antigravity + 서브에이전트 · SoundManager SFX 미리 불러오기·로딩 중 정지 (feat/sound-preload)
- 요청: TODO의 SoundManager 항목 처리(처음 트는 효과음의 재생 지연, 로드 중 `StopSFX` 뒤에도 재생됨).
- 변경: `SoundManager.PreloadAsync` 추가(설정 목록 대기, 공유 읽기는 호출자 토큰으로 취소하지 않음). `StopSFX` 호출 횟수로 로드를 기다리던 SFX 요청을 건너뜀. 테스트 4개.
- Claude 확인: Rider 빌드 에러 0, Unity 컴파일 에러 0, PlayMode 135/135(로컬 경로로 패키지를 참조하는 검증 프로젝트). 변이 3종(StopSFX 횟수 증가 제거, 재생 전 캐시 비우기, 읽기를 호출자 토큰으로 취소)에서 해당 테스트가 실패함을 확인.
- agy (gemini-4-argon-high, 1묶음):
  - StopSFX 전후 요청 구분·BGM 경로 유지·테스트 타당성 → 통과
  - 재검증: 리뷰 뒤 바뀐 부분(시작 시 취소 확인, 취소 테스트, 문서 문구) → 통과
  - 재검증: PR 리뷰(`/code-review`) 반영분(PlayBGM이 쓰지 않는 정지 횟수 인자를 넘기지 않게 정리, 테스트에 AudioListener를 붙여 "no audio listeners" 로그 제거) → 통과. PlayMode 135/135, SoundManagerTests의 해당 로그 0건
- 서브에이전트 (1묶음):
  - 문서·설정(CHANGELOG·README·package.json·TODO) → 지적: 목록을 기다리던 중 취소하면 읽기를 시작하지 않는데 문서는 늘 캐시에 들어가는 것처럼 읽힘, 테스트 설명 문구 → 고침
- 교차 검증 (agy gemini-4-argon-high + 서브에이전트, 1묶음):
  - 취소 처리·설정 대기 조건·테스트 2건 → 둘 다 통과
  - 끝나지 않는 대기 → 둘 다 지적(agy는 추정): 오브젝트가 꺼져 `Start`가 돌지 않으면 기본 토큰으로 부른 호출이 끝나지 않음 → summary·README에 파괴 토큰을 넘기라고 적음(구조는 유지)
  - 공유 읽기가 호출자 취소 뒤에도 캐시에 들어가는 경로 테스트 없음 → 서브에이전트만 지적 → 테스트 추가, 변이로 확인
- 남은 것: 사운드 목록을 기다리는 경로(DI + Settings.json)는 테스트 없음. 취소 테스트는 읽기가 1초 안에 끝난다고 가정하고 파일을 지움(기존 BGM 테스트와 같은 가정, 기록만). `HANDOFF.md`도 `TODO.md`처럼 패키지와 함께 소비 프로젝트에 들어감(기록만). 읽는 중 SoundManager가 파괴되면 예외 대신 false(앱 종료 때만, 기록만). `sounds[].key`가 null이면 설정 초기화가 예외로 끝나는 기존 경로는 JsonUtility가 문자열 필드를 null로 두지 않아 생기지 않는 것으로 판단(추정, 기록만).
