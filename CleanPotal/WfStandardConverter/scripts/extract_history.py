"""원본 파일 첫 시트의 '제·개정 현황' 표에서 Rev/일자/사유 추출.
헤더 행에서 '개정','일자','Rev','No' 키워드로 열을 동적 탐지(구분자 ·/. 정규화, 'Rev.'·'REV' 허용).
표가 없으면 'Rev. YYMMDD' 단일 셀(관리계획서) 폴백. 반환: [{"rev","date","reason"}] (오래된→최신)
"""
import re
import openpyxl
def _txt(v):
    return "" if v is None else (v.strftime("%Y.%m.%d") if hasattr(v,"strftime") else str(v)).strip()
def _norm(s):
    for ch in ('·','.',' ','　','\n','\r','\t'): s=s.replace(ch,'')
    return s
def extract_history(path, max_first_sheets=2):
    try: w=openpyxl.load_workbook(path, data_only=True)
    except Exception: return []
    for sn in w.sheetnames[:max_first_sheets]:
        ws=w[sn]
        hdr=None
        for r in range(1, min(ws.max_row,45)+1):
            rowvals=[ws.cell(r,c).value for c in range(1, min(ws.max_column,90)+1)]
            joined=" ".join(_txt(v) for v in rowvals)
            up=joined.upper(); nj=_norm(joined)
            if ("개정" in nj) and ("일자" in nj) and ("REV" in up) and (("NO" in up) or ("순번" in nj) or ("번호" in nj)):
                hdr=r; break
        if hdr is None: continue
        col={}
        for c in range(1, min(ws.max_column,90)+1):
            v=_txt(ws.cell(hdr,c).value); nv=_norm(v).upper()
            if nv.startswith("REV"): col.setdefault("rev",c)
            elif "일자" in v: col.setdefault("date",c)
            elif "내용" in v: col.setdefault("content",c)
            elif "사유" in v: col.setdefault("reason",c)
        if "rev" not in col and "date" not in col:
            continue
        out=[]; empties=0
        for r in range(hdr+1, ws.max_row+1):
            rev=_txt(ws.cell(r,col["rev"]).value) if col.get("rev") else ""
            date=_txt(ws.cell(r,col["date"]).value) if col.get("date") else ""
            cont=_txt(ws.cell(r,col["content"]).value) if col.get("content") else ""
            rea=_txt(ws.cell(r,col["reason"]).value) if col.get("reason") else ""
            if not any([rev,date,cont,rea]):
                empties+=1
                if out and empties>=2: break
                continue
            empties=0
            if not rev and not date:
                continue
            reason=cont
            if rea: reason=(reason+" - "+rea) if reason else rea
            out.append({"rev":rev.zfill(2) if rev.isdigit() else rev, "date":date, "reason":reason})
        if out: return out
    # 폴백: 표가 없는 관리계획서 등 'Rev. YYMMDD' 단일 셀
    try:
        ws=w[w.sheetnames[0]]
        pat=re.compile(r'Rev\.?\s*([0-9]{1,2})?[\s:]*((?:19|20)?\d{2}[.\-]\s?\d{1,2}[.\-]\s?\d{1,2})')
        for r in range(1, min(ws.max_row,30)+1):
            for c in range(1, min(ws.max_column,90)+1):
                m=pat.search(_txt(ws.cell(r,c).value))
                if m:
                    rev=(m.group(1) or "")
                    d=m.group(2).replace('-','.').replace(' ','')
                    return [{"rev":rev.zfill(2) if rev.isdigit() else rev, "date":d, "reason":""}]
    except Exception: pass
    return []
if __name__=="__main__":
    import sys
    for h in extract_history(sys.argv[1]): print(h)
