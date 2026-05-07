# 실행 체크리스트

업데이트 기준: 2026-05-07

현재 상태: 이 문서는 Gemini 검색 전환 당시의 실행 체크리스트다. 최신 운영 기준은 상위 `docs/아키텍처_흐름.md`, `docs/도구_통합_패널_사용_가이드.md`, `docs/검증_가이드.md`를 우선 본다.

## 1. 착수 전 체크
- [ ] P0~P7 목표와 완료 기준 확정
- [ ] 기준 문서(`GEMINI_SEARCH_RETRIEVER_INTEGRATION_PLAN.md`) 고정
- [ ] 키 정책 타입 고정(`GeminiKeySource = keychain | secure_file_600`)
- [ ] 실행 정책 고정(`GeminiKeyRequiredFor = test, validation, regression, production_run`)

## 2. 키 정책 체크
- [ ] macOS keychain Gemini 키 존재 확인
- [ ] Windows/Linux secure_file_600 또는 로컬 secure store Gemini 키 존재 확인
- [ ] 환경변수 직접 키 주입 차단 확인
- [ ] 키 미존재 시 즉시 실패 경로 확인
- [ ] 로그 키 마스킹 확인

## 3. 검색/근거 체크
- [ ] SearchGateway 구현
- [ ] GeminiGroundedRetriever 구현
- [ ] 과수집(2N/3N) 정책 반영
- [ ] 정규화/중복 제거 반영
- [ ] Evidence Pack 스키마 고정

## 4. 출력 가드 체크
- [ ] freshness 판정 반영
- [ ] credibility 판정 반영
- [ ] coverage 판정 반영
- [ ] count-lock 판정 반영
- [ ] fail-closed 동작 확인

## 5. 생성기/채널 체크
- [ ] 4개 생성기 공통 계약 반영
- [ ] 대화 탭 경로 반영
- [ ] 코딩 탭 경로 반영
- [ ] 텔레그램 우회 경로 반영

## 6. 운영/문서 체크
- [ ] 메트릭/로그 항목 반영
- [ ] 릴리스 게이트 체크 반영
- [ ] 릴리스 노트 템플릿 반영
