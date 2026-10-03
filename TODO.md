# TODO

<!-- Template 패키지에서 고쳐야 할 것. 다른 Unity 프로젝트에서 작업하다가 Template의 문제나 개선점을 발견하면 "할 일"에 적는다. -->
<!-- 형식: - [ ] 할 일 — 발견일: YYYY-MM-DD. 공개 저장소이므로 프로젝트 이름 대신 증상과 재현 조건을 일반적으로 적는다. -->
<!-- 고칠 때는 Template의 브랜치 → PR → 머지 흐름을 따르고, 끝나면 [x]로 바꾸고 완료일과 PR 번호를 붙여 "완료"로 옮긴다. -->

## 진행 중

## 할 일

- [ ] `JsonLoader.LoadAsync`가 취소(`OperationCanceledException`)도 삼키고 `new T()`를 돌려줘, 호출부가 오브젝트 파괴 뒤에도 기본값으로 초기화를 계속함. 재현: `GetCancellationTokenOnDestroy()` 토큰으로 로드하는 동안 씬을 다시 불러오면, 파괴된 컴포넌트가 이어서 이미 해제된 R3 Subject·StateMachine에 접근해 `ObjectDisposedException`이 나고 null 경고가 연쇄로 남음. 취소는 다시 던지거나(최소한 summary에 "취소돼도 new T()를 반환하므로 호출부가 토큰을 확인해야 함"을 명시) — 발견일: 2026-10-03

## 완료
