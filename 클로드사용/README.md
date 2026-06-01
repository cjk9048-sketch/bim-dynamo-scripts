# 비개발자를 위한 Claude 활용 가이드

이 폴더는 비개발자가 **Claude를 일상 업무에 활용하고 GitHub과 연동**하는 방법을 단계별로 안내합니다.

> 최종 업데이트: 2026-05-28

---

## 어디부터 시작할까요?

| 트랙 | 누구를 위한 것 | 무엇을 할 수 있나 | 난이도 |
|------|---------------|------------------|--------|
| [01. Claude Desktop + GitHub](./01-claude-desktop-github.md) | 매일 **채팅**으로 GitHub을 다루고 싶은 사람 | 파일 만들기·이슈 등록·커밋을 한국어 채팅으로 | ★☆☆ |
| [02. VSCode + Claude Code + GitHub](./02-vscode-claude-code.md) | **코드/문서 편집기** 안에서 작업하고 싶은 사람 | 파일 단위 작업·diff 검토·git 자동화 | ★★☆ |
| [03. oh-my-claudecode 설치](./03-oh-my-claudecode-설치.md) | 더 강력한 **자동화**를 원하는 사람 | 자동 코딩·리뷰·플래닝 (멀티 에이전트) | ★★★ |
| [99. 트러블슈팅](./99-troubleshooting.md) | 문제가 생긴 모든 사람 | 자주 발생하는 오류 해결법 | - |

---

## 추천 순서

1. **처음이거나 순수 채팅 사용이면** → [01번 가이드](./01-claude-desktop-github.md)부터. 30분 안에 설정 끝.
2. **파일/문서 작업이 많다면** → [02번 가이드](./02-vscode-claude-code.md). 코드 편집기 안에서 Claude 활용.
3. **자동화·멀티 에이전트가 필요하면** → [03번 가이드](./03-oh-my-claudecode-설치.md). 명령 프롬프트 사용.
4. **막히는 부분이 있으면** → [99번 트러블슈팅](./99-troubleshooting.md) 먼저 검색.

> 01·02·03은 **순차적**이 아니라 **병렬** 트랙입니다. 필요에 따라 골라 쓰거나 함께 쓰세요. 예: Desktop으로 평소 채팅 + VSCode로 코드 작업.

---

## 용어 정리

| 용어 | 풀이 |
|------|------|
| **Claude Desktop** | Anthropic이 만든 채팅 앱 (Windows/Mac). 우리가 평소 쓰는 ChatGPT 같은 화면 |
| **Claude Code** | 같은 회사의 **명령 프롬프트(검은 창)용** 코딩 도우미 |
| **MCP** | Claude가 외부 도구(GitHub 등)와 대화할 수 있게 해주는 다리 역할 규약 |
| **저장소(repository)** | GitHub에서 코드/문서를 모아두는 폴더 |
| **PAT** | Personal Access Token. 비밀번호 대신 쓰는 길고 무작위한 문자열 |

---

## 도움이 필요하면

- 이 저장소: https://github.com/Yonghoon-Byun/100_test
- Issues 탭에서 질문 가능
