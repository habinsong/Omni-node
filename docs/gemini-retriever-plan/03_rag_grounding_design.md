# Gemini 검색 리트리버 + RAG 상세 설계

업데이트 기준: 2026-05-15

현재 상태: 이 문서는 Gemini 검색 전환 당시의 RAG/grounding 설계다. 최신 운영 기준은 상위 `docs/아키텍처_흐름.md`, `docs/도구_통합_패널_사용_가이드.md`, `docs/검증_가이드.md`를 우선 본다.

## 1. 목표
- 최신성 질의에서 신뢰 가능한 근거를 안정적으로 수집한다.
- 생성기는 근거 팩 기반으로만 응답하도록 강제한다.
- count-lock과 fail-closed를 기본 정책으로 운영한다.

## 2. 핵심 동작
- `SearchGateway` 호출
- `GeminiGroundedRetriever` 검색 수행
- `Evidence Pack` 생성
- `Answer Guard` 검증
- 멀티 생성기로 최종 출력

## 3. 검색 파이프라인
1. 질문 프로파일링(`timeSensitivity`, `riskLevel`, `answerType`)
2. 1차 검색(원문 질의)
3. 2차 검색(질의 재작성)
4. 부족 시 보강 검색
5. 후보 정규화/중복 제거
6. 최신성/신뢰성 판정
7. count-lock 충족 여부 판정

## 4. 과수집 정책
- 기본: `K = max(2N, 10)`
- 고노이즈 질의: `K = max(3N, 15)`
- 열거 질의: 전체 열거 모드(페이지 종료까지 수집)

## 5. Evidence Pack 계약
```json
{
  "query": "오늘 주요 뉴스 5건",
  "requestedAtUtc": "2026-03-05T00:00:00Z",
  "userTimezone": "Asia/Seoul",
  "constraints": {
    "targetCount": 5,
    "maxAgeHours": 24,
    "minIndependentSources": 2,
    "strictTodayWindow": true
  },
  "items": [
    {
      "citationId": "c1",
      "title": "...",
      "url": "https://...",
      "publishedAt": "2026-03-05T08:30:00+09:00",
      "snippet": "...",
      "freshnessScore": 0.9,
      "credibilityScore": 0.8,
      "duplicateClusterId": "cluster-1"
    }
  ],
  "quality": {
    "freshnessPass": true,
    "credibilityPass": true,
    "coveragePass": true,
    "countLockSatisfied": true
  }
}
```

## 6. fail-closed 정책
- `freshnessPass=false` 또는 `credibilityPass=false`면 확정형 답변 금지
- `countLockSatisfied=false`면 생성 단계 진입 금지
- 기준 미달 시 재수집 또는 실패 메시지 반환

## 7. count-lock 정책
1. 요청 건수 슬롯 고정
2. 슬롯 미충족 시 재수집 루프 실행
3. 신뢰 하한선 미달 항목은 출력 금지
4. 반복 상한 도달 시 실패 처리

## 8. 보안/키 정책
- 전환 당시 기준으로 설정 탭 저장 Gemini 키만 사용
- macOS: keychain
- Windows/Linux: secure_file_600 또는 로컬 secure store
- 키 평문 로그 금지

## 9. 완료 기준
- 최신성 질의에서 시간창 외 항목 0건
- 요청 건수와 출력 건수 일치
- 인용 매핑 누락률 기준 충족
