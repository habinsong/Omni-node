---
name: skill-creator
description: 사용자가 새 스킬 만들기를 요청하면 <omni:skill> 디렉티브를 응답에 포함해 .omni/skills/<name>/SKILL.md를 실제로 생성한다.
---

사용자가 채팅(대화탭, 텔레그램 등)에서 "스킬 만들어줘", "이런 스킬 등록해줘", "make me a skill" 같이 새 스킬 생성을 요청하면, 응답 본문에 아래 형식의 `<omni:skill>` 디렉티브를 그대로 출력한다. 미들웨어가 후처리하여 실제 파일을 만든다.

## 디렉티브 형식

```
<omni:skill name="kebab-case-name" description="한 줄 설명" scope="project" overwrite="false">
스킬 본문(markdown). 적용 조건, 출력 형식, 예시를 간결하게.
</omni:skill>
```

- `name` (필수): `^[a-z0-9][a-z0-9-]{0,62}$`. 한글·공백·언더스코어 불가.
- `description` (필수, 한 줄): 언제 쓰는 스킬인지 명확히.
- `scope` (선택, 기본 `project`): 사용자가 "전역/global/어디서나"를 명시하면 `global`.
- `overwrite` (선택, 기본 `false`): 동일 이름 존재 시 덮어쓰기 허용 여부. 사용자가 명시 동의 시에만 `true`.

## 행동 규칙

- 이름이 명시되지 않으면 요청 의도를 kebab-case로 의역해 정한다.
- 본문은 사용자가 요청한 동작만 적고 추측으로 부풀리지 않는다.
- 모호하면 한 가지만 짧게 되묻고 답을 받은 뒤 디렉티브를 출력한다.
- 디렉티브를 코드블록(```)으로 감싸지 않는다.
- 디렉티브 외 자연어는 평소처럼 작성하되, "곧 만들어집니다" 같은 미래형 안내는 불필요하다 — 미들웨어가 디렉티브 자리를 `[skill_create:ok] ...` 또는 `[skill_create:error] ...` 결과 노트로 치환한다.

## 예시

사용자: "PR 요약 스킬 만들어줘"
응답:
```
PR 요약용 스킬을 추가했어요.

<omni:skill name="pr-summary" description="PR diff와 제목을 받아 핵심 변경/리스크/검증을 3섹션으로 정리한다.">
- 입력: PR 제목, 본문, 변경 파일 목록
- 출력: 변경 요약(3문장), 리스크(불릿), 검증 체크리스트(불릿)
- 추측이 필요한 부분은 "확인 필요"로 표기한다.
</omni:skill>
```
