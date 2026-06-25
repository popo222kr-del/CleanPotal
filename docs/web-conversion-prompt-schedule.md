# CleanPotal 웹 전환 개발 프롬프트 — Phase 1: 일정 관리

## 1. 프로젝트 개요

### 1.1 현재 시스템
- **앱 이름:** CleanPotal (세정팀 업무 관리 시스템)
- **현재 플랫폼:** WPF (.NET 8, Windows 데스크톱 앱)
- **사용 환경:** 천안공장 세정팀 (약 20명 이내 사용)
- **데이터베이스:** SQLite (dispatch.db) — 공유 네트워크 드라이브에 위치
  - 운영 경로: `\\10.10.40.98\천안공장\25. 생산 Inform 자료\주언\DATA\dispatch.db`
- **ORM:** Dapper (경량 ORM)
- **인증:** JSON 파일 기반 사용자 관리 (`users.json`)

### 1.2 웹 전환 목표
- 현재 WPF 앱의 **모든 기능**을 웹으로 1:1 전환
- 메뉴 순서대로 단계적 전환 진행
- **Phase 1:** 일정 관리 (세정팀 일정 달력 + 개인 메모장)
- 기존 DB 스키마 및 데이터 100% 호환 유지

### 1.3 전체 메뉴 구조 (전환 순서)
```
[Phase 0 - 공통] 로그인/인증, 사이드바 레이아웃, 권한 시스템
[Phase 1] 일정 관리
  ├─ 세정팀 일정 달력
  └─ 개인 메모장
[Phase 2] 세정 업무 현황판
  ├─ 자재물류 일정 현황
  ├─ 생산 현황판
  └─ 동탄 물류 현황판
[Phase 3] 현장 인수인계
  ├─ 인수인계 현황
  ├─ 주간세정 현황
  ├─ 생산미팅
  ├─ 생산팀 요청사항
  └─ 스케줄 보드
[Phase 4] 현장 점검
  ├─ 재고관리
  └─ 체크시트
[Phase 5] OFFICE 업무
  ├─ 업체 견적서
  ├─ 주간보고
  └─ BROKEN 관리
[Phase 6] 기타 도구
  ├─ 성적서 자동 변환
  ├─ 반출등록 성적서 생성
  └─ 문서 검색
[Phase 7] 관리자 영역
  └─ 사용자 계정 관리
```

---

## 2. 기술 스택 권장사항

### 2.1 프론트엔드
- **프레임워크:** React 또는 Next.js (App Router)
- **UI 라이브러리:** Tailwind CSS (현재 WPF 디자인이 Tailwind 색상 체계를 이미 사용 중)
- **상태 관리:** Zustand 또는 React Context
- **달력 라이브러리:** 커스텀 구현 (기존 WPF 달력이 커스텀이므로 동일하게)
- **HTTP 클라이언트:** Axios 또는 fetch

### 2.2 백엔드
- **프레임워크:** ASP.NET Core Web API (.NET 8) 또는 Node.js (Express/Fastify)
- **DB:** 기존 SQLite 유지 또는 PostgreSQL/MySQL로 마이그레이션
  - SQLite 유지 시: 네트워크 파일 공유 대신 웹 서버가 직접 DB 접근
- **인증:** JWT 토큰 기반 (기존 users.json → DB 테이블로 이관)

### 2.3 디자인 시스템 (기존 WPF 색상 체계 유지)
```
Primary Blue:     #2563EB (버튼, 강조, 선택)
Primary Hover:    #1D4ED8
Light Blue:       #DBEAFE (선택 배경, 뱃지)
Background:       #F8FAFC (전체 배경)
Card Background:  #FFFFFF
Border:           #E2E8F0
Text Dark:        #0F172A (제목, 본문)
Text Medium:      #475569 (부제목)
Text Light:       #64748B (설명)
Text Muted:       #94A3B8 (메타 정보, 비활성)
Success:          #10B981
Error/Danger:     #DC2626 / #EF4444
Sunday:           #DC2626 (빨간색)
Saturday:         #3B82F6 (파란색)
```

---

## 3. Phase 0: 공통 기반 (일정 관리 구현 전 필수)

### 3.1 인증 시스템

