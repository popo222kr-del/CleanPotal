"""CleanPotal WPF ↔ wf-standard-converter 연결용 얇은 러너.

명령:
  python wf_run.py list <master.xlsx> [--sheet 시트명] <src_dir1> [src_dir2 ...]
      → 모표준 그룹을 JSON 으로 stdout 출력. WPF 가 TreeView 채우는 데 사용.
        출력: {"sheet": "사용된시트명", "groups": [ {mno, mname, subs:[{subno,oldno,title,src}], issues:[...]} ]}

  python wf_run.py convert <mapping.json> <out_dir>
      → convert.py 오케스트레이터 실행(진행상황은 convert.py 가 stdout 출력).

설계 메모:
- build_mapping.py 의 parse_master 는 시트명이 'WF사업부채번반영' 고정이라,
  여기서 시트 자동 탐색(--sheet → 'WF사업부채번반영' → '채번' 포함 → 첫 시트)을 먼저 처리한다.
- 원본 스킬 스크립트는 건드리지 않는다.
"""
import sys, os, json, re

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)


def _pick_sheet(master_path, requested):
    import openpyxl
    wb = openpyxl.load_workbook(master_path, data_only=True, read_only=True)
    names = list(wb.sheetnames)
    wb.close()
    if requested and requested in names:
        return requested
    if 'WF사업부채번반영' in names:
        return 'WF사업부채번반영'
    for n in names:
        if '채번' in n:
            return n
    return names[0] if names else None


def cmd_list(args):
    # args: <master> [--sheet S] <src_dirs...>
    master = args[0]
    rest = args[1:]
    sheet = None
    src_dirs = []
    i = 0
    while i < len(rest):
        if rest[i] == '--sheet' and i + 1 < len(rest):
            sheet = rest[i + 1]
            i += 2
        else:
            src_dirs.append(rest[i])
            i += 1

    used_sheet = _pick_sheet(master, sheet)
    if not used_sheet:
        print(json.dumps({"error": "시트를 찾을 수 없습니다.", "groups": []}, ensure_ascii=False))
        return

    import openpyxl

    # 컬럼: G(7)=기존No, H(8)=변경No(모표준), I(9)=표준명, J(10)=부속서No, K(11)=세부문서명, L(12)=부서, M(13)=담당자
    ws = openpyxl.load_workbook(master, data_only=True)[used_sheet]

    def cell(r, c):
        v = ws.cell(r, c).value
        return str(v).strip() if v is not None else ''

    groups = []
    cur = None
    for r in range(1, ws.max_row + 1):
        G, H, I, J, K, L, Mo = (cell(r, 7), cell(r, 8), cell(r, 9),
                                cell(r, 10), cell(r, 11), cell(r, 12), cell(r, 13))

        # 헤더/안내 행 스킵 (예: H='실행표준 (변경)', L='부서')
        if H.replace(' ', '') == '실행표준(변경)' or L == '부서':
            continue

        if H:  # H 채워진 행 = 새 모표준 시작
            cur = {'mno': H, 'mname': I, 'dept': L, 'depts': [], 'owners': [], 'subs': [], 'issues': []}
            groups.append(cur)
        if cur is None:
            continue
        # 이 모표준에 속한 모든 행의 부서(L)/담당자(M)를 수집 (섞여 있을 수 있음)
        if L and L not in cur['depts']:
            cur['depts'].append(L)
        if Mo and Mo not in cur['owners']:
            cur['owners'].append(Mo)
        if J or G:  # 부속서(또는 기존No만 있는 행)
            cur['subs'].append({'subno': J or '(미부여)', 'oldno': G, 'title': K, 'owner': Mo})

    # 소스 파일 목록을 미리 수집 (영숫자만 남긴 키 + 파일명 + 경로)
    src_files = _collect_src_files(src_dirs)

    # 소스 파일 매칭 (oldno 가 파일명에 포함된 최신 Rev 파일)
    for grp in groups:
        for s in grp['subs']:
            s['src'] = _match_source(s['oldno'], src_files)
            if s['oldno'] and not s['src']:
                grp['issues'].append('⛔ 소스 미발견: ' + s['oldno'])

    print(json.dumps({"sheet": used_sheet, "groups": groups}, ensure_ascii=False))


def _alnum(s):
    return re.sub(r'[^A-Z0-9]', '', str(s).upper()) if s else ''


def _rev_num(fn):
    m = re.search(r'[Rr]ev[.\s_]*0*(\d+)', fn or '')
    return int(m.group(1)) if m else -1


def _collect_src_files(src_dirs):
    """소스 폴더(하위 포함)의 엑셀 파일을 (영숫자키, 파일명, 전체경로)로 수집."""
    files = []
    for d in src_dirs:
        if not d or not os.path.isdir(d):
            continue
        for root, _dirs, names in os.walk(d):
            for fn in names:
                if not fn.lower().endswith(('.xlsx', '.xls')):
                    continue
                if fn.startswith('~$'):
                    continue
                files.append((_alnum(fn), fn, os.path.join(root, fn)))
    return files


def _match_source(oldno, src_files):
    """기존No(oldno)가 파일명에 포함된 파일 중 최신 Rev 를 반환. 구분자 무시(영숫자 비교)."""
    key = _alnum(oldno)
    if not key:
        return None
    best = None
    best_rev = -2
    for akey, fn, path in src_files:
        idx = akey.find(key)
        if idx < 0:
            continue
        # 숫자 경계 보호: key 바로 뒤가 숫자면 다른 번호(07 vs 070)이므로 제외
        nxt = akey[idx + len(key): idx + len(key) + 1]
        if nxt.isdigit():
            continue
        rv = _rev_num(fn)
        if rv > best_rev:
            best_rev = rv
            best = path
    return best


def cmd_check(args):
    # Python 버전 + 필수 패키지(openpyxl, lxml) 임포트 가능 여부를 JSON 으로 출력
    import platform
    result = {"python": platform.python_version(), "missing": []}
    for pkg in ("openpyxl", "lxml"):
        try:
            __import__(pkg)
        except Exception:
            result["missing"].append(pkg)
    print(json.dumps(result, ensure_ascii=False))


def cmd_convert(args):
    mapping_path, out_dir = args[0], args[1]
    import tempfile
    import convert as CV
    # convert() 기본 tmp 는 '/tmp/_conv' (Windows 부적합) → 시스템 임시폴더 사용
    tmp = os.path.join(tempfile.gettempdir(), "wf_conv_work")
    for r in CV.convert(mapping_path, out_dir, tmp=tmp):
        print(f"✓ {r['mno']}: 부속서{r['subs']} → 출력시트{r['out_sheets']} img{r['out_img']}", flush=True)
        for it in r["issues"]:
            print("    - " + it, flush=True)


def main():
    if len(sys.argv) < 2:
        print(json.dumps({"error": "명령이 필요합니다 (list|convert)"}, ensure_ascii=False))
        sys.exit(1)
    cmd = sys.argv[1]
    try:
        if cmd == "check":
            cmd_check(sys.argv[2:])
        elif cmd == "list":
            cmd_list(sys.argv[2:])
        elif cmd == "convert":
            cmd_convert(sys.argv[2:])
        else:
            print(json.dumps({"error": f"알 수 없는 명령: {cmd}"}, ensure_ascii=False))
            sys.exit(1)
    except Exception as e:
        import traceback
        sys.stderr.write(traceback.format_exc())
        print(json.dumps({"error": str(e), "groups": []}, ensure_ascii=False))
        sys.exit(2)


if __name__ == "__main__":
    main()
