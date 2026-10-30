# HANDOFF

Claude가 Antigravity(agy)와 리뷰 서브에이전트에 맡긴 검증 기록. 새 항목을 맨 위에 추가한다. 항목 형식은 공통 스킬 `handoff` 를 따른다.

---

### [2026-10-30 11:15] Claude → Antigravity + 서브에이전트 · 전송 전 인터넷 연결 확인 (fix/internet-connectivity-check)
- 요청: 작업 표시줄이 지구본 아이콘인데도 시작 확인이 Reachable로 나와, 인터넷이 없을 때 전송을 거르지 못하던 문제 고치기(시작 로그로 Windows 판정을 보정).
- 변경: `InternetConnectivity`(Windows `GetNetworkConnectivityHint`)와 `InternetAccessJudge`를 추가하고, `ApiRetryUtil` 전송 전 확인과 `NetworkStatusService`가 같은 판정을 씀. 시작 로그만 판정으로 거르지 않으며, 판정이 "인터넷 연결됨"이 아닐 때 시작 로그가 닿으면 판정을 무시함. `requireInternetAccess` 매개변수 추가. 테스트 8건.
- Claude 확인: Unity 프로젝트가 없는 패키지 저장소라 Unity 밖에서 확인함. csc(C# 10)로 Runtime 전체를 Windows 플레이어·Development·비Windows·에디터 조건으로, 테스트 어셈블리까지 컴파일해 에러 0. 판정기 테스트 8/8 통과(NUnit 직접 실행), 어댑터만 보던 예전 동작 사본에서는 4건 실패. 같은 P/Invoke 선언을 이 PC에서 호출해 InternetAccess(3)를 받음. Unity Editor 컴파일·PlayMode·콘솔은 확인하지 못함.
- agy: gemini-4-argon-high가 할당량 초과(429)라 gemini-3.8-flash-high로 넘김.
- agy (gemini-3.8-flash-high, 1묶음):
  - NetworkStatusService 이벤트·시작 로그·테스트 기대값 → 통과
- 서브에이전트 (1묶음):
  - 문서(CHANGELOG·README·package.json) → 지적: LAN 요청에 false가 필요한 조건을 좁게 적음, 테스트하지 않은 이유가 틀림, NetworkStatusService Breaking 누락, 부팅 직후 판정 전(Unknown)에 시작 로그가 닿으면 보정되지 않음 → 모두 고침
- 교차 검증 (agy gemini-3.8-flash-high + 서브에이전트, 3묶음):
  - 판정기 상태 전이·값 변환·테스트, P/Invoke 선언·실패 처리·정적 초기화, 전송 경로·매개변수 호환·Assume 조건 → 둘 다 통과
  - LAN 요청(`requireInternetAccess: false`)이 성공해도 판정 무시가 켜짐 → 서브에이전트만 지적(2묶음) → 시작 로그가 성공했을 때만 보고하도록 고침
  - 판정을 읽지 못한 이유가 ZLogger 파일 로그에 남지 않음 → 서브에이전트만 지적(낮음) → NetworkStatusService 시작 로그에 Warning으로 남김
  - 재검증(고친 부분 3가지) → 코드는 둘 다 통과. 문서에 보정되지 않는 조건 누락(에디터·Development, 보정 해제 뒤, 어댑터 미연결) → 서브에이전트만 지적(낮음) → 고침
- 남은 것: Windows 판정이 늘 틀리는 망에서 시작 로그가 실패하면 그 실행 동안 전송이 걸러짐(서브에이전트 지적, 나중에 다시 확인하는 경로를 둘지는 사용자 결정). `NetworkStatusService`를 빼고 등록하면 판정 무시가 늦게 풀릴 수 있음(낮음, 기록만). 판정이 Internet과 NoInternet을 오가는 망이면 첫 Internet에서 보정이 풀림(실제로 오가는지는 확인 못 함). 시작 뒤 처음 생긴 판정 읽기 실패는 Unity 로그에만 남음(낮음, 기록만).

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
