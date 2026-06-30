"""표준변환 오케스트레이터 (정본 표지+재.개정 이력 + 부속서 고충실 이식).
- 표지/재.개정 이력: 공식 템플릿 복사 + 값 치환. 표/박스 테두리는 '패턴(상대 위치)'으로 생성
  (부속서 개수에 따라 행 위치가 자동 이동 — 절대행 하드코딩 없음. 결재/운영목적 등 고정 헤더만 절대).
- 재.개정 이력: 각 부속서 원본 내부 '제·개정 현황'에서 Rev/일자/사유 자동 추출(extract_history).
- 부속서 시트: 원본 시트 OOXML 이식(이미지·서식 보존). 문서번호 셀의 기존No → 신규 부속서No 치환.
- 결재/담당자/일자/보존년한/운영목적 = 빈칸(담당자 기입).

mapping.json:
[ {"mno":"AQI-AW-I801a","mname":"...",
   "subs":[{"subno":"AQI-AW-I801b","oldno":"AQI-804-W-51","src":"/abs/..xlsx","owner":"","note":"모표준 본문"}, ...],
   "issues":[...] } ]
사용: python convert.py mapping.json /out/dir   (임시작업 /tmp 권장)
"""
import sys, os, re, json, zipfile, shutil
from lxml import etree
HERE=os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0,HERE)
import xlsx_merge as XM
from build_cover_from_template import build_workbook
from extract_history import extract_history
TEMPLATE=os.path.join(HERE,"..","templates","모표준_표지_템플릿.xlsx")
COVER_SHEET="모표준_AQI-AW-W104a"; HIST_SHEET="재.개정 이력"

def rev_of(fn):
    m=re.search(r'[Rr]ev[.\s]*0*(\d+)', fn or ""); return m.group(1).zfill(2) if m else ""

