# TODO

<!-- Template 패키지에서 고쳐야 할 것. 다른 Unity 프로젝트에서 작업하다가 Template의 문제나 개선점을 발견하면 "할 일"에 적는다. -->
<!-- 형식: - [ ] 할 일 — 발견일: YYYY-MM-DD. 공개 저장소이므로 프로젝트 이름 대신 증상과 재현 조건을 일반적으로 적는다. -->
<!-- 고칠 때는 Template의 브랜치 → PR → 머지 흐름을 따르고, 끝나면 [x]로 바꾸고 완료일과 PR 번호를 붙여 "완료"로 옮긴다. -->

## 진행 중

## 할 일

- [ ] `JsonLoader.LoadAsync`가 취소(`OperationCanceledException`)도 삼키고 `new T()`를 돌려줘, 호출부가 오브젝트 파괴 뒤에도 기본값으로 초기화를 계속함. 재현: `GetCancellationTokenOnDestroy()` 토큰으로 로드하는 동안 씬을 다시 불러오면, 파괴된 컴포넌트가 이어서 이미 해제된 R3 Subject·StateMachine에 접근해 `ObjectDisposedException`이 나고 null 경고가 연쇄로 남음. 취소는 다시 던지거나(최소한 summary에 "취소돼도 new T()를 반환하므로 호출부가 토큰을 확인해야 함"을 명시) — 발견일: 2026-10-03
- [ ] `JsonLoader.SaveAsync`/`Save`가 쓰기 실패(읽기 전용 폴더, 권한 없음 등)를 로그로만 남기고 호출자에게 알리지 않아, 설정 저장 결과를 사용자에게 보여 주려면 저장 직후 파일을 다시 읽어 비교해야 함. 재현: 쓰기 권한이 없는 위치의 StreamingAssets JSON에 SaveAsync 후 호출부에서는 성공과 구분할 방법이 없음. 성공 여부(bool)를 돌려주거나 예외를 다시 던지는 오버로드 추가 — 발견일: 2026-10-07

## 완료
