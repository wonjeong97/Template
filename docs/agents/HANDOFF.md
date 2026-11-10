# HANDOFF

Claude가 Antigravity(agy)와 리뷰 서브에이전트에 맡긴 검증 기록. 새 항목을 맨 위에 추가한다. 항목 형식은 공통 스킬 `handoff` 를 따른다.

---

### [2026-11-10 19:00] Claude → Antigravity + 서브에이전트 · 랜 장치용 UDP·TCP 클라이언트 추가 (feat/lan-device-clients)
- 요청: 요즘 아두이노를 랜선으로 연결하므로, 랜 아두이노를 쓴 두 현장 프로젝트의 통신 코드를 비교해 템플릿 클래스로 추가(둘 다).
- 변경: `UdpCommandClient`(명령 → 같은 문자열 응답 확인, 재전송, 순서대로 하나씩, Dispose 때 남은 명령 전송)와 `TcpLineClient`(접속 유지, 1초→30초 재접속, keepalive, 줄 단위 수신, 메인 스레드 발행, 끊긴 동안 Send false), 줄 나누기 `LineBuffer`(internal). 장치 로직은 빼고 통신만 일반화. 26.11.10-4. 테스트 24건(UDP 10, TCP 9, LineBuffer 5, 루프백 가짜 장치).
- Claude 확인: PlayMode 200/200(테스트로 `Disconnect` 뒤 `IsConnected`가 true로 남는 결함을 찾아 고침). 플레이어 스크립트 컴파일 성공(빌드 자체는 검증 프로젝트 경로가 260자를 넘어 NuGet DLL 복사에서 실패, 코드와 무관). 검증 프로젝트(Temp)의 파일이 정리돼 사라져 Template_Dev를 Library 없이 세션 임시 폴더로 다시 복사함.
- agy (gemini-4-argon-high):
  - UDP → Dispose 순서(결과 완료가 끝남 표시보다 먼저라 재진입), 보내기 실패 시도도 넘어가기에 셈, 테스트 공백(재전송 뒤 성공, 다른 문자열, 순서, 대상 유지), 송수신 로그 누락 → 모두 고침. 같은 명령 연속 시 늦은 응답 구별 불가 → 프로토콜 한계로 문서에 적음
  - TCP → 1차는 5분 제한으로 결과 없음, 범위를 줄여 재요청 → 이전 루프의 이벤트 순서 뒤바뀜·가짜 접속 알림(중간), 이전 루프가 새 접속의 보낼 문자열을 가져감(중간) → 세대 번호로 발행을 막고 보낼 큐를 루프마다 둠
  - 재검증 → 통과
- 서브에이전트:
  - UDP → 공백 붙은 명령의 응답 비교(중간), echo 테스트 100ms 대기가 프레임에 흔들림(중간), 실행 중 SetTarget 변경, Dispose 테스트가 취소를 확인 못 함(낮음) → Trim 비교, 대기 1초, 명령마다 대상 저장, 0.5초 제한
  - TCP → 멈춘 루프의 이벤트·줄 발행(중간), 주소 전환 테스트 없음(중간), 받자마자 끊는 장치에 간격 안 늘림·작업 묶음 누적(낮음) → 모두 고치고 테스트 2건 추가
  - 문서 → 공백 명령(중간, 위와 같음), 메인 스레드 조건·keepalive 시간(약 12초)·TCP 수신 비ASCII·첫 접속 실패 재시도 설명(낮음) → 문서·코드 고침('?'로 바꿈)
  - 재검증 → 결함 없음. 실행되지 않는 catch 주석, 주소 전환 뒤 Send 확인 없음(낮음) → 고침
