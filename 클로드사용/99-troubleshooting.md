# 99. 트러블슈팅 — 자주 발생하는 문제와 해결

> 최종 업데이트: 2026-05-28

질문 번호 규칙:
- `Q-D*` = 01번 Claude **D**esktop 관련
- `Q-V*` = 02번 **V**SCode + Claude Code 관련
- `Q-O*` = 03번 **O**MC(oh-my-claudecode) 관련
- `Q-C*` = **C**ommon (공통)

---

## 01번 가이드 (Claude Desktop + GitHub) 관련

### Q-D1. 채팅창에 MCP 도구 아이콘이 안 보여요
**원인**: 설정 파일 문법 오류 또는 Claude Desktop이 완전히 재시작되지 않음.

**해결**:
1. 시스템 트레이에서 Claude를 **완전 종료** (X 버튼이 아닌 Quit)
2. `%APPDATA%\Claude\claude_desktop_config.json`을 https://jsonlint.com 에 붙여넣고 검증
3. 오류 메시지가 나오면 콤마(`,`), 따옴표(`"`), 중괄호(`{}`) 누락 확인
4. 수정 후 Claude Desktop 재시작

---

### Q-D2. "node를 인식할 수 없습니다" 오류
**원인**: Node.js가 설치되지 않았거나 PATH 환경변수에 등록되지 않음.

**해결**:
1. PowerShell에서 `node -v` 입력 → 오류 나면 미설치
2. https://nodejs.org 에서 LTS 버전 재설치
3. 설치 시 **"Add to PATH"** 옵션 반드시 체크
4. 설치 후 **PC 재부팅** (PATH 적용)
5. PowerShell 재실행 → `node -v` 다시 시도

---

### Q-D3. PAT(토큰)가 만료됐어요
**증상**: "Bad credentials" 또는 "401 Unauthorized" 오류.

**해결**:
1. https://github.com/settings/tokens 접속
2. 만료된 토큰 옆 **Regenerate** 클릭 (같은 권한 유지)
3. 새 토큰(`ghp_...`) 즉시 복사
4. `claude_desktop_config.json` 열고 `GITHUB_PERSONAL_ACCESS_TOKEN` 값 교체
5. Claude Desktop 재시작

> 팁: 캘린더에 90일마다 알림 등록.

---

### Q-D4. "npx를 찾을 수 없음" 오류
**원인**: Node.js는 설치되었으나 npm/npx 경로 미인식.

**해결**:
1. PowerShell 관리자 권한 실행
2. `npm install -g npm@latest` 입력 → npm 업데이트
3. PowerShell 새 창 열기 → `npx --version` 확인

---

### Q-D5. Claude가 GitHub 작업을 거절해요
**증상**: "I don't have permission to do that" 답변.

**원인**: PAT 스코프(권한)가 부족.

**해결**:
1. https://github.com/settings/tokens 에서 사용 중 토큰 찾기
2. **Edit** 클릭
3. **`repo`** 전체가 체크되어 있는지 확인 (하위 항목 모두)
4. **Update token** 클릭

---

## 02번 가이드 (VSCode + Claude Code) 관련

### Q-V1. Spark(✱) 아이콘이 안 보여요
**해결**:
1. **파일을 하나 열기** — Editor Toolbar 아이콘은 파일이 열린 상태에서만 표시됨
2. VSCode 버전 확인 (`Help → About`, 1.98.0 이상)
3. 명령 팔레트(`Ctrl+Shift+P`) → `Developer: Reload Window`
4. 다른 AI 확장(Cline, Continue 등) 일시 비활성화
5. **Workspace Trust** 확인 — Restricted Mode에서는 동작 안 함
- 대안: 우하단 **Status Bar**의 `✱ Claude Code` 클릭, 또는 명령 팔레트에서 `Claude Code` 검색

---

### Q-V2. 로그인 화면이 계속 떠요
**원인**: VSCode가 셸 환경변수를 못 읽거나 인증 토큰이 꼬임.

**해결**:
1. 명령 팔레트 → `Developer: Reload Window`
2. 그래도 안 되면 PowerShell에서 `code .`로 프로젝트 열기 (환경변수 상속됨)
3. 채팅창에 `/login` 입력해 강제 재로그인

---

