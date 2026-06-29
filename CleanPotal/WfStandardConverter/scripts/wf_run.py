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
import sys, os, json

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

    import build_mapping as BM
    groups = BM.build_mapping(master, src_dirs, sheet=used_sheet)
    print(json.dumps({"sheet": used_sheet, "groups": groups}, ensure_ascii=False))


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
        if cmd == "list":
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
