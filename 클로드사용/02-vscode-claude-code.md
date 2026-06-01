# 02. VSCode + Claude Code + GitHub 연동 가이드

코드 편집기(VSCode) 안에서 Claude와 대화하며 작업하는 방법입니다. 채팅 UI는 Desktop과 비슷하지만, **파일/선택 영역을 자동으로 인식**하고 변경사항을 **side-by-side 비교**로 보여주는 것이 큰 차이입니다.

> 대상: 약간의 코드 작업이 있거나, 문서/스크립트를 자주 다루는 사람 / 난이도: ★★☆ / 소요 시간: 약 20분
>
> 공식 문서: https://code.claude.com/docs/en/ide-integrations

---

## 이 트랙이 적합한 사람

- 01번(Desktop)으로 해보니 **파일 단위 작업**이 많아 더 편한 도구가 필요한 사람
- 마크다운/CSV/스프레드시트/스크립트 등을 **코드 편집기에서 직접** 손보고 싶은 사람
- GitHub과 좀 더 **밀접하게** 일하고 싶지만, 03번(OMC CLI)까지는 부담스러운 사람

> 순수 채팅 사용이면 01번으로 충분합니다. 본격 자동화/멀티 에이전트가 필요하면 03번으로.

---

## 이 가이드로 할 수 있게 되는 일

- VSCode에서 파일을 열고 **선택한 부분만** Claude에게 물어보기
- "이 파일을 100_test 저장소에 커밋해줘" → Claude가 git 명령 자동 실행
- 변경사항을 **diff(차이) 뷰**로 검토 후 승인/거절
- `/plan`, `/usage`, `/compact` 같은 명령으로 작업 흐름 관리

---

## 0. 준비물