#### 사용자 데이터 모델
```typescript
interface User {
  username: string;        // 로그인 ID (예: "1004")
  password: string;        // 해시된 비밀번호
  realName: string;        // 표시 이름 (예: "박주언")
  teamName: string;        // 팀명 (예: "Office", "주간팀", "장팀", "김팀")
  jobTitle: string;        // 직책
  email: string;
  phoneNumber: string;
  employeeNumber: string;  // 사번
  isResigned: boolean;     // 퇴사 여부
  // 권한 플래그
  canManageFiles: boolean;
  canManageNotices: boolean;
  canManageVendors: boolean;
  canManageSchedule: boolean;  // 일정 관리 관리자 권한
  canManageBroken: boolean;
  canAccessEtcMenu: boolean;
  canManageInventory: boolean;
}
```

#### 권한 체계
- **마스터 계정:** username === "1004" → 모든 기능 접근 가능
- **일정 관리 권한 (`PermissionType.Schedule`):** 모든 사용자 접근 가능 (권한 체크 없음)
- **일정 편집 권한 (세부):**
  - 근무 일정 편집: Office팀 또는 마스터 또는 주간팀 또는 `canManageSchedule=true`
  - 팀 일정(이벤트) 편집: Office팀 또는 마스터만
  - 교육 일정 편집: Office팀 또는 마스터 또는 `canManageSchedule=true`

#### 인증 플로우
1. 로그인 → JWT 토큰 발급 (username, realName, teamName, permissions 포함)
2. 클라이언트에서 토큰 저장 (localStorage 또는 httpOnly cookie)
3. API 요청 시 토큰 첨부
4. 자동 로그인 지원 (토큰 만료 전 자동 갱신)

### 3.2 레이아웃

#### 사이드바 구조
```
┌──────────────────────────────────────────────┐
│ [로고/앱명]  CleanPotal                       │
│                                              │
│ MAIN                                         │
│  📄 업무 파일 통합 관리                        │
│                                              │
│ WORKSPACE                                    │
│  📊 세정 업무 현황판  ▼                        │
│    ├ 자재물류 일정 현황                         │
│    ├ 생산 현황판                               │
│    └ 동탄 물류 현황판                          │
│  📅 일정관리  ▼                               │
│    ├ 세정팀 일정 달력                          │
│    └ 개인 메모장                              │
│  📋 현장 인수인계  ▼                          │
│    ├ 인수인계 현황                             │
│    ├ 주간세정 현황                             │
│    ├ 생산미팅                                  │
│    ├ 생산팀 요청사항  [뱃지: 미읽음 수]         │
│    └ 스케줄 보드                              │
│  🔍 현장 점검  ▼                              │
│    ├ 재고관리                                  │
│    └ 체크시트                                 │
│  🏢 OFFICE 업무  ▼  (Office팀/마스터만 표시)    │
│    ├ 업체 견적서                               │
│    ├ 주간보고                                  │
│    └ BROKEN 관리                              │
│                                              │
│ TOOLS                                        │
│  ⚙️ 기타  ▼                                  │
│    ├ 성적서 자동 변환                          │
│    ├ 반출등록 성적서 생성                       │
│    └ 문서 검색                                │
│                                              │
│ ADMIN (마스터만 표시)                          │
│  🔐 관리자 영역  ▼                            │
│    └ 사용자 계정 관리                          │
│                                              │
│ ──────────────────                           │
│ [아바타] 사용자 이름                           │
│ 팀명 · 직책                                   │
│ [계정 편집] [로그아웃]                         │
│ v1.0.0                                       │
└──────────────────────────────────────────────┘
```

- 사이드바 확장 너비: 240px / 축소 너비: 64px (아이콘만)
- 섹션 헤더 (MAIN, WORKSPACE, TOOLS, ADMIN): 축소 시 숨김
- Expander 클릭 시 하위 메뉴 토글 (chevron 애니메이션)
- 활성 메뉴: 배경 #DBEAFE, 텍스트 #2563EB
- 마우스 오버: 배경 #F1F5F9

#### 헤더 영역
- 각 뷰마다 제목 + 설명 + 컨텍스트 버튼 변경
- 일정 달력 헤더: `[◀] 2026년 6월 [▶] [오늘] [⚙️ 패턴 생성] [+ 일정 등록]`

---

## 4. Phase 1-A: 세정팀 일정 달력

### 4.1 개요
월간 달력 형태로 세정팀의 근무 일정(주간/야간/휴무/연차/반차), 교육 일정, 팀 공통 이벤트를 한눈에 보여주는 화면.

### 4.2 DB 스키마

