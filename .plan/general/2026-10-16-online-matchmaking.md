# 2026-10-16 — 온라인 매칭과 게임 완성

- Date: 2026-10-16
- GitHub Issue: #224
- Status: In progress

## Goal

기존 여섯 종족 RTS를 보완하고 게스트 2인 자동 매칭, 비공개 방 생성/참가,
WebSocket 중계 기반 인터넷 대전을 구현한다. 사용자는 서버가 없으므로 Docker
실행 패키지와 별도 비공개 GitHub 저장소 준비를 선택했다.

## Non-goals

이번 서버는 계정, 랭킹, 결제, 대규모 다중 리전 운영을 제공하지 않는다.
실제 호스팅 계정이 없으므로 공용 배포 성공으로 보고하지 않는다.

## Context / Constraints

전투는 기존 결정론적 2피어 락스텝을 유지한다. 서버는 매칭 및 바이너리
메시지 중계만 담당하며 게임 상태를 판정하지 않는다. UDP 포트포워딩 없이
연결할 수 있도록 클라이언트 양쪽이 WebSocket 연결을 연다.
Hindsight 기록은 서버 DNS 오류로 실패했다. 별도 워크트리는 모두 정리했으며
기본 작업트리에서 파일 소유 범위를 나눠 병렬 작업한다.

## Approach (Checklist)
- [x] **Step 0: Recon**: 현재 main 동기화, 기존 Net/Unity 구조와 미완료 이슈 확인.
- [ ] **Step 1: Implementation**: Server/ 독립 ASP.NET Core 서비스와 Docker;
  Net/ WebSocket transport; Unity 온라인 메뉴와 취소/실패 흐름.
  AI 담당은 #211, 비주얼 담당은 #223을 병렬 수행한다.
- [ ] **Step 2: Tests**: HTTP 매칭·버전 거부·인증·취소·relay 통합 검사,
  기존 Replay/Host/Unity 검사, 실제 두 클라이언트 연결과 화면 확인.
- [ ] **Step 3: Rollout / Rollback**: 비공개 서버 저장소, 실행 가이드, 검증
  근거를 제공한다. 기존 LAN/솔로 기능을 유지한다.

## Validation
- **Commands to run:** `dotnet build`, Replay, Host selfcheck, server checks,
  Unity ViewSelfCheck/ViewShots/BuildPlayer 및 릴레이 두 피어 검사.
- **Expected output:** 두 참가자만 동일 방/seed를 받고 중계 메시지와 최종
  상태 해시가 일치. 취소·상대 종료·잘못된 토큰·버전 불일치를 처리한다.

## Risks & Rollback
- **Risks:** 단일 인스턴스 메모리 매칭은 재시작하면 방이 사라진다. 공개 운영은
  HTTPS/WSS와 리버스 프록시, 호스팅 비용 및 운영 검증이 필요하다.
- **Rollback steps:** 온라인 메뉴를 사용하지 않고 기존 LAN/솔로로 실행;
  서버 프로세스 종료. 독립 커밋 단위로 revert 가능.

## Open Questions
- 호스팅 제공자와 공개 도메인은 미정이다. Docker와 비공개 저장소까지 준비한다.
