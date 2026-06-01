# 01. Claude Desktop + GitHub 연동 가이드

채팅창에서 한국어로 "100_test 저장소에 회의록 올려줘"라고 말하면 Claude가 알아서 GitHub에 파일을 만들어주는 환경을 30분 안에 만듭니다.

> 대상: 비개발자 / 난이도: ★☆☆ / 소요 시간: 약 30분

---

## 이 가이드로 할 수 있게 되는 일

- "내 GitHub 저장소 목록 보여줘" → 답변
- "100_test에 오늘 회의록 올려줘" → 파일 자동 생성·커밋
- "100_test에 'TODO 정리' 이슈 만들어줘" → 이슈 생성
- "100_test 최근 변경사항 요약해줘" → 커밋 내역 요약

---

## 0. 준비물 체크리스트

시작 전 다음이 있는지 확인하세요:

- [ ] Claude 계정 (https://claude.ai 에서 가입 — 무료 가능)
- [ ] GitHub 계정 (https://github.com)
- [ ] Windows 10/11 PC
- [ ] 인터넷 연결

---

## 1. Claude Desktop 설치

1. https://claude.ai/download 접속
2. **Download for Windows** 클릭 → 설치 파일(.exe) 실행
3. 설치 완료 후 실행 → Claude 계정으로 로그인

확인 방법: 채팅 화면이 뜨면 성공.

---

## 2. Node.js 설치

GitHub MCP 서버는 `npx`라는 도구로 실행되며, 이는 Node.js에 포함되어 있습니다.

1. https://nodejs.org 접속
2. **LTS** 버전(좌측, 권장 표시) 다운로드 → 설치
3. 설치 중 "Add to PATH" 옵션은 **체크된 상태 그대로** 두기

### 확인
1. 시작 메뉴에서 **"PowerShell"** 검색해 실행
2. 다음 명령 입력 후 엔터:
   ```powershell
   node -v
   ```
3. `v20.x.x` 같은 버전 번호가 나오면 성공

---

## 3. GitHub Personal Access Token (PAT) 발급

Claude가 GitHub에 접근하려면 비밀번호 대신 사용할 토큰이 필요합니다.

1. https://github.com/settings/tokens 접속
2. 우측 상단 **Generate new token** → **Generate new token (classic)** 클릭
3. 아래 설정 입력:
   - **Note**: `Claude Desktop MCP`
   - **Expiration**: `90 days` (3개월마다 갱신)
   - **Scopes**: **`repo`** 하나만 체크 (전체 저장소 읽기/쓰기)
4. 맨 아래 **Generate token** 클릭
5. 화면에 나타나는 `ghp_` 로 시작하는 긴 문자열을 **즉시 복사** (이 화면을 닫으면 다시 볼 수 없음)
6. 비밀번호 관리자(1Password, Bitwarden) 또는 안전한 메모장에 임시 저장

> 경고: 이 토큰은 비밀번호와 같습니다. 누구에게도 공유하지 말고, GitHub에 절대 커밋하지 마세요.

---

## 4. Claude Desktop 설정 파일 편집

### 4-1. 설정 파일 위치 열기

1. 파일 탐색기 실행
2. 주소창에 다음을 입력하고 엔터:
   ```
   %APPDATA%\Claude
   ```
3. `Claude` 폴더가 열림. `claude_desktop_config.json` 파일을 찾으세요.
4. 파일이 **없으면** 새로 만들기:
   - 빈 공간 우클릭 → **새로 만들기** → **텍스트 문서**
   - 이름을 `claude_desktop_config.json`으로 변경 (확장자 `.txt` 제거)

### 4-2. 내용 붙여넣기

`claude_desktop_config.json`을 **메모장**으로 열고 아래 내용을 통째로 붙여넣습니다:

```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "ghp_여기에_3단계에서_복사한_토큰"
      }
    }
  }
}
```

### 4-3. 토큰 바꿔치기

`ghp_여기에_3단계에서_복사한_토큰` 부분을 3단계에서 복사해 둔 실제 토큰(`ghp_AbCdEf1234...`)으로 바꿉니다.

> 주의: 따옴표(`"`)와 중괄호(`{}`)는 그대로 두고 토큰 문자열만 교체.

### 4-4. 저장
- 메뉴 **파일** → **다른 이름으로 저장**
- **파일 형식**: "모든 파일"로 변경
- **인코딩**: UTF-8
- 파일명 `claude_desktop_config.json` 그대로 저장

---

## 5. Claude Desktop 완전 재시작

설정 파일은 앱 시작 시에만 읽힙니다.

1. 화면 우하단 **시스템 트레이**(시계 옆 작은 화살표)에서 Claude 아이콘 찾기
2. 우클릭 → **Quit** 또는 **종료**
3. 다시 시작 메뉴에서 Claude Desktop 실행

---

## 6. 동작 검증

새 채팅을 열고 다음 질문을 차례로 입력해 확인:

| 테스트 | 입력 | 기대 결과 |
|--------|------|----------|
| 1. 저장소 목록 | `내 GitHub 저장소 목록 보여줘` | `100_test` 포함 목록 |
| 2. 파일 읽기 | `100_test 저장소의 README.md 내용을 보여줘` | README 내용 표시 |
| 3. 파일 쓰기 | `100_test에 hello.md 파일을 만들고 "안녕하세요" 라고 써줘` | https://github.com/Yonghoon-Byun/100_test 에서 hello.md 확인됨 |
| 4. 이슈 생성 | `100_test에 "테스트 이슈" 라는 제목으로 이슈를 만들어줘` | 이슈 #1 생성됨 |

### 도구 사용 권한 요청이 나오면?
Claude가 GitHub에 무언가 쓸 때마다 "이 도구를 사용해도 될까요?" 같은 권한 요청 팝업이 나옵니다.
- **Allow once**: 이번 한 번만 허용
- **Allow always**: 앞으로 항상 허용 (편리)

처음에는 한 번씩 허용하며 어떤 동작인지 확인하는 것을 권장합니다.

---

## 7. 일상 사용 예시

설정이 끝났다면 이제 채팅으로 자유롭게 GitHub을 다룰 수 있습니다:

### 문서 관리
- `100_test의 docs 폴더에 "2026-05-28-주간회의.md" 파일을 만들고 회의 안건을 정리해줘`
- `100_test에 있는 모든 .md 파일 목록 보여줘`
- `README.md를 수정해서 프로젝트 소개를 추가해줘`

### 이슈/할 일 관리
- `이번 주에 할 일을 정리해서 100_test에 이슈로 5개 만들어줘`
- `100_test의 열린 이슈 목록 보여줘`
- `1번 이슈를 닫아줘`

### 코드 검토 (개발자와 협업 시)
- `100_test의 최근 5개 커밋을 요약해줘`
- `main 브랜치의 README.md와 다른 점이 있는지 알려줘`

---

## 8. 정리

이제 당신은:
- ✅ Claude Desktop에서 한국어로 GitHub을 조작할 수 있음
- ✅ 파일·이슈·커밋을 채팅으로 관리 가능
- ✅ 90일 후 PAT 갱신만 잊지 않으면 됨

> 다음 단계:
> - **코드/문서 편집기 안에서** Claude를 쓰고 싶다면 → [02. VSCode + Claude Code](./02-vscode-claude-code.md)
> - **자동 코딩·멀티 에이전트** 자동화가 필요하면 → [03. oh-my-claudecode 설치](./03-oh-my-claudecode-설치.md)

> 문제가 생기면 [99. 트러블슈팅](./99-troubleshooting.md) 참고.