### Q-V3. Claude가 git/gh 명령 실행 권한을 매번 물어봐요
**해결**: 권한 요청 팝업에서 **Allow always** 선택. 또는 채팅창 아래 모드 표시 → **Auto-accept edits** 모드로 전환 (주의: 자동 승인이므로 신뢰하는 작업에서만).

---

### Q-V4. `claude` 명령이 통합 터미널에서 안 먹어요
**원인**: 확장의 번들 CLI 경로가 PATH에 없음.

**해결**:
- 첫 시도: 통합 터미널 새로 열기 (`Ctrl+Shift+` `` ` ``)
- 그래도 안 되면 확장 설정에서 `claudeProcessWrapper` 확인, 또는 별도로:
  ```powershell
  npm install -g @anthropic-ai/claude-code
  ```

---

## 03번 가이드 (oh-my-claudecode) 관련

### Q-O1. `/plugin marketplace add` 명령이 안 먹어요
**원인**: Claude Code 버전이 너무 오래됨.

**해결**:
```powershell
npm install -g @anthropic-ai/claude-code@latest
```
Claude Code를 닫고 다시 실행 → 명령 재시도.

---

### Q-O2. npm install 시 EACCES / 권한 오류
**원인**: 일반 사용자 권한으로 글로벌 패키지 설치 시도.

**해결**:
1. 시작 메뉴에서 **PowerShell** 검색
2. 우클릭 → **관리자 권한으로 실행**
3. 동일 명령 재실행:
   ```powershell
   npm install -g oh-my-claude-sisyphus
   ```

---

### Q-O3. `claude` 명령이 없다고 나와요
**원인**: npm 글로벌 경로가 PATH에 없음.

**해결**:
1. PowerShell에서:
   ```powershell
   npm config get prefix
   ```
   출력된 경로 복사 (예: `C:\Users\admin\AppData\Roaming\npm`)
2. Windows 검색 → "환경 변수 편집"
3. **Path** 변수 편집 → **새로 만들기** → 복사한 경로 붙여넣기
4. 확인 → PC 재부팅 또는 PowerShell 재실행

---

### Q-O4. `/omc-doctor`가 빨간 X를 표시해요
**해결**: 표시된 항목별로 진단 메시지가 함께 나옵니다. 주요 패턴:
- **"Plugin not registered"** → `/plugin install oh-my-claudecode` 재실행
- **"Config missing"** → `/omc-setup` 다시 실행
- **"Outdated version"** → `/plugin marketplace update omc`

---

### Q-O5. autopilot이 도중에 멈춰요
**해결**:
1. `/cancelomc` 입력해 강제 종료
2. `/omc-doctor`로 상태 점검
3. 더 작은 단위로 쪼개서 재시도:
   - ❌ `autopilot: 전체 쇼핑몰 만들어줘` (너무 큼)
   - ✅ `autopilot: 상품 목록 페이지 하나 만들어줘`

---

## 공통

### Q-C1. 한국어 답변이 깨져요
**원인**: PowerShell 기본 인코딩이 CP949일 수 있음.

**해결**:
PowerShell 프로필에 추가:
```powershell
notepad $PROFILE
```
열린 파일에 한 줄 추가:
```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
```
저장 후 PowerShell 재시작.

---

### Q-C2. 어디에 질문해야 하나요?
- **이 가이드 관련**: 이 저장소의 Issues 탭 (https://github.com/Yonghoon-Byun/100_test/issues)
- **Claude Desktop 자체**: https://support.anthropic.com
- **Claude Code (VSCode 확장 포함)**: https://github.com/anthropics/claude-code/issues
- **oh-my-claudecode**: https://github.com/Yeachan-Heo/oh-my-claudecode/issues
- **GitHub MCP 서버**: https://github.com/modelcontextprotocol/servers/issues

---

## 안전 수칙 (자주 까먹는 것)

- ⚠️ **PAT를 GitHub에 커밋하지 마세요** — `.gitignore`에 토큰 포함 파일 등록
- ⚠️ **`claude_desktop_config.json`을 공유하지 마세요** — 토큰이 들어있음
- ⚠️ **PAT 만료일을 캘린더에 등록** — 갑자기 안 될 때 당황 방지
- ⚠️ **`autopilot:`을 실제 작업 폴더에서 처음 쓸 때는 조심** — 작은 빈 폴더에서 충분히 연습 후