- [ ] **VSCode 1.98.0 이상** (https://code.visualstudio.com/download)
- [ ] **Anthropic 계정** (Claude.ai 계정과 동일)
- [ ] 인터넷 연결
- [ ] (권장) GitHub 계정 + `100_test` 저장소

> Node.js나 별도 CLI 설치는 **필요 없음** — 확장에 모두 포함되어 있습니다.

---

## 1. VSCode 설치

이미 설치되어 있다면 **버전 확인** 후 건너뛰세요:
- VSCode 실행 → **Help → About** → 버전이 `1.98.0` 이상인지 확인
- 낮으면: **Help → Check for Updates** 또는 https://code.visualstudio.com/download 에서 최신 설치 파일 받기

신규 설치:
1. https://code.visualstudio.com/download 에서 Windows 설치 파일 다운로드
2. 설치 진행 (모든 옵션 기본값 OK, "Add to PATH" 권장)
3. 실행 후 한국어 표시팩 안내가 나오면 설치 (선택사항)

---

## 2. Claude Code 확장 설치

### 방법 A — 마켓플레이스에서 검색 (가장 일반적)
1. VSCode 좌측 사이드바의 네모 블록 4개 아이콘(**Extensions**) 클릭 또는 `Ctrl+Shift+X`
2. 검색창에 `Claude Code` 입력
3. **Anthropic** 발행자의 **Claude Code** 항목 선택 → **Install** 클릭

### 방법 B — 링크로 바로 설치
브라우저 주소창에 다음 입력 후 엔터 (VSCode가 자동 실행됨):
```
vscode:extension/anthropic.claude-code
```

### 설치 안 보이면
- VSCode 재시작: 명령 팔레트(`Ctrl+Shift+P`)에서 `Developer: Reload Window`
- 다른 AI 확장(Cline, Continue 등) 일시 비활성화

> Cursor/Windsurf/Kiro 같은 VSCode 변형 에디터에서도 동일한 이름으로 검색 가능. 또는 Open VSX(https://open-vsx.org/extension/Anthropic/claude-code) 활용.

---

## 3. 첫 실행 & 로그인

설치 후 **Spark(✱) 아이콘**을 찾으세요. 다음 4곳 중 하나에 있습니다:

| 위치 | 설명 |
|------|------|
| **Editor Toolbar** (파일 열렸을 때 우측 상단) | 가장 빠른 진입 |
| **Activity Bar** (좌측 세로 아이콘 바) | 항상 보임, 세션 목록 표시 |
| **Status Bar** (우하단) | `✱ Claude Code` 클릭 |
| **Command Palette** (`Ctrl+Shift+P`) | `Claude Code` 검색 → `Open in New Tab` |

### 로그인
1. Claude 패널이 열리면 **Sign in** 버튼 클릭
2. 브라우저가 자동으로 열리며 Anthropic 인증 페이지
3. 승인 → VSCode로 자동 복귀 → 로그인 완료

로그인 안 되면: 명령 팔레트에서 `Developer: Reload Window` 후 재시도.

---

## 4. 첫 사용 — 파일 보면서 질문하기

1. VSCode에서 100_test 폴더 열기 (**File → Open Folder...**)
2. `README.md` 클릭해서 열기
3. 본문 중 **몇 줄을 마우스로 선택**
4. Spark 아이콘 또는 `Ctrl+Esc`로 Claude 패널 열기
5. 채팅창에 입력:
   ```
   선택한 부분을 한국어로 더 자연스럽게 다듬어줘
   ```
6. Claude가 **diff 뷰**에 수정안 표시 → **Accept** / **Reject** / 직접 편집 가능

> 팁: 파일을 직접 끌어서 채팅창에 떨어뜨려도 첨부 가능. `@파일명`으로도 참조 (예: `@README.md`).

---

## 5. GitHub 연동 — 3가지 방법

### 방법 1 — 내장 git 명령 (가장 쉬움, 권장)

Claude Code는 워크스페이스의 git을 자동 인식합니다. 그냥 자연어로:

```
오늘 수정한 내용을 커밋하고 GitHub에 푸시해줘
```
```
이 변경사항으로 'docs: README 보강' 메시지의 커밋 만들고 푸시
```
```
새 브랜치 'feature/login'을 만들고 거기서 작업 시작해줘
```

Claude가 `git add`, `git commit`, `git push`를 순서대로 실행하고 결과를 보여줍니다. **각 명령마다 권한 요청**이 한 번 뜸 — `Allow` 또는 `Allow always` 선택.

### 방법 2 — gh CLI로 이슈/PR 생성

GitHub CLI(`gh`)가 설치되어 인증되어 있다면(이미 설정 완료) Claude가 이를 활용:

```
100_test에 "TODO 정리" 이슈를 만들어줘
```
```
지금 브랜치로 PR을 만들어줘. 제목은 "docs: 가이드 추가"
```

### 방법 3 — MCP GitHub 서버 (Desktop과 동일)

01번 가이드처럼 PAT 기반 MCP 서버를 붙이려면 통합 터미널(`` Ctrl+` ``)에서:

```powershell
claude mcp add --transport http github https://api.githubcopilot.com/mcp/ --header "Authorization: Bearer ghp_여기에_PAT_붙여넣기"
```

그러면 Desktop과 같은 방식으로 자연어 GitHub 조작이 가능합니다.
설치된 MCP 서버 관리는 채팅창에 `/mcp` 입력.

> **언제 어느 방법?**
> - 코드/문서 작업 위주 → 방법 1 (충분)
> - 이슈/PR을 자주 만든다 → 방법 2 추가
> - GitHub MCP의 풍부한 검색·코멘트 기능까지 → 방법 3

---

## 6. 자주 쓰는 기능

| 기능 | 사용법 |
|------|--------|
| 채팅창 ↔ 편집기 포커스 전환 | `Ctrl+Esc` |
| 새 대화 시작 | 명령 팔레트 → `Claude Code: Open in New Tab` |
| 파일·라인 참조 삽입 | 텍스트 선택 후 `Alt+K` (`@file.md#5-10` 형태로 입력됨) |
| 권한 모드 변경 | 채팅창 아래 모드 표시 클릭 → `Plan` / `Auto-accept` 등 |
| 명령 메뉴 | 채팅창에 `/` 입력 → 사용 가능 명령 표시 |
| 토큰 사용량 확인 | `/usage` |
| 대화 압축 | `/compact` (긴 대화 메모리 절약) |

### Plan 모드 (강력 추천)
큰 변경 전에 사용하세요. Claude가 **수정하기 전에 계획만 먼저** 마크다운으로 보여줍니다. 그 문서에 인라인 코멘트로 피드백을 달면, 그걸 반영해서 실행.

활성화: 채팅창 아래 모드 표시 클릭 → **Plan** 선택.

---

## 7. 일상 사용 시나리오

### 문서 정리
```
docs 폴더의 모든 .md 파일 맞춤법을 점검하고 수정해줘
```

### 회의록 → 이슈 변환
```
@meeting-notes.md의 액션 아이템들을 100_test 저장소에 이슈로 각각 만들어줘
```

### 기존 코드 설명
```
선택한 함수가 무슨 일을 하는지 한국어로 설명해줘
```

### 데이터 변환
```
@data.csv를 JSON으로 변환해서 data.json으로 저장해줘
```

### Git 워크플로
```
오늘 변경한 모든 파일을 적절한 단위로 나눠서 3개 커밋으로 만들고 푸시해줘
```

---

## 8. CLI도 같이 쓰고 싶다면

확장에 포함된 CLI를 VSCode 통합 터미널에서 바로 사용:

1. ``Ctrl+` `` (백틱)으로 터미널 열기
2. `claude` 입력 → CLI 모드 실행
3. 확장과 **같은 세션 히스토리** 공유 — `claude --resume`로 이전 대화 이어가기

CLI는 그래픽 패널에 없는 기능(전체 슬래시 명령, MCP 세부 설정, `!` 쉘 단축, 탭 완성)을 제공합니다.

---

## 9. 정리

이제 당신은:
- ✅ VSCode 안에서 Claude와 코드/문서 작업 가능
- ✅ 변경사항을 diff 뷰로 검토하며 승인/거절
- ✅ 자연어로 GitHub 커밋·푸시·이슈·PR 처리 가능
- ✅ 필요시 통합 터미널에서 CLI까지 활용

> 더 강력한 자동화(멀티 에이전트, autopilot)가 필요하면 [03. oh-my-claudecode 설치](./03-oh-my-claudecode-설치.md)로 진행.

> 문제가 생기면 [99. 트러블슈팅](./99-troubleshooting.md) 참고.
깃허브 작업
