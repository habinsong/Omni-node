# 리스크 및 품질/운영 기준

업데이트 기준: 2026-05-08

현재 상태: 이 문서는 Gemini 검색 전환 당시의 리스크/품질 기준이다. 최신 운영 기준은 상위 `docs/아키텍처_흐름.md`, `docs/도구_통합_패널_사용_가이드.md`, `docs/검증_가이드.md`를 우선 본다.

## 1. 주요 리스크

| 리스크 | 영향도 | 설명 | 대응 |
|---|---|---|---|
| 검색 결과 변동성 | 높음 | 동일 질의 결과가 시점별로 달라질 수 있음 | 재수집/정규화/중복제거 |
| 근거 부족 상태 생성 | 높음 | 생성기가 단정형 응답을 만들 위험 | fail-closed 강제 |
| 건수 미충족 | 중간 | N건 요청에서 건수 부족 가능 | count-lock + 보강 루프 |
| 키 정책 우회 | 높음 | 환경변수 키 주입으로 정책 이탈 가능 | keychain/secure_file_600 강제 |
| 채널별 편차 | 중간 | 경로별 동작 차이 발생 가능 | 공통 파이프라인 단일화 |

## 2. 품질 게이트
- 요청 건수와 출력 건수 일치(count-lock)
- 최신성 실패 항목 출력 0건
- 인용 누락률 기준 충족
- 키 소스 정책 위반 0건

## 3. 운영 관측 항목
- `query`, `targetCount`, `retrievedCount`, `validatedCount`
- `freshnessPass`, `credibilityPass`, `coveragePass`, `countLockSatisfied`
- `retryCount`, `dropReasons`, `modelRoute`, `keySource`

## 4. 장애 대응 기준
- 즉시 차단 조건
  - 키 소스 정책 위반
  - fail-closed 미적용
  - count-lock 우회 출력
- 부분 비활성 조건
  - 특정 생성기 장애
  - 채널별 임시 라우팅 오류

## 5. 최종 전환 기준
- P0~P7 완료
- 품질 게이트 충족
- 운영 체크리스트 통과
- 복구 경로 확인 완료