def convert(mapping_path, out_dir, tmp="/tmp/_conv", today=None):
    # 표지/재.개정 이력에 찍히는 제·개정 일자 (미지정 시 변환 실행일)
    if not today:
        import datetime; today=datetime.date.today().strftime("%Y.%m.%d")
    groups=json.load(open(mapping_path, encoding="utf-8"))
    if os.path.exists(tmp): shutil.rmtree(tmp)
    os.makedirs(tmp); os.makedirs(out_dir, exist_ok=True)
    # 전역 문서번호 매핑(구No→신No): 모든 그룹의 부속서. 자기 헤더 + 타 표준 상호참조까지 일괄 채번.
    GLOBAL={}
    for g in groups:
        for s in g.get("subs",[]):
            o=(s.get("oldno") or "").strip(); n=(s.get("subno") or "").strip()
            if o and n and "미부여" not in n and "(" not in n:
                GLOBAL.setdefault(o, n)
    report=[]
    for g in groups:
        mno, mname = g["mno"], g["mname"]; subs=g.get("subs",[]); issues=list(g.get("issues",[]))
        # current Rev (파일명 우선, 없으면 src에서)
        for s in subs:
            s["_rev"]=s.get("rev") or rev_of(os.path.basename(s.get("src") or "")) or "—"
            s["_title"]=s.get("title") or (os.path.basename(s["src"]).split(". ",1)[-1].replace(".xlsx","") if s.get("src") else mname+" 본문(신규)")
        sub_meta=[{"no":s["subno"],"title":s["_title"],"rev":s["_rev"],"owner":s.get("owner",""),"note":s.get("note","")} for s in subs]
        # 표지 최종 재.개정 내역(1행/표준)
        hist_flat=[{"stdno":mno,"rev":"00","date":today,"reason":"표준 체계 변경에 따른 통합 신규 제정"}]
        for s in subs: hist_flat.append({"stdno":s["subno"],"rev":s["_rev"],"date":today,"reason":"표준 체계 변경에 따른 부속서 No 신규 생성"})
        # 재.개정 이력 블록(원본 내부 이력 자동 추출)
        blocks=[{"type":"모표준","stdno":mno,"title":mname,
                 "revs":[{"rev":"00","date":today,"reason":"표준 체계 변경에 따른 통합 신규 제정","note":"신규 표준 No 채번"}]}]
        for s in subs:
            h=extract_history(s["src"]) if s.get("src") else []
            revs=[{"rev":x["rev"],"date":x["date"],"reason":x["reason"],"note":s.get("oldno","")} for x in h] \
                 or [{"rev":s["_rev"],"date":"","reason":"(원본 내부 이력 없음 - 담당자 기입)","note":s.get("oldno","")}]
            blocks.append({"type":"부속서","stdno":s["subno"],"title":s["_title"],"revs":revs})
        base=os.path.join(tmp,"_base.xlsx")
        build_workbook(TEMPLATE, COVER_SHEET, HIST_SHEET, mno, mname, sub_meta, hist_flat, blocks).save(base)
        pkg=XM.Pkg(base, os.path.join(tmp,"_work")); tsh=timg=0
        for k,s in enumerate(subs):
            if not s.get("src"): continue
            if not os.path.exists(s["src"]): issues.append("⛔ 소스 없음: "+s["src"]); continue
            if open(s["src"],"rb").read(4)!=b'PK\x03\x04':
                issues.append("🔒 DRM 미해제: "+os.path.basename(s["src"])); continue
            sd=XM.extract(s["src"], os.path.join(tmp,f"_s{k}")); sst=XM.read_sst(sd)
            xfm=pkg.merge_styles(etree.parse(os.path.join(sd,'xl/styles.xml')).getroot())
            rep=dict(GLOBAL)  # 전역 매핑(자기 헤더+상호참조 일괄 치환)
            if s.get("oldno") and s.get("subno") and "미부여" not in s["subno"]:
                rep[s["oldno"]]=s["subno"]
            src_sheets=XM.list_src_sheets(sd)
            # 변경문서 = '작성 방법 안내'(인쇄영역 밖 ※□⇒ 주석 열)가 있는 시트 / 나머지 = 기존문서.
            #   시트명은 문서마다 다르므로 '안내 유무'로만 구분한다.
            # 비교 가능하도록 둘 다 출력:
            #   · 변경문서 → 변환본(부속서No (1)(2)…, 문서번호 치환·안내열 제거·블록삭제)
            #   · 기존문서 → 원본 그대로('기존_' 라벨, 치환/안내제거/삭제 없음)
            # 변경문서(안내 있는 시트)가 하나도 없으면 전체를 변경문서로 처리 — 빈 출력 방지.
            changed=[t for t in src_sheets if XM.sheet_has_guide(sd, t[1], sst)]
            existing=[t for t in src_sheets if t not in changed]
            if not changed: changed=src_sheets; existing=[]
            # 같은 문서의 여러 개정본이 시트로 들어있으면 최신 Rev 만 남기고 옛 Rev 제외.
            # 시트명의 'Rev.N'(rev.05 등)을 읽어 최댓값만 유지. Rev 표기 없는 시트는 유지.
            def _revnum(nm):
                m=re.search(r'[Rr]ev[.\s_]*0*(\d+)', nm or "")
                return int(m.group(1)) if m else None
            revs=[_revnum(t[0]) for t in changed]
            present=[r for r in revs if r is not None]
            if present:
                mx=max(present)
                kept=[t for t,r in zip(changed,revs) if r is None or r==mx]
                if len(kept)<len(changed):
                    issues.append(f"옛 개정본 {len(changed)-len(kept)}개 시트 제외(최신 Rev {mx}만 유지): {os.path.basename(s['src'])}")
                    changed=kept
            # 변경문서: 부속서No (1),(2)... 로 변환 (시트간 수식 참조도 이 맵으로 보정)
            cmap=pkg.plan_sheet_names(s['subno'], changed)
            for i,(nm,pth,state) in enumerate(changed,1):
                pkg.add_sheet(sd, pth, sst, xfm, cmap[nm], replace_map=rep, title=s['_title'],
                              delete_block=(i==1), state=state, sheet_rename=cmap)
            # 기존문서: 비교용 원본 그대로 첨부 (문서번호/안내문구/블록 손대지 않음)
            emap={}
            for (nm,pth,state) in existing: emap[nm]=pkg._reserve("기존_"+nm)
            for (nm,pth,state) in existing:
                pkg.add_sheet(sd, pth, sst, xfm, emap[nm], replace_map=None, title=None,
                              delete_block=False, state=state, sheet_rename=emap, strip_guide=False)
            if existing: issues.append(f"기존문서 {len(existing)}개 시트 비교용 첨부('기존_' 라벨): {os.path.basename(s['src'])}")
            tsh+=len(changed)+len(existing); timg+=sum(1 for n in zipfile.ZipFile(s["src"]).namelist() if 'media' in n)
        pkg.finalize_views()  # 보기 설정: 눈금선 해제·기본 보기·페이지 구분선 제거(표지/이력 포함 전 시트)
        locout=os.path.join(tmp, mno+".xlsx"); pkg.save(locout)
        import openpyxl
        w=openpyxl.load_workbook(locout, read_only=True); nsh=len(w.sheetnames); w.close()
        media=sum(1 for n in zipfile.ZipFile(locout).namelist() if 'media' in n)
        _safe=re.sub(r'[<>:"/\\|?*]','',mname).strip().rstrip('.-> ')
        fname=f"{mno}_{_safe[:28]}.xlsx"
        shutil.copy(locout, os.path.join(out_dir, fname))
        report.append({"mno":mno,"mname":mname,"subs":len(subs),"out_sheets":nsh,"out_img":media,"file":fname,"issues":issues})
    shutil.rmtree(tmp, ignore_errors=True)
    # _convert_report.json 파일은 생성하지 않음 (결과 폴더에 불필요한 파일 방지).
    # 진행 결과는 return 값으로 WPF(wf_run.py)에 전달됨.
    return report

if __name__=="__main__":
    for r in convert(sys.argv[1], sys.argv[2]):
        print(f"✓ {r['mno']}: 부속서{r['subs']} → 출력시트{r['out_sheets']} img{r['out_img']}")
        for i in r["issues"]: print("    -",i)