#### 테이블 1: ShiftSchedule (근무 일정)
```sql
CREATE TABLE IF NOT EXISTS ShiftSchedule (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TargetDate TEXT NOT NULL,       -- "yyyy-MM-dd" 형식
    TeamGroup TEXT,                 -- "세정", "주간팀", "장팀", "김팀" 등
    Role TEXT,                      -- "사원" 등 직급
    MemberName TEXT NOT NULL,       -- 직원 실명
    ShiftType TEXT NOT NULL         -- "주간", "야간", "휴무", "연차", "반차", "반반차", "비우기"
);
```

**CRUD 연산:**
- `UpsertShiftSchedule`: 같은 날짜+이름 조합이 있으면 DELETE 후 INSERT (덮어쓰기 패턴)
- `GetShiftSchedulesInRange(start, end)`: 날짜 범위 조회
- `DeleteShiftSchedule(id)`: 단건 삭제
- `UpdateShiftScheduleType(id, newShiftType)`: 근무 유형만 변경

#### 테이블 2: EducationPlan (교육 일정)
```sql
CREATE TABLE IF NOT EXISTS EducationPlan (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MemberName TEXT NOT NULL,
    CourseName TEXT NOT NULL,       -- 교육 과정명
    StartDate TEXT NOT NULL,        -- "yyyy-MM-dd"
    EndDate TEXT NOT NULL,          -- "yyyy-MM-dd"
    Status TEXT,                    -- "대기", "진행중", "완료"
    Progress INTEGER,               -- 0~100 (진행률)
    EduMethod TEXT,                 -- "이러닝", "화상", "집합"
    AttachmentPath TEXT             -- 첨부파일 경로
);
```

**CRUD 연산:**
- `InsertEducationPlan`, `UpdateEducationPlan`
- `GetEducationPlansInRange(start, end)`: StartDate ≤ end AND EndDate ≥ start (겹치는 범위 조회)
- `DeleteEducationPlan(id)`, `UpdateEducationPlanStatus(id, status, progress)`
- `UpdateEducationPlanAttachment(id, path)`
- `GetEducationPlansByMember(memberName)`: 특정 직원의 교육 이력

#### 테이블 3: TeamEvents (팀 공통 일정)
```sql
CREATE TABLE IF NOT EXISTS TeamEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RegisteredBy TEXT NOT NULL,     -- 등록자 실명
    StartDate TEXT NOT NULL,        -- "yyyy-MM-dd"
    EndDate TEXT NOT NULL,          -- "yyyy-MM-dd"
    Content TEXT NOT NULL,          -- 일정 제목/내용
    Detail TEXT                     -- 상세 메모
);
```

**CRUD 연산:**
- `InsertTeamEvent`, `UpdateTeamEvent`, `DeleteTeamEvent`
- `GetTeamEventsInRange(start, end)`: StartDate ≤ end AND EndDate ≥ start

### 4.3 API 엔드포인트 설계

```
# 근무 일정
GET    /api/shifts?start=2026-06-01&end=2026-07-12     → ShiftScheduleModel[]
POST   /api/shifts                                       → { upsert: 같은 날짜+이름 → 덮어쓰기 }
PUT    /api/shifts/:id/type                              → { shiftType: string }
DELETE /api/shifts/:id

# 교육 일정
GET    /api/education?start=2026-06-01&end=2026-07-12   → EducationPlanModel[]
POST   /api/education
PUT    /api/education/:id
DELETE /api/education/:id
PUT    /api/education/:id/status                         → { status, progress }
PUT    /api/education/:id/attachment                     → multipart/form-data
GET    /api/education/member/:name                       → EducationPlanModel[]

# 팀 일정
GET    /api/team-events?start=2026-06-01&end=2026-07-12 → TeamEvent[]
POST   /api/team-events
PUT    /api/team-events/:id
DELETE /api/team-events/:id

# 공휴일
GET    /api/holidays?year=2026                           → { [date: string]: name }

# 사용자 목록 (달력에서 팀/이름 매핑에 필요)
GET    /api/users                                        → UserModel[] (비밀번호 제외)
```

### 4.4 달력 UI 상세

