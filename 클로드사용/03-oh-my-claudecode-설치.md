# 03. oh-my-claudecode 설치 가이드

Claude Code(명령 프롬프트용 Claude)에 멀티 에이전트 자동화 기능을 추가하는 확장팩 **oh-my-claudecode**(이하 **OMC**) 설치 가이드입니다.

> 대상: 01·02번 트랙에 익숙해진 후 자동화에 관심 있는 사람 / 난이도: ★★★ / 소요 시간: 약 1시간
>
> 공식 저장소: https://github.com/Yeachan-Heo/oh-my-claudecode

---

## 들어가기 전에 — 솔직한 안내

이 트랙은 **명령 프롬프트(검은 창, PowerShell)** 중심입니다. 01·02번 GUI 트랙과 달리:
- 채팅창이 아니라 **터미널**에서 슬래시 명령을 입력
- 코드 편집기(VSCode)를 함께 쓰면 효과 극대화 (02번 트랙 권장)
- 처음에는 낯설지만, 익숙해지면 01·02번보다 훨씬 강력함

**01번/02번 가이드만으로도 충분히 활용 가능하다면 굳이 03번을 진행할 필요는 없습니다.**

---

## OMC가 뭔가요?

세 줄 요약:
1. **Claude Code의 확장팩** — 30개가 넘는 전문 에이전트(플래너, 실행자, 리뷰어 등)를 자동 동원
2. **자동 코딩/리뷰/플래닝** — "autopilot: 할 일 앱 만들어줘" 한 마디로 기획→코딩→테스트까지
3. **무료** — Claude Max/Pro 구독 또는 Anthropic API 키만 있으면 됨

---

## 0. 준비물 체크리스트

- [ ] **Claude Code CLI** 설치 완료 (아래 1번 단계)
- [ ] **Claude Max/Pro 구독** 또는 **Anthropic API 키**
- [ ] Node.js LTS (01번 가이드 2단계에서 이미 설치했다면 OK)
- [ ] Windows PowerShell (Windows에 기본 포함)
- [ ] 선택사항: Gemini CLI, Codex CLI (멀티-AI 협업용)

---

## 1. Claude Code 설치

Claude Code는 Claude Desktop과 **다른** 별개 도구입니다 (명령 프롬프트용).

### 방법 A — npm 설치 (권장)
PowerShell을 관리자 권한으로 실행 후:
```powershell
npm install -g @anthropic-ai/claude-code
```

### 방법 B — 공식 사이트 설치 파일
1. https://claude.ai/download 접속 → Claude Code 탭
2. Windows 설치 파일 다운로드 후 실행

### 확인
PowerShell에서:
```powershell
claude --version
```
버전 번호가 나오면 성공.

### 첫 로그인
프로젝트 폴더로 이동 후:
```powershell
cd D:\DATA\연구원 연도별 업무사항\06_2026년 개발 자료\100_test
claude
```
처음 실행 시 브라우저가 열리며 Claude 계정 로그인 요청 → 완료하면 채팅 프롬프트(`>`) 나타남.

---

## 2. oh-my-claudecode 설치

설치 방법은 **두 가지**입니다. **방법 1(플러그인 마켓)을 강력 추천**합니다.

### 방법 1 — 플러그인 마켓 (★ 권장)

Claude Code 안에서(`>` 프롬프트가 나타난 상태) 다음을 차례로 입력:

```
/plugin marketplace add https://github.com/Yeachan-Heo/oh-my-claudecode
```

엔터 후 마켓 등록 완료 메시지 확인. 그 다음:

```
/plugin install oh-my-claudecode
```

설치가 자동 진행됩니다.

### 방법 2 — npm 글로벌 설치

**중요**: npm 패키지명은 `oh-my-claudecode`가 아니라 **`oh-my-claude-sisyphus`** 입니다.

PowerShell(관리자):
```powershell
npm install -g oh-my-claude-sisyphus
```

설치 후 Claude Code를 재시작하면 OMC 기능이 활성화됩니다.

---

## 3. 초기 설정

설치 직후 한 번만 실행:

```
/omc-setup
```

질문에 답하며 진행:
- 선호하는 실행 모드 (기본값: `ultrawork` — 병렬 실행)
- 알림 통합 (선택사항: Telegram/Discord/Slack)
- 기타 옵션은 기본값 그대로 두어도 OK

설정 파일은 `~/.claude/.omc-config.json`에 저장됩니다.

---

## 4. 동작 검증

### 4-1. 진단 명령
```
/omc-doctor
```
설치 상태·필수 의존성·플러그인 정상 여부를 자동 점검. 모든 항목이 ✓이면 성공.

### 4-2. 첫 자동화 시연

새 빈 폴더로 이동:
```powershell
mkdir D:\temp\omc-test
cd D:\temp\omc-test
claude
```

Claude Code 프롬프트에서:
```
autopilot: build a simple TODO list CLI in Python
```
또는 한국어:
```
autopilot: 간단한 할 일 목록 CLI를 파이썬으로 만들어줘
```

OMC가 **자동으로** 다음을 수행하는 것을 관찰:
1. 계획 수립 (Planner 에이전트)
2. 코드 작성 (Executor 에이전트, 병렬)
3. 검증 (Architect 에이전트)
4. 완성 보고

### 4-3. 요구사항 인터뷰 (선택)

만들고 싶은 것이 모호하다면:
```
/deep-interview "할 일 관리 앱을 만들고 싶어요"
```
OMC가 요구사항을 정리하는 질문을 던집니다.

---

## 5. 자주 쓰는 명령

| 명령 | 용도 | 예시 |
|------|------|------|
| `autopilot: ...` | 전체 자동 실행 | `autopilot: REST API 만들어줘` |
| `/plan ...` | 계획만 먼저 수립 | `/plan 인증 기능 추가` |
| `/ralph ...` | 끝까지 멈추지 않고 실행 | `/ralph 모든 오류 고쳐줘` |
| `/cancelomc` | 진행 중인 OMC 모드 취소 | (그냥 입력) |
| `/omc-doctor` | 설치 진단 | (그냥 입력) |
| `/help` | 도움말 | (그냥 입력) |

전체 스킬 목록은:
```
/oh-my-claudecode:help
```

---

## 6. 업데이트 (정기 권장)

OMC는 자주 업데이트됩니다.

```
/plugin marketplace update omc
/omc-setup
```

월 1회 실행을 권장.

---

## 7. 문제가 생기면

먼저 진단:
```
/omc-doctor
```

그래도 안 되면 [99. 트러블슈팅](./99-troubleshooting.md) 참고하거나, OMC 공식 문서 확인:
- 문서 사이트: https://yeachan-heo.github.io/oh-my-claudecode-website
- 저장소 이슈: https://github.com/Yeachan-Heo/oh-my-claudecode/issues

---

## 8. 정리

이제 당신은:
- ✅ Claude Code CLI를 사용 가능
- ✅ oh-my-claudecode로 자동 코딩/플래닝/리뷰 활용 가능
- ✅ `autopilot:`, `/plan`, `/ralph` 등 핵심 명령 숙지

> 다음 단계:
> - 실제 프로젝트에 `autopilot:` 활용해보기
> - `/plan` → `/ralph` 조합으로 큰 작업 분할 실행
> - OMC 문서 사이트에서 고급 스킬 탐색