- PR 리뷰(Claude `/code-review` high, #76, 5건) → 주석 1건은 바로 고치고, 사용자 지시("전부 수정해")로 나머지 4건도 고침: 끊긴 동안 Send 경고를 끊긴 기간마다 한 번만, 접속 직후 멈추면 실패 로그 없이 끝냄, 재연결 간격 계산을 `ReconnectBackoff`(Utils)로 합쳐 ArduinoManager와 함께 씀(동작 동일), 간격 테스트를 접속 시각 간격 비교로(백그라운드 스레드에서 받음). 변이(받자마자 끊겨도 처음 간격)로 테스트가 "1001ms, 1001ms"로 실패함을 확인.
  - 교차 검증(agy + 서브에이전트) → agy 통과(첫 요청은 네트워크 오류로 재시도). 서브에이전트: 경고 플래그를 접속 때 되돌리면 Send와 겹칠 때 경고가 빠짐, 테스트가 메인 스레드 멈춤에 약함(낮음) → 끊길 때 되돌리고, 접속을 백그라운드 스레드에서 받아 시각을 잼. 홀수 상한에서 1ms 차이·큰 값 넘침이 고쳐짐(참고)
  - Claude 확인: PlayMode 200/200
- 남은 것: 실제 랜 아두이노(UDP·TCP)로는 시험하지 않음(루프백 가짜 장치만). keepalive로 랜선 빠짐을 알아채는 시간은 계산값. 두 현장 프로젝트를 새 클래스로 옮기는 것은 각 프로젝트 작업.

### [2026-11-10 16:30] Claude → Antigravity + 서브에이전트 · PR 리뷰 지적 반영: 동시 포트 스캔, 바이트 단위 핸드셰이크 (fix/video-reporter-device, #75)
- 요청: PR #75 리뷰(Claude `/code-review` high)의 동작 지적 2건을 모두 고침. ① 0.5초로 나눈 `ReadLine`이 시간 초과 때 읽던 줄을 버려 경계에 걸친 핸드셰이크를 잃을 수 있음 ② 포트당 대기 3초를 차례로 기다려 응답 없는 포트가 많으면 느림 → 사용자가 '마지막 포트 먼저' 대신 동시 스캔을 고름. 아두이노 여러 대는 매니저가 싱글톤이라 지금 범위 밖이라고 답함.
- 변경: `PortScan`(포트마다 확인 스레드, 맞으면 나머지를 멈추고 연 포트가 닫힐 때까지만 기다림, 열기가 멈춘 포트는 기다리지 않고 `ProbingPorts`로 다음 스캔이 건너뜀), 핸드셰이크는 `ReadTimeout` 100ms `ReadByte`로 한 바이트씩 읽어 줄 확인, 찾은 직후 취소도 OCE로 전달.
- Claude 확인: PlayMode 176/176, 플레이어 빌드, 실제 장치(CH340) 전 항목 반복 통과. 중간에 COM1이 Unity 밖에서도 "잘못된 기능입니다"로 열리지 않게 됨 → 단계별 진단으로 `Open()`에서 실패하고 PowerShell에서도 같음을 확인해 장치·드라이버 문제로 판단(처음엔 `BytesToRead` 탓으로 잘못 봄). 사용자가 다른 USB 포트에 꽂아 COM9로 다시 잡힌 뒤 통과, 반복 시험 뒤에도 포트가 정상으로 열림. `BytesToRead`가 원인이라고 적었던 주석·CHANGELOG 문구는 확인되지 않은 내용이라 지움(읽기는 `ReadByte` 방식 유지).
- agy (gemini-4-argon-high, 로그인 복구됨):
  - 동시 스캔 1차 → 지적: 찾은 뒤 남은 스레드가 포트를 닫기 전에 돌아옴·늦게 시작한 스레드가 취소 뒤 포트를 엶(중간), 대기 한도와 포트별 대기의 기준 시점 불일치(중간), 찾은 직후 파괴되면 OCE 미전달(낮음) → 모두 고침
  - 2차 재검증 → 통과
  - 3차 재검증(`_openingCount`) → 통과
- 서브에이전트:
  - 동시 스캔 1차 → 통과, `_connectGate` 주석 불일치·다른 보드 리셋 문서화(낮음) → 주석·CHANGELOG에 적음. 열기가 멈추는 드라이버에서 스레드가 쌓일 수 있음(불확실) → `ProbingPorts`로 막음
  - 2차 재검증 → 열기가 늦은 포트(블루투스 등) 때문에 찾은 뒤·취소 뒤에도 최대 5초 기다림(중간), 스레드 시작 실패 시 `ProbingPorts`에 남음(낮음), 문서 불일치(낮음) → 연 포트만 기다리도록 `_openingCount` 추가, 시작 실패 처리, 문구 정리
  - 3차 재검증 → 통과. 스캔이 끝날 때 열기 중이던 포트를 바로 다음 스캔이 한 번 건너뛸 수 있음(낮음, 다음 재시도에서 확인) → 맞바꿈으로 두고 주석에 적음. "이 시간 안에 끝남" 문구가 열기가 막히는 포트와 맞지 않음(낮음) → 고침
- 남은 것: 열기가 멈추는 블루투스 포트, 포트가 여러 개인 환경, 케이블을 뽑았다 꽂는 자동 재연결은 이 PC에서 재현하지 않음. 아두이노는 시험용 스케치가 올라간 채 COM9에 꽂혀 있음.

### [2026-11-10 15:00] Claude → 서브에이전트 (agy 인증 만료) · 실제 아두이노 시험, VideoManager·Reporter (fix/video-reporter-device)
- 요청: VideoManager(프로젝트 RenderTexture 파괴)와 Reporter(닫힌 동안 FPS 문자열 할당) 수정, 연결한 아두이노로 ArduinoManager 시험(스케치 업로드 허용).
- 실제 장치: COM1 CH340 Uno 호환. 원래 스케치는 9600bps로 `Telephone_Booth` 반복 전송. 포트를 연 뒤 첫 줄까지 1655~1658ms(3회). ArduinoManager는 핸드셰이크를 0.5초만 기다려 한 번도 연결되지 않음(실측으로 확인한 기존 결함) → 대기 3초(`HandshakeTimeoutMs`)와 줄 반복 확인으로 고침. 시험용 스케치(`TPL_ECHO_TEST`를 부팅 때 한 번 보내고 받은 줄을 `ECHO:`로 돌려줌)를 `arduino-cli`로 올림(사용자 허락, 원래 스케치는 지워짐).
- Claude 확인(빌드 플레이어 + 임시 확인 스크립트, 복사본 프로젝트에만 넣고 지움): 반복 전송 스케치·한 번 전송 스케치 모두 약 1.7초에 연결, 2초에 9줄 수신, `Send`→`ECHO:ping`, `RebootDeviceAsync` 뒤 `ECHO:pong`, 수동 해제 뒤 4초간 재연결 없음, `ConnectAsync` 연속 호출 연결, 연결 중 해제 시 스캔 중단, 틀린 핸드셰이크는 "unexpected handshake '…'". PlayMode 176/176. 변이: VideoManager 해제 조건을 되돌리면 새 테스트 2개 실패.
- agy: 인증 만료("Authentication required")로 모든 요청 실패 → 서브에이전트 단독. 사용자가 보낸 인증 코드는 자격 증명이라 입력하지 않았고, 이미 끝난 실행의 일회용 코드라 쓸 수도 없음 → 사용자가 직접 `agy` 로그인 필요.
- 서브에이전트:
  - 핸드셰이크 → 통과. 빈 줄이 실패 이유를 덮어씀, 해제·파괴 뒤에도 남은 포트를 최대 3초씩 스캔(낮음) → 줄 사이·포트 사이 취소 확인과 0.5초 단위 대기로 고침
  - VideoManager·Reporter·문서 → 새 테스트가 Destroy 지연 때문에 되돌려도 통과(중간) → Create/IsCreated와 프레임 대기로 고치고 변이로 확인. Reporter가 닫힌 동안 로그 샘플에 낡은 FPS를 기록(중간) → `AddSample`에서 만듦. 다른 VideoPlayer로 옮긴 텍스처 해제(낮음) → 주인이 같을 때만 해제
  - 재검증 → 결함 없음. 마지막 재시도 중 취소되면 중단 대신 연결 실패 경고가 남음(낮음) → 취소된 스캔은 취소로 전달. 0.5초 분할 주석이 실제 동작과 다름(낮음) → 주석 고침
- 남은 것: 케이블을 뽑았다 꽂는 자동 재연결은 사람 손이 필요해 시험하지 않음. IL2CPP 미시험. 아두이노에는 시험용 스케치가 올라가 있으므로 원래 장치로 쓰려면 원래 스케치를 다시 올릴 것.

### [2026-11-10 12:30] Claude → Antigravity + 서브에이전트 · 코딩 스킬 기준 패키지 점검과 정리 (fix/audit-defects)
- 요청: 수정된 코딩 스킬(`unity-stack-scaffold`·`unity-network-protocol`)로 템플릿 전체를 점검하고 나온 것을 전부 수정. summary "한 문장" 규칙은 스킬 문구를 고치기로 함(사용자 결정).
- 점검: grep(규칙 기계 확인) + 영역별 감사(agy: Core·App·Data·Utils, 서브에이전트: UI·네트워크·하드웨어·Editor·Tests). agy 첫 Core 요청은 저장소 밖 파일 읽기 권한 거부로 빈 결과 → 둘로 나눠 저장소 밖 금지를 적고 다시 맡김. 순수 C# 객체의 `?.`·`??` 지적은 스킬이 허용해 오탐으로 뺌.
- 변경: SoundManager 페이드 취소 NRE, 외부 API 로그가 호출을 늦춤, ArduinoManager(재연결 간격 늘리기, 수동 해제 뒤 재연결, 파괴 토큰, 포트별 실패 이유, 재부팅 중 해제), UIManager(효과음 캐시, Graphic 충돌, raycastTarget), JsonLoader(교체 재시도, 교체 실패 시 내용 유실 방지), ShutdownScheduler 할당, 조용한 실패 로그, ProfilerMarker, 필드 순서·summary·테스트 코드 규칙. 스킬 문서 오류 7곳 수정(G: 드라이브, git 밖). 26.11.10-2. 테스트 6건 추가.
- Claude 확인: 검증 프로젝트 PlayMode 174/174(2·3차 수정 뒤 다시 확인), Windows 플레이어 빌드 성공. 변이: SoundManager를 예전 순서로 되돌리면 새 테스트가 NullReferenceException으로 실패함을 확인. 새 페이드 테스트는 이 환경에서 결론 없음이 아니라 실제로 실행됨.
- agy (gemini-4-argon-high):
  - 외부 API(b2) → 통과
  - ArduinoManager 연결부(b3a) → 인증 만료("Authentication required")로 실패, 서브에이전트 단독
  - ArduinoManager 재연결(b3b) → 지적: 읽기 오류와 수동 해제가 겹치면 재연결(중간, 서브에이전트와 같음), 첫 대기 보정 누락·CTS 교체 원자성(낮음) → 모두 고침
  - JSON·예약 종료·타이머(b5) → 통과
  - 2차 수정 재검증 → 통과
- 서브에이전트 (8묶음 + 재검증):
  - SoundManager → 통과(예전 코드에서 실패하는지 추정 → 변이로 확인)
  - 외부 API → 통과, 성공 Return 로그가 호출자 취소로 빠질 수 있음(낮음) → 파괴 토큰으로 보냄
  - ArduinoManager → 첫 연결 중 수동 해제 뒤 연결됨(중간), 읽기 오류·수동 해제 겹침, 간격 상한·0 이하 설정(낮음) → 모두 고침. 빈 handshake 거부는 Breaking으로 적음
  - UIManager → `SetImage` 뒤 `SetButton`이면 클릭 안 됨(중간) → Button을 붙일 때 Graphic을 켬. 꺼진 오브젝트의 TMP가 raycastTarget을 덮어씀(낮음) → 주석·CHANGELOG에 적음
  - JSON·예약 종료·타이머 → 통과. 기존 코드의 교체 실패 시 저장 내용 유실(중간)·접근 거부 재시도·날짜 출처 할당(낮음) → 고침
  - 기계적 정리(필드 순서·summary) → 통과
  - 테스트·문서 → CHANGELOG 누락(SetText·SetTMPText·TMP), 시간 제한 실패 메시지(낮음) → 고침
  - 2차 수정 재검증 → `ExecuteWithExternalApiLoggingAsync`가 await 뒤에 파괴 토큰을 얻음(중간) → 시작할 때 받아 둠. OnDestroy와 파괴 토큰 순서(불확실) → 파괴 표시로 막음
- PR 리뷰(Claude `/code-review` high, 3건, 모두 낮음) → 사용자 지시로 1번만 고침: `ConnectAsync`를 연달아 부르면 이전 스캔과 같은 포트를 다툼 → `_connectGate`(SemaphoreSlim)로 스캔 차례를 하나로 묶고 호출 번호로 가장 새 호출만 스캔. 2번(빈 handshake는 예외 없이 반환)·3번(영구 접근 거부도 재시도)은 의도한 동작이라 그대로 둠.
  - 교차 검증(agy + 서브에이전트) → 둘 다 통과. agy만: 차례를 얻은 직후 파괴되면 OCE가 빠짐(낮음) → 맨 앞에서 파괴 확인. 서브에이전트만: 자동 재연결 스캔과 ConnectAsync가 같은 포트를 다툼(중간, 기존 문제) → 재연결 스캔도 같은 차례를 거치고 시도마다 이미 연결됐는지 확인
  - 재검증(서브에이전트) → ConnectAsync가 자동 재연결을 멈추게 했더니 그 연결이 실패하면 재연결이 다시 시작되지 않음(중간) → 멈추지 않도록 되돌림(차례만으로 다툼이 막힘)
  - Claude 확인: PlayMode 174/174, Windows 빌드 성공, 마지막 수정 뒤 ArduinoManagerTests 2/2·컴파일 오류 0
- 남은 것: IL2CPP 빌드 미시험. 실제 시리얼 장치로 ArduinoManager를 시험하지 않음(스캔·재연결은 코드 리뷰와 간격 계산 테스트만). 백그라운드 스레드에서 `Disconnect`를 부르면 ConnectAsync 이어지는 코드가 그 스레드에서 돌 수 있음(기존 StopAutoReconnect와 같은 성질, 기록만). 영구 접근 거부도 저장을 약 200ms 늦춤(낮음, 기록만). agy 인증이 한 번 만료됐다가 이후 요청은 됐으므로, 다음 세션 전에 `agy` 로그인 상태를 확인할 것.

### [2026-11-10 10:45] Claude → Antigravity + 서브에이전트 · 자동 재시작 상태 API·종료 코드 유지·영상 재생 결과 (feat/auto-restart-status-api)
- 요청: TODO 할 일 중 Reporter 항목을 뺀 4건 처리(재시작 이유 공개 API, 감시 상태 API, ApiManagerBase 종료 코드 0 문제, `PrepareAndPlayAsync` 성공 여부).
- 변경: `AutoRestartWatchdog` 에 `WasRestarted`·`RestartReason`·`IsWatching`·`WaitUntilWatchingAsync`·`TryRequestRestart` 추가. 앱이 `CreateEventW` 로 이벤트를 만들고 감시가 앱 핸들을 연 뒤 Set하면 지켜보는 것으로 봄. 종료 코드 75는 "restart requested by the app"으로 기록. `QuitReason.ExitCode`·`Set(reason, exitCode)` 추가, `ApiManagerBase` 가 그 코드로 다시 끝냄. `PrepareAndPlayAsync` 가 `UniTask<bool>` 을 돌려줌. 26.11.10-1. 테스트 8건 추가.
- Claude 확인: 로컬 경로로 패키지를 참조하는 검증 프로젝트 복사본을 명령줄 Unity(2022.3.62f3)로 실행해 컴파일 오류 0, PlayMode 163/163(리뷰 반영 뒤 다시 163/163). Windows 플레이어 빌드 성공(플레이어 조건 코드 컴파일). 임시 확인 스크립트를 넣은 빌드를 실행해 확인함: 감시 시작 1초 뒤 "now watching" → `TryRequestRestart` → 종료 로그 경로를 거쳐 종료 코드 75 → 5초 뒤 재시작 → 다시 켜진 앱이 `WasRestarted=True`, 이유 "restart requested by the app (exit code 75)", 기존 감시 연결 → 정상 종료(0)로 감시도 끝남. 없는 영상 파일 테스트는 시간 초과가 아니라 오류 경로로 0.02초에 끝남.
- agy (gemini-4-argon-high, 재검증 1묶음):
  - 리뷰 뒤 고친 부분(설정 읽기 취소 시 결과 확정, 취소와 준비 완료가 같은 프레임일 때 재생 안 함, 핸들·신호 순서 단언) → 앞 2개 통과. 순서 단언이 핸들 줄이 없으면 늘 통과함(낮음) → 존재 확인을 먼저 하도록 고침
- 서브에이전트 (2묶음):
  - VideoManager 반환값·테스트 → 통과. 취소와 준비 완료가 같은 프레임이면 재생함(낮음) → Play 전에 취소 확인. 같은 프레임 오류 경우는 테스트로 재현하지 않음 → CHANGELOG에 적음. Breaking 범위 → `UniTask<T>`→`UniTask` 암시적 변환이 있어 메서드 그룹을 `Func<…, UniTask>` 에 넘기는 경우로 좁힘
  - 문서(CHANGELOG·README·TODO·package.json) → 통과. 시간 제한 시 `OperationCanceledException` 누락, `Application.Quit(코드)` 가 0이 되는 조건(`ApiManagerBase` 사용) 누락(둘 다 낮음) → 고침
- 교차 검증 (agy gemini-4-argon-high + 서브에이전트, 3묶음):
  - 감시 C#(WatchDecided 완료 경로·핸들 수명·P/Invoke·TryRequestRestart 흐름) → 둘 다 지적: 설정을 읽다 취소되면 WatchDecided가 완료되지 않음(낮음) → 고침. 서브에이전트만 지적: 신호 대기 중 스코프가 파기되면 `IsWatching` 이 그 실행 동안 false로 남음, 다른 종료가 보류 중일 때 `TryRequestRestart` 가 QuitReason을 덮어씀, `IsWatching` 이 호출마다 OS에 물음(모두 낮음) → summary에 적음(동작 유지)
  - 감시 스크립트·명령줄·테스트 → 둘 다 통과. 서브에이전트만 지적: 신호 위치(핸들을 연 뒤)를 테스트가 지키지 않음(낮음) → 순서 단언 추가
  - 종료 코드(QuitReason·ApiManagerBase) → 둘 다 통과. 서브에이전트 참고: `ApiManagerBase` 의 `Application.Quit(QuitReason.ExitCode)` 는 에디터 테스트로 확인할 수 없음 → 빌드 실행으로 확인
- PR 리뷰(Claude `/code-review` high, 6건) → 사용자 지시로 전부 고침: 진행 중인 종료를 재시작 요청이 덮어씀(`QuitReason.IsQuitRequested` 와 거절), 준비 중 VideoPlayer 파괴 시 예외(false 반환), 취소된 종료의 코드 75 잔류(`QuitReason.Clear()` 와 안내), 스코프 파기 뒤 `IsWatching` 이 false로 남음(대기를 앱 종료 토큰에 묶음), PID 재사용(감시 생존 이벤트 `Local\HuliacDev.AutoRestart.Alive.<PID>` 로 확인), 영상 테스트 정리(끝날 때 취소).
- 수정분 검증:
  - 교차 검증(agy gemini-4-argon-high + 서브에이전트, 종료·감시 묶음) → 둘 다 통과. 서브에이전트만 지적: 시작 시각 비교가 시계 역행에 약함(중간) → 생존 이벤트 방식으로 바꿈. 둘 다 지적: 파기 뒤 로그 3곳(낮음) → 고침. 서브에이전트 낮음 2건(GameCloser 확인 창 취소 안내, 자기 요청 진행 중에도 false) → 주석에 적음
  - 서브에이전트(영상 묶음) → 통과. 파괴 테스트가 파괴 경로를 지나는지 불확실 → 루프 확인을 빼는 변이로 그 테스트가 실패함을 확인
  - agy(생존 이벤트 재검증) → 통과. 이벤트 확인과 핸들 열기 사이 PID 재사용 틈(낮음) → 핸들을 먼저 열도록 순서를 바꿈
- 수정분 Claude 확인: PlayMode 167/167, 생존 이벤트 테스트 추가 뒤 168건 중 `JsonLoaderTests` SaveAsync 1건이 `File.Replace` 파일 잠금으로 실패했다가 다시 두 번 17/17(이번 변경과 무관한 일시 실패로 보고 TODO에 적음). 빌드 실행으로 재시작 요청 → 75 → 재시작 → 생존 이벤트로 기존 감시 찾기 → 정상 종료를 다시 확인.
- 남은 것: 확인 빌드는 Mono이고 IL2CPP는 시험하지 않음. 시작 직후 감시가 앱을 잡기 전에 0이 아닌 코드로 끝내는 경우와 감시 실행 실패 경로는 빌드로 재현하지 않음(`IsWatching` 이 false라 `TryRequestRestart` 가 거절하는 것은 에디터 테스트로만 확인). 이전 버전 감시가 떠 있는 채로 새 빌드로 바꾸면 감시가 하나 더 뜸(CHANGELOG에 적음). `ApiManagerBase` 의 `MarkQuitRequested` 호출과 `TryRequestRestart` 의 연결은 에디터 테스트로 잡을 수 없음(코드 리뷰로 확인). 확인 빌드가 검증 프로젝트와 같은 `persistentDataPath` 를 써서 그곳 `AutoRestart.log` 에 확인 기록이 남음.

### [2026-11-04 15:46] Claude → Antigravity · WebGL 코드 삭제 (chore/remove-webgl-branches)
- 요청: 더 이상 다루지 않는 WebGL을 코드에서도 삭제. 사용자가 Android를 가끔 빌드하므로 Android 영향 확인(사용자 결정, Android도 쓰는 URL 경로 처리는 유지).
- 변경: `ArduinoManager` WebGL 스텁, `GameCloser` WebGL 분기, 파일 로깅 조건의 `!UNITY_WEBGL` 제거. 주석과 `JsonLoader` 로그의 WebGL 언급 정리. README 플랫폼을 `Windows (주 타깃) / Android` 로, `UNITASK_DOTWEEN_SUPPORT` 는 플랫폼별 값이라는 안내 추가. 26.11.4-3(WebGL 빌드 지원 중단은 Breaking).
- Claude 확인: 코드에 WebGL 언급 0(git grep, ThirdParty 제외). 검증 프로젝트 PlayMode 155/155. `CompilePlayerScripts` 로 Windows·Android 스크립트 컴파일 통과(오류 0). 검증 프로젝트는 Android 심볼에 `UNITASK_DOTWEEN_SUPPORT` 가 없어 잠시 넣어 컴파일한 뒤 원래 값으로 되돌림(사용 프로젝트 8곳은 모두 이미 있음). 임시 확인 스크립트는 삭제.
- agy (gemini-4-argon-high, 2묶음):
  - 전처리 지시문 짝·삭제 범위·Windows/Android 컴파일 동일성 → 통과
  - 주석·로그 뜻 유지와 테스트 기대 문구, CHANGELOG 형식·공개 저장소 규칙 → 통과
- 남은 것: Android는 스크립트 컴파일만 확인했고 실제 APK 빌드·실행은 하지 않음. WebGL 스크립트 컴파일은 지운 뒤에도 통과하지만 파일 로깅·앱 종료 등 런타임 동작은 지원 대상이 아님.

### [2026-11-04 15:23] Claude → Antigravity · README를 코드와 대조해 정리 (main)
- 요청: README를 내용에 맞게 수정, PR 없이 main에 바로 푸시(사용자 지시).
- 변경: Api Compatibility Level 안내 수정, DOTween 설치 단계와 예약 종료 절·`apiUrl`·키 생략 동작 추가, 없는 `Runtime/Logging` 폴더와 `AppSettings` 타입, 프로젝트 스코프 예시, 폴더 트리·클래스 표 수정. 코드 변경 없음(CHANGELOG·버전 그대로).
- Claude 확인: README의 코드 이름을 저장소와 대조, 설치 표의 버전이 검증 프로젝트 manifest·packages.config와 일치, 프리팹에 `GameCloser`·`SystemCanvas` 있음, `JsonUtility` 키 대소문자 구분을 에디터에서 실행해 확인(`ResetTime` 0, `resetTime` 5).
- agy (gemini-4-argon-high, 2묶음):
  - 설치 안내·프로젝트 스코프 예시·폴더 트리·클래스 표 → 통과
  - 예약 종료 절·Settings.json 절·공개 저장소 규칙 → 통과
- 추가 변경: 더 이상 다루지 않는 WebGL 내용을 README에서 삭제(사용자 지시). README에 WebGL 언급 0(grep), agy(gemini-4-argon-high) 다른 설명 유지·파일 끝 구조 → 통과.
- 남은 것: `[Inject]` 지점의 소스 제너레이터 적용은 예전 확인 기록뿐이고, 검증 프로젝트에 제너레이터가 없어 다시 확인하지 못함(README에 그대로 적음). 새 설치 절차를 처음부터 따라 해 보지는 않음. 코드의 WebGL 분기는 그대로 둠.

### [2026-11-04 15:08] Claude → Antigravity · 쓰지 않는 warningTime·fadeTime 제거 (chore/remove-unused-settings-keys)
- 요청: 어느 코드도 읽지 않는 `warningTime`·`fadeTime` 정리(사용자 지시).
- 변경: `Settings` 의 두 필드 제거, README 예시·설명과 테스트의 임시 JSON 정리. 26.11.4-2(필드를 코드에서 읽던 프로젝트는 Breaking).
- Claude 확인: Runtime·Editor·Tests·Tools~·README·package.json에 참조 0(git grep), 이 PC의 사용 프로젝트 코드·설정에도 0. 검증 프로젝트 컴파일 오류 0, PlayMode 155/155. 두 키가 남은 JSON도 `resetTime` 을 정상으로 읽음. 동작이 바뀌지 않아 테스트 추가 없음.
- agy (gemini-4-argon-high, 1묶음):
  - 테스트 임시 JSON 변경의 영향, CHANGELOG 형식·공개 저장소 규칙 → 통과
  - 첫 요청은 하위 에이전트에 맡긴 뒤 결과 없이 끝남 → 참조 검색은 Claude가 직접 하고 나머지 항목만 다시 맡김

### [2026-11-04 14:49] Claude → Antigravity + 서브에이전트 · targetFrameRate가 0 이하면 수직 동기화 켜기 (fix/vsync-on-unset-frame-rate)
- 요청: 사용 프로젝트의 영상 화면에 찢어짐이 보이고, `targetFrameRate` 를 0으로 두면 빌드에서 1300프레임까지 오름 → 0이면 수직 동기화를 켜도록 Template 변경(사용자 결정, `useVSync` 키 추가안과 비교해 고름).
- 변경: `ApplyFrameRateSettings` 가 0 이하면 `vSyncCount` 를 1로 켬(값을 지정한 경우는 그대로). 주석·README, 테스트 4건. 26.11.4-1(0 이하를 쓰던 프로젝트는 Breaking).
- Claude 확인: 검증 프로젝트 컴파일 오류 0. 고치기 전 새 테스트 3/4 실패(`vSyncCount` 기대 1, 실제 0), 고친 뒤 PlayMode 155/155. Play 중 바꾼 `vSyncCount` 는 Play를 끝내면 원래 값으로 돌아오고 `QualitySettings.asset` 해시도 그대로임. MCP PlayMode 실행으로 켜진 검증 프로젝트의 Enter Play Mode Options는 되돌림.
- agy (gemini-4-argon-high, 1묶음):
  - 문서(README·CHANGELOG·package.json)의 코드 일치·형식·공개 저장소 규칙 → 통과
- 교차 검증 (agy gemini-4-argon-high + 서브에이전트, 1묶음):
  - 두 경로와 대체 설정 경로, 코드 규칙, 테스트 타당성 → 둘 다 통과
- PR 리뷰(Claude `/code-review`, 1건): 코드에서 넣은 `Application.targetFrameRate` 가 수직 동기화가 켜지면 무시된다는 안내가 Breaking에 없음 → 고침. 사용자 요청으로 적용 로그에 `VSync on`/`VSync off` 추가.
- 재검증: agy(gemini-4-argon-high) 로그 문구·CHANGELOG 추가 문장 → 통과. PlayMode 155/155. 검증 프로젝트 Play에서 값 60은 `VSync off`·`vSyncCount` 0·제한 60, 값 0은 `VSync on`·`vSyncCount` 1을 로그와 실제 값으로 확인(설정은 되돌림).
- 남은 것: 실제 프레임 제한과 찢어짐 해소는 스탠드얼론 빌드에서 아직 확인하지 않음(사용자가 빌드 확인을 건너뛰고 바로 수정하기로 함). 그래픽 드라이버가 수직 동기화를 강제로 끄면 프레임이 제한되지 않음.

### [2026-11-03 18:05] Claude → Antigravity · Build 메뉴와 빌드 중 창 영어로 (chore/english-build-menu)
- 요청: Build 하위 메뉴를 "Windows Build", "Setting"으로, 빌드 중 압축·복사 때 뜨는 창도 영어로. Template만 바꾸고 버전만 올림(사용자 결정, 프로젝트 반영은 사용자가 함).
- 변경: 메뉴 Build/Windows Build·Build/Setting, 빌드 대화상자(씬 없음, Build 폴더 비우기 실패, 보관 폴더 없음: Open Settings / Build Without Archiving / Cancel), 진행 표시(Archiving Build, Compressing, Copying Build), 복사 확인 창(Copy Build, Don't ask again, Copy / Skip). Preferences 화면 도움말의 메뉴 이름, README·package.json 설명의 메뉴·버튼 이름을 맞춤. Preferences 화면과 BuildInfo.txt 항목 이름은 그대로. 26.11.3-5(메뉴 경로 Breaking).
- Claude 확인: 검증 프로젝트 컴파일 오류 0, Build 메뉴 항목이 "Build/Windows Build | Build/Setting", 복사 확인 창을 띄워 영어 문구가 창 크기 안에 들어가는 것을 화면으로 확인(Skip으로 닫음, 설정값 그대로).
- agy (gemini-4-argon-high, 1묶음): 문구 뜻·버튼 순서와 처리 → 통과. README 메뉴·버튼 이름 → 지적: package.json 설명에 옛 메뉴 이름 → 고침. CHANGELOG·버전·개인 정보 → 통과.

### [2026-11-03 17:30] Claude → Antigravity · 에디터·도구 로그 영어로 통일 (chore/english-logs)
- 요청: "이건 템플릿이니까 로그는 영어로"(사용자 지시).
- 변경: `BuildMenu`·`PackageUpdater`·`ShutdownScheduleEditorBuilder` 콘솔 로그, Reporter에 덧붙인 경고 한 줄, 작업 스케줄러 백업 스크립트(`Write-BackupLog`) 문구를 영어로. UI 문구와 `BuildInfo.txt` 항목 이름(설치 기록에서 읽음)은 그대로. 26.11.3-4.
- Claude 확인: 로그 호출에 한글 문자열이 남지 않음(grep), 검증 프로젝트 컴파일 오류 0·새 문구가 DLL에 들어감, 종료 스케줄 편집기 `dotnet build` 오류·경고 0.
- agy (gemini-4-argon-high, 1묶음): 뜻·보간 변수 유지 → 통과. C#·PowerShell 문자열 구문 → 통과. CHANGELOG·버전·개인 정보 → 통과.
- 남은 것: 백업 스크립트 로그는 각 프로젝트에서 종료 스케줄 편집기를 다시 만들고 작업 스케줄러를 갱신해야 바뀜.

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