#### 레이아웃
```
┌─────────────────────────────────────────────────────────────────────┐
│ [◀ 이전달]  2026년 6월  [다음달 ▶]  [오늘]  [⚙️ 패턴 생성] [+ 일정 등록] │
├────────┬────────┬────────┬────────┬────────┬────────┬────────┤
│ 일(빨강) │   월   │   화   │   수   │   목   │   금   │ 토(파랑) │
├────────┼────────┼────────┼────────┼────────┼────────┼────────┤
│   31   │    1   │    2   │    3   │    4   │    5   │    6   │
│        │ [현충일]│        │        │        │        │        │
│        │주간: 5  │주간: 5  │주간: 5  │주간: 5  │주간: 5  │        │
│        │야간: 3  │야간: 3  │야간: 3  │야간: 3  │야간: 3  │        │
│        │        │교육: 1  │        │        │        │        │
│        │[팀일정] │        │        │        │        │        │
├────────┼────────┼────────┼────────┼────────┼────────┼────────┤
│   ...  │  ...   │  ...   │  ...   │  ...   │  ...   │  ...   │
└────────┴────────┴────────┴────────┴────────┴────────┴────────┘
```

#### 달력 그리드 (7열 × 6행 = 42셀)
- **컬럼:** 일(Sunday)~토(Saturday)
- **행:** 6주 고정
- 해당 월이 아닌 날짜: 투명도 55% (opacity: 0.55)
- **오늘 셀:** 파란 테두리(#3B82F6, 2px), 배경 #EFF6FF, 날짜 숫자는 파란 원(#2563EB) 안에 흰 글씨

#### 셀 내부 구조
```
┌──────────────────────┐
│ [28]  [현충일]        │  ← 날짜 숫자 + 공휴일 이름 뱃지(빨간 배경)
│ [수원 전체 대청소]     │  ← 팀 일정 헤더 뱃지 (파란 배경 #DBEAFE)
│                      │
│ [주간: 5]            │  ← 주간 근무 뱃지 (노란 배경 #FEF3C7, 글씨 #D97706)
│ [야간: 3]            │  ← 야간 근무 뱃지 (연보라 배경 #E0E7FF, 글씨 #4338CA)
│ [주간 휴무: 1]        │  ← 휴무/연차 뱃지 (회색 배경)
│ [교육: 1]            │  ← 교육 뱃지 (연두 배경 #ECFCCB, 글씨 #65A30D)
└──────────────────────┘
```

#### 뱃지 색상 매핑
| 뱃지 유형 | 배경색 | 글씨색 | 텍스트 형식 |
|----------|--------|--------|------------|
| 주간 근무 | #FEF3C7 | #D97706 | "주간: {count}" |
| 야간 근무 | #E0E7FF | #4338CA | "야간: {count}" |
| 휴무/연차/반차 (주간 소속) | #FEF3C7 | #D97706 | "주간 휴무: {count}" 또는 "주간 연차" 등 |
| 휴무/연차/반차 (야간 소속) | #E0E7FF | #4338CA | "야간 휴무: {count}" 등 |
| 휴무/연차/반차 (일반) | #F1F5F9 | #475569 | "휴무/연차: {count}" |
| 교육 | #ECFCCB | #65A30D | "교육: {count}" |
| 팀 일정 | #DBEAFE | #1D4ED8 | 일정 제목 (14자 초과 시 잘림 + "…", 복수 시 "+ 외 N건") |
| 공휴일 | #FEE2E2 | #DC2626 | 공휴일 이름 |

#### 뱃지 호버 시 툴팁
- **근무 뱃지:** 소속 인원 이름 목록 (줄바꿈으로 구분)
  ```
  김철수
  이영희
  박민수
  ```
- **교육 뱃지:** `[과정명] 이름` 형식
  ```
  [위험물 안전교육] 김철수
  [화학물질 취급] 이영희
  ```
- **팀 일정 뱃지:** 제목 + 상세 내용 + 등록자
  ```
  수원 전체 대청소
    └ 09:00~17:00 전원 참석
  등록자: 박주언
  ```

#### 뱃지 클릭 동작
뱃지 클릭 → 모달/사이드패널 오픈 → 해당 뱃지에 포함된 상세 목록 표시:

**근무 일정 상세 모달:**
```
┌─────────────────────────────────┐
│ 주간 근무 - 2026-06-15          │
│                                 │
│ 이름         유형    [편집]      │
│ ─────────────────────────────── │
│ [장팀] 김철수    주간   [변경▼]   │
│ [장팀] 이영희    주간   [변경▼]   │
│ [김팀] 박민수    주간   [변경▼]   │
│                                 │
│ [변경▼] 클릭 시 드롭다운:        │
│   주간 / 야간 / 휴무 / 연차 /    │
│   반차 / 반반차 / 비우기(삭제)    │
│                                 │
│              [닫기]              │
└─────────────────────────────────┘
```

- **편집 권한 있는 경우:** 각 행에 "변경" 드롭다운 표시 → 근무 유형 즉시 변경 가능
- **편집 권한 없는 경우:** 읽기 전용 (드롭다운 숨김)

#### 휴무 분류 로직 (중요!)
휴무/연차/반차 뱃지는 소속 팀에 따라 주간/야간 그룹으로 분류:
1. 직원의 TeamGroup 확인
2. "주간" 또는 "Office" 포함 → 주간 그룹
3. 해당 팀이 같은 날짜에 주간 근무가 있으면 → 주간 그룹
4. 해당 팀이 같은 날짜에 야간 근무가 있으면 → 야간 그룹
5. 판별 불가 시 인접 ±3일 근무 이력 참조
6. 그래도 판별 불가 시 주간 그룹으로 기본 분류

### 4.5 일정 등록 모달 (ScheduleRegisterWindow)

헤더의 `[+ 일정 등록]` 버튼 클릭 시 모달 오픈. **2개 탭** 구성 (교육 탭은 별도 메뉴에서 접근):

#### 탭 1: 근무 등록
```
┌─────────────────────────────────────┐
│ 일정 등록                           │
│                                     │
│ [근무 등록] [팀 일정]                │
│ ─────────────────────────────────── │
│                                     │
│ 시작일: [2026-06-15]  종료일: [같은일]│
│                                     │
│ 대상자: [콤보박스 ▼]                 │
│   ○ 전체 선택 목록:                  │
│   ☑ [장팀] 김철수                    │
│   ☑ [장팀] 이영희                    │
│   ☐ [김팀] 박민수                    │
│   ...                               │
│                                     │
│ 근무 유형: [주간 ▼]                  │
│   주간 / 야간 / 휴무 / 연차 /        │
│   반차(오전) / 반차(오후) / 반반차     │
│                                     │
│ ⚠ 공휴일 자동 건너뛰기:              │
│   ☑ 공휴일은 자동 제외               │
│                                     │
│        [취소]  [등록]                │
└─────────────────────────────────────┘
```

**동작:**
- 시작일~종료일 범위의 각 날짜에 대해 선택된 인원의 근무 일정을 일괄 등록
- Upsert 패턴: 같은 날짜+이름 조합이 이미 있으면 덮어쓰기
- 공휴일 자동 건너뛰기 옵션: 체크 시 공휴일에는 등록하지 않음
- 대상자 목록: 전체 사용자 목록에서 `[팀명] 이름` 형태로 표시
- 권한 체크: `canEditShifts` (Office팀/마스터/주간팀/canManageSchedule) 만 접근 가능

#### 탭 2: 팀 일정
```
┌─────────────────────────────────────┐
│ 팀 일정 등록                        │
│                                     │
│ 등록자: 박주언 (자동 입력)            │
│                                     │
│ 시작일: [2026-06-15]                │
│ 종료일: [2026-06-15]                │
│                                     │
│ 제목: [수원 전체 대청소]              │
│ 상세: [09:00~17:00 전원 참석 필수]    │
│                                     │
│        [취소]  [등록]                │
└─────────────────────────────────────┘
```

**동작:**
- Office팀 또는 마스터만 접근 가능
- 등록자는 현재 로그인 사용자의 실명 자동 입력
- 시작일~종료일 범위의 모든 날짜에 팀 일정 표시

### 4.6 패턴 생성 (ScheduleProgramWindow)
헤더의 `[⚙️ 패턴 생성]` 버튼 클릭 시 모달 오픈.

**기능:** 반복되는 근무 패턴(예: 주간2일→야간2일→휴무2일)을 레시피로 저장하고, 선택한 인원에게 일괄 적용하는 기능.

- 레시피 CRUD (이름, 패턴 시퀀스)
- 적용 시 시작일 + 대상자 선택 → 패턴에 따라 자동으로 ShiftSchedule 일괄 생성

### 4.7 공휴일 API 연동
- 한국 공공데이터 포털 API 사용 (공휴일 정보)
- 연도별 공휴일 데이터를 서버에서 캐싱
- 달력 렌더링 시 비동기로 공휴일 로드 (초기에는 공휴일 없이 먼저 렌더링 → 로드 완료 후 갱신)

### 4.8 성능 고려사항
- 달력 데이터는 한 번에 42일(6주) 분량만 조회
- 공휴일 데이터는 연도 단위 캐싱
- 월 이동 시 build version 관리로 이전 렌더링 취소 (race condition 방지)

---

## 5. Phase 1-B: 개인 메모장

### 5.1 개요
각 사용자가 개인적으로 메모를 작성/관리하는 기능. 다른 사용자의 메모는 보이지 않음.

### 5.2 데이터 모델

```typescript
interface PersonalNote {
  noteId: string;       // GUID (32자 hex)
  userId: string;       // 로그인 ID (예: "1004")
  title: string;        // 제목
  content: string;      // 본문 (줄바꿈 포함 일반 텍스트)
  createdAt: string;    // ISO datetime
  updatedAt: string;    // ISO datetime
  isDeleted: boolean;   // 소프트 삭제 플래그
  tags: string[];       // 태그 (현재 미사용이지만 스키마에 포함)
}
```

### 5.3 현재 저장 방식 → 웹 전환
**현재 (WPF):** 사용자별 JSON 파일 (`{DataRoot}/personal_notes/{userId}.json`)
**웹 전환 시:** DB 테이블로 이관 권장

```sql
CREATE TABLE IF NOT EXISTS PersonalNotes (
    NoteId TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    Title TEXT DEFAULT '',
    Content TEXT DEFAULT '',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    IsDeleted INTEGER DEFAULT 0,
    Tags TEXT DEFAULT '[]'  -- JSON array
);
CREATE INDEX idx_personal_notes_user ON PersonalNotes(UserId, IsDeleted);
```

### 5.4 API 엔드포인트

```
GET    /api/notes                    → PersonalNote[] (현재 로그인 사용자의 메모만, IsDeleted=false)
POST   /api/notes                    → 새 메모 생성
PUT    /api/notes/:noteId            → 제목/내용 수정
DELETE /api/notes/:noteId            → 소프트 삭제 (IsDeleted=true)
```

- 모든 API는 JWT 토큰에서 userId를 추출하여 해당 사용자의 메모만 접근

### 5.5 UI 상세

#### 전체 레이아웃 (2패널)
```
┌──────────────────────┬───────────────────────────────────────────┐
│  왼쪽 패널 (280px)    │  오른쪽 패널 (나머지)                      │
│                      │                                           │
│  사용자명(ID)         │  [제목 입력 (22pt ExtraBold)]    [저장][삭제]│
│                      │  생성일 2026-06-24   수정일 2026-06-24      │
│  [+ 새 메모]          │  작성자 박주언(1004)                        │
│                      │  ─────────────────────────────────────────│
│  [🔎 제목/본문 검색]   │                                           │
│                      │  본문 입력 영역                             │
│  2026년 06월          │  (14pt, 줄바꿈 지원, 스크롤)                │
│  ┌─────────────────┐ │                                           │
│  │ 회의록 정리   06-24│ │                                           │
│  │ 내일 할 일 목록...│ │                                           │
│  ├─────────────────┤ │                                           │
│  │ 구매 요청    06-23│ │                                           │
│  │ A업체 견적 확인.. │ │                                           │
│  └─────────────────┘ │                                           │
│                      │                                           │
│  2026년 05월          │                                           │
│  ┌─────────────────┐ │                                           │
│  │ 5월 정산      05-31│ │                                           │
│  └─────────────────┘ │                                           │
└──────────────────────┴───────────────────────────────────────────┘
```

#### 왼쪽 패널 (메모 목록)
- **사용자 헤더:** `{실명}({아이디})` (예: "박주언(1004)")
- **새 메모 버튼:** Primary 스타일 (파란색 #2563EB), 전체 너비
  - 클릭 시 "새 메모" 제목으로 생성, 중복 시 "새 메모 1", "새 메모 2" ...
  - 생성 즉시 목록 최상단에 추가 + 자동 선택 + 제목 포커스
- **검색창:** 제목 + 본문 동시 검색 (실시간 필터링)
  - 플레이스홀더: "제목/본문 검색..."
- **메모 목록:** 월별 그룹으로 구분 (UpdatedAt 기준 "yyyy년 MM월")
  - 각 메모 항목: 제목 (13pt SemiBold) + 날짜 (10pt, 오른쪽 "MM-dd") + 미리보기 (11pt, 본문 첫 줄 60자)
  - 선택된 메모: 배경 #DBEAFE
  - 마우스 오버: 배경 #F1F5F9
- **정렬:** UpdatedAt 내림차순 (최근 수정 순)

#### 오른쪽 패널 (편집 영역)
- **메모 미선택 시:** 빈 상태 안내
  ```
  📝
  선택된 메모가 없습니다
  왼쪽에서 메모를 선택하거나 + 새 메모를 클릭하세요
  ```
- **메모 선택 시:**
  - 제목 입력: 22pt ExtraBold, 테두리 없음
  - 액션 버튼: [저장] Primary 스타일, [삭제] Danger 스타일 (빨간 글씨)
  - 저장 상태: "저장되지 않은 변경사항" / "저장됨 · 14:30:25"
  - 메타 정보: 생성일, 수정일, 작성자 (11pt #94A3B8)
  - 구분선 (1px #E5E7EB)
  - 본문 입력: 14pt, 줄바꿈 지원, 흰 배경 카드, 라운드 테두리 (10px)

### 5.6 동작 상세

#### 새 메모 생성
1. 미저장 변경사항 있으면 저장 확인 다이얼로그
2. 제목 중복 회피: "새 메모", "새 메모 1", "새 메모 2" ...
3. 목록 최상단에 추가 + 디스크 저장
4. 자동 선택 + 제목 필드 포커스 + 전체 선택

#### 메모 전환 시
1. 현재 메모에 미저장 변경사항 있으면 저장 확인 (예/아니오/취소)
2. "취소" 선택 시 전환 취소
3. "예" 선택 시 저장 후 전환
4. "아니오" 선택 시 변경사항 버리고 전환

#### 편집 감지
- 제목 또는 본문 텍스트 변경 시 `_isDirty = true`
- 상태 표시: "저장되지 않은 변경사항" (11pt #94A3B8)

#### 저장
- UpdatedAt을 현재 시각으로 갱신
- 목록 재정렬 (수정일 기준)
- 상태 표시: "저장됨 · HH:mm:ss"

#### 삭제
- 확인 다이얼로그: `"{제목}" 메모를 삭제하시겠습니까?`
- IsDeleted = true (소프트 삭제)
- 목록에서 제거 후 다음 메모 자동 선택

#### 페이지 이탈 시
- 미저장 변경사항 있으면 저장 확인 다이얼로그

---

## 6. 공통 UI 컴포넌트

### 6.1 버튼 스타일

| 스타일명 | 배경 | 글씨 | 테두리 | hover 배경 | 용도 |
|---------|------|------|--------|-----------|------|
| Primary | #2563EB | White | #2563EB | #1D4ED8 | 저장, 주요 액션 |
| Ghost | White | #0F172A | #CBD5E1 | #F8FAFC | 보조 액션 |
| Danger | #DC2626 | White | #DC2626 | #B91C1C | 삭제 |
| Nav | White | #0F172A | #CBD5E1 | #F8FAFC | ◀ ▶ 화살표 |

- 모든 버튼: 라운드 8px, cursor: pointer, 13pt SemiBold, padding 14px 7px
- disabled 시: 글씨 #94A3B8, 테두리 #E2E8F0

### 6.2 카드 스타일
```css
.card {
  background: white;
  border: 1px solid #E2E8F0;
  border-radius: 12px;
  padding: 16px;
  margin-bottom: 10px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
}
```

### 6.3 입력 필드
- 배경: White / Transparent
- 테두리: 1px #E2E8F0
- 라운드: 8px
- 포커스: 테두리 #2563EB

### 6.4 모달/다이얼로그
- 배경 오버레이: rgba(0, 0, 0, 0.5)
- 모달 카드: 흰색, 라운드 12px, 최대 너비 적절히 설정
- 제목: 18pt Bold
- 버튼 영역: 오른쪽 정렬 [취소] [확인]

---

## 7. 데이터 마이그레이션 가이드

### 7.1 기존 SQLite DB → 웹 DB
- ShiftSchedule, EducationPlan, TeamEvents 테이블은 스키마 그대로 이관
- 날짜 형식 "yyyy-MM-dd" 유지
- PersonalNotes는 JSON 파일 → DB 테이블로 이관 스크립트 작성 필요

### 7.2 users.json → DB 테이블
```sql
CREATE TABLE IF NOT EXISTS Users (
    Username TEXT PRIMARY KEY,
    Password TEXT NOT NULL,        -- bcrypt 해시
    RealName TEXT NOT NULL,
    TeamName TEXT DEFAULT '',
    JobTitle TEXT DEFAULT '',
    Email TEXT DEFAULT '',
    PhoneNumber TEXT DEFAULT '',
    EmployeeNumber TEXT DEFAULT '',
    IsResigned INTEGER DEFAULT 0,
    CanManageFiles INTEGER DEFAULT 0,
    CanManageNotices INTEGER DEFAULT 0,
    CanManageVendors INTEGER DEFAULT 0,
    CanManageSchedule INTEGER DEFAULT 0,
    CanManageBroken INTEGER DEFAULT 0,
    CanAccessEtcMenu INTEGER DEFAULT 0,
    CanManageInventory INTEGER DEFAULT 0
);
```

---

## 8. 반응형 고려사항

- 사이드바: 768px 이하에서 오버레이 형태로 전환 (햄버거 버튼)
- 달력: 모바일에서도 7열 유지하되 뱃지 텍스트 축소
- 개인 메모장: 모바일에서 목록과 편집 영역 탭 전환
- 최소 지원 해상도: 1280×720 (현재 WPF 앱 기준)

---

## 9. 구현 체크리스트

### Phase 0: 공통 기반
- [ ] 프로젝트 초기 설정 (React/Next.js + Tailwind)
- [ ] 백엔드 API 프로젝트 설정
- [ ] DB 스키마 생성 (Users, 기존 테이블 이관)
- [ ] JWT 인증 구현 (로그인/로그아웃/토큰 갱신)
- [ ] 사이드바 레이아웃 구현 (확장/축소, 메뉴 하이라이트)
- [ ] 헤더 영역 구현 (동적 제목/버튼)
- [ ] 권한 체크 미들웨어

### Phase 1-A: 세정팀 일정 달력
- [ ] 달력 그리드 컴포넌트 (7×6, 월 이동)
- [ ] 뱃지 렌더링 (근무/교육/팀일정/공휴일)
- [ ] 뱃지 클릭 → 상세 모달
- [ ] 근무 유형 변경 (드롭다운)
- [ ] 휴무 분류 로직 (주간/야간 그룹 자동 분류)
- [ ] 일정 등록 모달 (근무 등록 탭)
- [ ] 팀 일정 등록 모달
- [ ] 공휴일 API 연동 + 캐싱
- [ ] 패턴 생성 기능
- [ ] 권한별 UI 분기 (편집 가능/불가)
- [ ] 툴팁 (뱃지 호버 시 인원 목록)

### Phase 1-B: 개인 메모장
- [ ] 2패널 레이아웃 (목록 + 편집)
- [ ] 메모 CRUD API
- [ ] 월별 그룹 목록
- [ ] 실시간 검색 필터링
- [ ] 새 메모 생성 (중복 제목 회피)
- [ ] 메모 편집 + 미저장 감지
- [ ] 저장/삭제 동작
- [ ] 빈 상태 UI
- [ ] 페이지 이탈 시 저장 확인

---

## 10. 테스트 시나리오

### 일정 달력
1. 월 이동 (◀ ▶) 시 데이터 정상 로드
2. 오늘 날짜 하이라이트 확인
3. 공휴일 표시 (빨간색 + 이름)
4. 근무 등록 → 달력에 뱃지 즉시 반영
5. 뱃지 클릭 → 상세 목록 정확히 표시
6. 근무 유형 변경 → DB 반영 + 달력 갱신
7. 팀 일정 등록 → 날짜 범위 모든 셀에 표시
8. 권한 없는 사용자: 편집 UI 숨김 확인
9. 여러 뱃지 동시 표시 (주간+야간+교육+팀일정)
10. 주간/야간 팀 구분 휴무 분류 정확성

### 개인 메모장
1. 새 메모 생성 + 자동 선택
2. 제목/본문 편집 → 미저장 상태 표시
3. 저장 → 수정 시각 갱신 + 목록 재정렬
4. 다른 메모 선택 시 저장 확인 다이얼로그
5. 삭제 → 소프트 삭제 + 다음 메모 자동 선택
6. 검색 → 제목+본문 실시간 필터
7. 빈 상태에서 새 메모 생성
8. 다른 사용자 로그인 시 해당 사용자 메모만 표시
