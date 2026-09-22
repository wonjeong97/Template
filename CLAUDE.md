# CLAUDE.md

`com.huliacdev.template` — 전시/키오스크형 Unity 앱의 공통 기반을 제공하는 UPM 패키지. 소비 프로젝트는 git URL로 이 패키지를 참조한다.

## 이 저장소의 성격

Unity 프로젝트가 아니라 **패키지 저장소**다. `Assets/`, `ProjectSettings/`, `Packages/manifest.json`이 없으며 만들지 않는다. 동작 확인이 필요하면 이 저장소가 아니라 소비 프로젝트 쪽에서 한다.

여기 있는 코드는 앞으로 만들 모든 프로젝트가 물려받고, 에이전트가 "이 스택의 정답 예시"로 열어보는 대상이다. 그래서 규칙 위반이 하나 있으면 그대로 복제된다 — 이 저장소의 코드는 시스템에서 가장 규칙을 잘 지켜야 한다.

## 코딩 표준

작성 규칙은 스킬이 정의한다. 이 파일에 규칙을 복제하지 않는다.

- `unity-stack-scaffold` — `var` 금지, `TryGetComponent`, Unity 오브젝트의 암시적 bool 검사, VContainer/UniTask/MessagePipe/R3/ZLogger/ZString/DOTween 사용 패턴, FSM 계약, Zero-GC 규칙
- `unity-network-protocol` — 패킷 구조체 정의와 바이트 패킹, 소켓 수신 루프, 지연 보상

**스킬이 표준이고 이 저장소가 거기에 맞춘다. 반대 방향이 아니다.** 스킬과 이 저장소의 코드가 어긋나면 기본은 코드를 고치는 것이다. 다만 스킬이 "무엇이 이미 있는가"(예: `StateMachine`, `PacketUtility`의 API 표면)를 잘못 적고 있다면 그건 스킬 쪽 오류이므로 사용자에게 알린다.

예외: `Runtime/ThirdParty/`는 외부에서 들여온 코드(Reporter, RuntimeInspector)다. 규칙 적용 대상이 아니며 요청 없이 손대지 않는다. `Runtime/Input/TemplateInputActions.cs`는 Input System이 생성한 파일이라 직접 편집하지 않는다.

## 레이아웃

- `Runtime/{App,Core,Data,Hardware,Input,Network,UI,Utils}` — 네임스페이스는 `HuliacDev.<폴더명>`
- `Editor/` — 에디터 전용. 빌드에 포함되지 않으므로 리플렉션 금지 규칙의 예외다
- `Tests/Runtime` — `[UnityTest]` 기반 테스트. 테스트를 언제 쓰는지는 `unity-stack-scaffold` 13번을 따른다
- `Tools~` — 물결표 폴더라 Unity가 임포트하지 않는다

## CHANGELOG

공개 API나 동작이 바뀌면 `CHANGELOG.md`를 갱신한다. 소비 프로젝트가 이 파일만 보고 업데이트 여부를 판단하므로, 내부 리팩터링이 아닌 한 기록을 빠뜨리지 않는다.

- 버전 섹션 형식은 `## [26.9.22-2] - 2026-09-22`(날짜 기반, 같은 날 여러 번이면 `-N` 접미사)이며 `package.json`의 `version`과 맞춘다.
- `Added` / `Changed` / `Fixed` / `Removed`로 구분한다.
- 각 항목은 **대상 클래스와 경로를 굵게 앞세우고**, 무엇을 왜 바꿨는지와 추가한 테스트 건수까지 적는다. 기존 항목들의 상세도를 기준으로 삼는다.
- 호환성이 깨지는 변경은 해당 항목 안에 **Breaking:** 을 붙이고, 소비 프로젝트가 무엇을 고쳐야 하는지 적는다.
