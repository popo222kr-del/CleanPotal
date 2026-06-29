"""변경안(마스터)에서 모표준 매핑 자동 생성 + 소스 파일 자동 탐색.
parse_master: WF사업부채번반영 시트 → 모표준 그룹(mno/mname/subs[subno,oldno,title]).
locate_source: oldno로 src_dirs에서 최신 Rev 파일 경로 탐색.
"""
import os, re, openpyxl
def _norm(s): return re.sub(r'-+','-',str(s).upper().strip().replace('_','-').replace(' ','')) if s else ''
def _rev(fn):
    m=re.search(r'[Rr]ev[.\s]*0*(\d+)', fn or ''); return int(m.group(1)) if m else -1
def _tok(fn):
    n=fn.upper().replace(' ','').replace('_','-')
    m=re.search(r'AQI-?80\d-?(?:GW|SW|W)-?\d+', n) or re.search(r'AW-?C-?\d+', n) or re.search(r'AQP-?\d+(?:-\d+)?', n)
    return _norm(m.group(0)) if m else ''

def parse_master(master_path, sheet='WF사업부채번반영', mno_filter=None):
    ws=openpyxl.load_workbook(master_path, data_only=True)[sheet]
    groups=[]; cur=None
    for r in range(1, ws.max_row+1):
        g=lambda c: (str(ws.cell(r,c).value).strip() if ws.cell(r,c).value is not None else '')
        H,I,J,G,K = g(8),g(9),g(10),g(7),g(11)
        if H:
            cur={'mno':H,'mname':I,'subs':[]}; groups.append(cur)
        if cur is None: continue
        if J or G:
            cur['subs'].append({'subno':J or '(미부여)','oldno':G,'title':K})
    if mno_filter:
        f=_norm(mno_filter); groups=[x for x in groups if _norm(x['mno'])==f]
    return groups

def locate_source(oldno, src_dirs):
    if not oldno: return None
    key=_norm(oldno); best=None; bestrev=-2
    for d in src_dirs:
        if not os.path.isdir(d): continue
        for fn in os.listdir(d):
            if not fn.lower().endswith(('.xlsx','.xls')): continue
            if _tok(fn)==key:
                rv=_rev(fn)
                if rv>bestrev: bestrev=rv; best=os.path.join(d,fn)
    return best

def build_mapping(master_path, src_dirs, mno_filter=None, sheet='WF사업부채번반영'):
    groups=parse_master(master_path, sheet, mno_filter)
    for grp in groups:
        for s in grp['subs']:
            s['src']=locate_source(s['oldno'], src_dirs)
            if s['oldno'] and not s['src']: grp.setdefault('issues',[]).append('⛔ 소스 미발견: '+s['oldno'])
    return groups
