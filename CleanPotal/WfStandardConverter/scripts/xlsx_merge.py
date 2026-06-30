"""OOXML 시트 이식 엔진.
원본 .xlsx 시트를 대상 워크북에 고충실 이식(스타일 오프셋 병합·공유문자열 inline·drawing/media 복사).
부가: 문서번호 셀 치환, 부속서 첫 시트의 표준명/결재/제개정현황 블록 행삭제(시프트업)+이미지 앵커 보정.
"""
import zipfile, shutil, os, re, copy
from lxml import etree
NS={'main':'http://schemas.openxmlformats.org/spreadsheetml/2006/main',
'r':'http://schemas.openxmlformats.org/officeDocument/2006/relationships',
'ct':'http://schemas.openxmlformats.org/package/2006/content-types',
'pr':'http://schemas.openxmlformats.org/package/2006/relationships'}
M='{%s}'%NS['main']; R='{%s}'%NS['r']; CT='{%s}'%NS['ct']; PR='{%s}'%NS['pr']
XMLSP='{http://www.w3.org/XML/1998/namespace}space'

import re as _re
XDRNS='http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing'
DOCTYPE_SUF=('기준서','표준서','계획서','지침서','절차서')
def _ctext(c):
    is_=c.find(M+'is')
    if is_ is not None:
        t=is_.find(M+'t')
        return (t.text or '') if t is not None else ''
    return ''
def _desp(x): return (x or '').replace(' ','').replace('　','').replace('\n','').replace('\r','').replace('\t','').strip()
def _rn(ref):
    m=_re.search(r'\d+',ref); return int(m.group(0)) if m else 0
def _cp(ref):
    m=_re.match(r'[A-Z]+',ref); return m.group(0) if m else 'A'
def _coln(ref):
    m=_re.match(r'[A-Z]+',ref)
    if not m: return 0
    n=0
    for ch in m.group(0): n=n*26+(ord(ch)-64)
    return n
# 작성 지침(가이드) 표기 기호 — 본문 우측 '작성 방법 안내' 열 식별용
_GUIDE_MARKS=('※','□','⇒','⇨','◇','▶','☞')
def _strip_guide_cols(root):
    """본문 우측의 '작성 방법 안내' 열을 제거.
    판정: 한 열에 지침기호(※□⇒…)가 3개 이상 + 그 열 바로 왼쪽 2개 열이 비어있음
          (= 본문과 빈 간격으로 분리된 우측 주석 블록). 그 열부터 오른쪽 전체 삭제.
    표지/일반 본문(지침 열 없음)은 영향 없음."""
    sd=root.find(M+'sheetData')
    if sd is None: return None
    colmark={}; colused=set()
    for row in sd:
        for c in row.findall(M+'c'):
            ref=c.get('r')
            if not ref: continue
            txt=_ctext(c)
            if txt and txt.strip():
                col=_coln(ref); colused.add(col)
                if any(mk in txt for mk in _GUIDE_MARKS):
                    colmark[col]=colmark.get(col,0)+1
    boundary=None
    for col in sorted(colmark):
        if colmark[col]>=3 and (col-1) not in colused and (col-2) not in colused:
            boundary=col; break
    if boundary is None: return None
    for row in list(sd):
        for c in row.findall(M+'c'):
            ref=c.get('r')
            if ref and _coln(ref)>=boundary: row.remove(c)
    mc=root.find(M+'mergeCells')
    if mc is not None:
        for m in list(mc):
            if _coln(m.get('ref').split(':')[0])>=boundary: mc.remove(m)
        if len(mc): mc.set('count',str(len(mc)))
        else: root.remove(mc)
    cols=root.find(M+'cols')
    if cols is not None:
        for col in list(cols):
            if int(col.get('min','1'))>=boundary: cols.remove(col)
        if not len(cols): root.remove(cols)
    return boundary

def sheet_has_guide(sd, spath, sst):
    """해당 시트에 '작성 방법 안내'(인쇄영역 밖 ※□⇒ 주석 열)가 있는지 판정.
    있으면 변경문서, 없으면 기존문서. 시트명과 무관 — 안내 유무로만 구분.
    공유문자열(t='s')도 해석해서 텍스트를 읽는다."""
    try:
        root=q(os.path.join(sd,spath)).getroot()
    except Exception:
        return False
    colmark={}; colused=set()
    for c in root.iter(M+'c'):
        ref=c.get('r')
        if not ref: continue
        txt=''
        if c.get('t')=='s':
            v=c.find(M+'v')
            if v is not None and v.text is not None:
                try: txt=sst[int(v.text)]
                except Exception: txt=''
        else:
            txt=_ctext(c)
        if txt and txt.strip():
            col=_coln(ref); colused.add(col)
            if any(mk in txt for mk in _GUIDE_MARKS):
                colmark[col]=colmark.get(col,0)+1
    for col in sorted(colmark):
        if colmark[col]>=3 and (col-1) not in colused and (col-2) not in colused:
            return True
    return False
def _set_inline(c, text):
    c.set('t','inlineStr')
    for ch in list(c): c.remove(ch)
    isn=etree.SubElement(c,M+'is'); t=etree.SubElement(isn,M+'t'); t.text=text; t.set(XMLSP,'preserve')
def _find_title_cell(root):
    sd=root.find(M+'sheetData')
    for row in sd:
        if int(row.get('r'))>6: break
        for c in row.findall(M+'c'):
            t=_desp(_ctext(c))
            if t and len(t)<=6 and t.endswith(DOCTYPE_SUF): return c
    return None
_START_LABELS={'표준명','기준명','문서명','기준서명','표준서명','검사기준서명','작업표준서명','관리기준서명'}
def _is_name_label(t):
    if t in _START_LABELS: return True
    return (len(t)<=10) and (t.endswith('준명') or t.endswith('서명'))
def _detect_block(root):
    sd=root.find(M+'sheetData'); s=None; hh=None
    rowmap={int(r.get('r')):r for r in sd}
    for rn in sorted(rowmap):
        for c in rowmap[rn].findall(M+'c'):
            t=_desp(_ctext(c))
            if not t: continue
            if s is None and rn<=20 and _is_name_label(t): s=rn
            if hh is None and rn<=45 and '개정현황' in t: hh=rn
    if s is None or hh is None or s>hh: return None
    e=hh; rr=hh+1
    while rr in rowmap:
        row=rowmap[rr]
        ne=any(_ctext(c).strip() or (c.find(M+'v') is not None) for c in row.findall(M+'c'))
        if ne: e=rr; rr+=1
        else: break
    return (s,e)
def _delete_rows(root, s, e):
    count=e-s+1; sd=root.find(M+'sheetData')
    for row in list(sd):
        rn=int(row.get('r'))
        if s<=rn<=e: sd.remove(row); continue
        if rn>e:
            nn=rn-count; row.set('r',str(nn))
            for c in row.findall(M+'c'):
                ref=c.get('r')
                if ref: c.set('r', _cp(ref)+str(nn))
    mc=root.find(M+'mergeCells')
    if mc is not None:
        # 행 삭제 후 병합셀 끝점 재계산. 삭제구간[s,e]에 걸친 끝점을 올바로 클램프
        # (시작점은 s 로, 끝점은 s-1 로) — 범위 역전(min>max) 방지.
        def newrow(r, is_start):
            if r < s: return r
            if r <= e: return s if is_start else s-1
            return r-count
        for m in list(mc):
            a,b=m.get('ref').split(':'); ar=_rn(a); br=_rn(b)
            nr1=newrow(ar, True); nr2=newrow(br, False)
            if nr1>nr2: mc.remove(m); continue   # 삭제로 완전히 사라진 병합
            m.set('ref', _cp(a)+str(nr1)+':'+_cp(b)+str(nr2))
        if len(mc): mc.set('count',str(len(mc)))
        else: root.remove(mc)
    return count
def _shift_anchors(droot, s, e, count):
    XD='{%s}'%XDRNS
    for anchor in list(droot):
        for tag in ('from','to'):
            el=anchor.find(XD+tag)
            if el is None: continue
            rel=el.find(XD+'row')
            if rel is None or rel.text is None: continue
            dr=int(rel.text); sr=dr+1
            if sr>e: rel.text=str(dr-count)
            elif s<=sr<=e: rel.text=str(max(s-1,0))

def q(p): return etree.parse(p)

def _resolve_target(pkg_root, owner_dir, target):
    """관계(rels) Target 경로를 절대 파일경로로 해석.
    '/xl/..' (패키지 루트 절대, openpyxl) 와 '../drawings/..' (상대, Excel) 모두 지원."""
    if target.startswith('/'):
        return os.path.normpath(os.path.join(pkg_root, target.lstrip('/')))
    return os.path.normpath(os.path.join(owner_dir, target))

def _remap_sheet_refs(formula, changes):
    """수식 내 시트 참조('원본시트'!A1 / 원본시트!A1)를 최종 시트명으로 치환.
    바뀐 시트명만 처리. 새 이름은 특수문자(-, () 등) 포함이 많아 항상 따옴표로 감싼다."""
    for old, new in changes.items():
        qn = "'" + new.replace("'", "''") + "'"
        # 1) 이미 따옴표로 감싼 참조:  'old'!  → 'new'!
        formula = formula.replace("'" + old.replace("'", "''") + "'!", qn + "!")
        # 2) 따옴표 없는 참조:  old!  → 'new'!  (앞이 단어문자/따옴표가 아닐 때만)
        formula = _re.sub(r"(?<![\w'!:])" + _re.escape(old) + r"!", qn + "!", formula)
    return formula

# rels Type 끝부분 → [Content_Types] Override (xml 파트)
_XML_CT={
    'comments':'application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml',
    'table':'application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml',
    'pivottable':'application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml',
    'querytable':'application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml',
    'chart':'application/vnd.openxmlformats-officedocument.drawingml.chart+xml',
    'chartstyle':'application/vnd.ms-office.chartstyle+xml',
    'colors':'application/vnd.ms-office.chartcolorstyle+xml',
}
# 확장자 → Default Content-Type (바이너리/기타 파트)
_DEF_CT={'vml':'application/vnd.openxmlformats-officedocument.vmlDrawing',
    'bin':'application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings',
    'emf':'image/x-emf','wmf':'image/x-wmf','png':'image/png','jpeg':'image/jpeg',
    'jpg':'image/jpeg','gif':'image/gif','bmp':'image/bmp','tiff':'image/tiff'}

class Pkg:
    def __init__(self,base,workdir):
        self.dir=workdir
        if os.path.exists(workdir): shutil.rmtree(workdir)
        os.makedirs(workdir)
        with zipfile.ZipFile(base) as z: z.extractall(workdir)
        self.styles=q(self.p('xl/styles.xml')).getroot()
        self.wb=q(self.p('xl/workbook.xml')).getroot()
        self.wbrels=q(self.p('xl/_rels/workbook.xml.rels')).getroot()
        self.ctypes=q(self.p('[Content_Types].xml')).getroot()
        self.ns=self._nf(); self.nr=self._nr(); self.nsid=self._ns(); self.nd=self._nd(); self.ni=self._ni()
        # 이미 사용 중인 시트명(표지/이력 등) — 부속서 시트명 중복 방지용
        self._used=set(s.get('name') for s in self.wb.find(M+'sheets'))
    def p(self,s): return os.path.join(self.dir,s)
    def _ls(self,s): d=self.p(s); return os.listdir(d) if os.path.isdir(d) else []
    def _nf(self):
        n=0
        for f in self._ls('xl/worksheets'):
            m=re.match(r'sheet(\d+)\.xml$',f); n=max(n,int(m.group(1))) if m else n
        return n+1
    def _nd(self):
        n=0
        for f in self._ls('xl/drawings'):
            m=re.match(r'drawing(\d+)\.xml$',f); n=max(n,int(m.group(1))) if m else n
        return n+1
    def _ni(self):
        n=0
        for f in self._ls('xl/media'):
            m=re.match(r'image(\d+)\.',f); n=max(n,int(m.group(1))) if m else n
        return n+1
    def _nr(self):
        n=0
        for rel in self.wbrels:
            m=re.match(r'rId(\d+)',rel.get('Id')); n=max(n,int(m.group(1))) if m else n
        return n+1
    def _ns(self):
        n=0
        for s in self.wb.find(M+'sheets'): n=max(n,int(s.get('sheetId')))
        return n+1
    def merge_styles(self,src):
        def ch(r,t): return r.find(M+t)
        def kids(r,t):
            e=ch(r,t); return list(e) if e is not None else []
        tgt=self.styles
        def ensure(t):
            e=ch(tgt,t)
            if e is None: e=etree.SubElement(tgt,M+t)
            return e
        offf=len(kids(tgt,'fonts')); offfl=len(kids(tgt,'fills')); offb=len(kids(tgt,'borders'))
        tnf=ensure('numFmts'); ex=[int(n.get('numFmtId')) for n in tnf]; nxt=max(ex+[163])+1; nfm={}
        for n in kids(src,'numFmts'):
            o=int(n.get('numFmtId')); nfm[o]=nxt
            nn=copy.deepcopy(n); nn.set('numFmtId',str(nxt)); tnf.append(nn); nxt+=1
        ef=ensure('fonts')
        for n in kids(src,'fonts'): ef.append(copy.deepcopy(n))
        efl=ensure('fills')
        for n in kids(src,'fills'): efl.append(copy.deepcopy(n))
        eb=ensure('borders')
        for n in kids(src,'borders'): eb.append(copy.deepcopy(n))
        for t in ('fonts','fills','borders','numFmts'):
            e=ch(tgt,t)
            if e is not None and len(e): e.set('count',str(len(e)))
        # dxfs(조건부 서식 차등 스타일) 병합 — 스키마 순서 유지하며 삽입
        offd=len(ch(tgt,'dxfs')) if ch(tgt,'dxfs') is not None else 0
        sdxfs=ch(src,'dxfs')
        if sdxfs is not None and len(sdxfs):
            tdxfs=ch(tgt,'dxfs')
            if tdxfs is None:
                tdxfs=etree.Element(M+'dxfs')
                _ORD=('numFmts','fonts','fills','borders','cellStyleXfs','cellXfs','cellStyles','dxfs','tableStyles','colors','extLst')
                after=_ORD[:_ORD.index('dxfs')]; pos=len(tgt)
                for i,kid in enumerate(tgt):
                    nm=etree.QName(kid).localname
                    if nm not in after: pos=i; break
                tgt.insert(pos,tdxfs)
            for n in list(sdxfs): tdxfs.append(copy.deepcopy(n))
            tdxfs.set('count',str(len(tdxfs)))
        self._dxf_off=offd
        tx=ensure('cellXfs'); offx=len(tx); xfm={}
        for i,xf in enumerate(kids(src,'cellXfs')):
            nx=copy.deepcopy(xf)
            for a,off in (('fontId',offf),('fillId',offfl),('borderId',offb)):
                if nx.get(a) is not None: nx.set(a,str(int(nx.get(a))+off))
            nfid=nx.get('numFmtId')
            if nfid is not None and int(nfid)>=164: nx.set('numFmtId',str(nfm.get(int(nfid),0)))
            tx.append(nx); xfm[i]=offx+i
        tx.set('count',str(len(tx)))
        return xfm
    def _reserve(self, desired):
        base=(desired or 'Sheet').strip()[:31] or 'Sheet'
        cand=base; n=1
        while cand in self._used:
            suf='(%d)'%n; cand=base[:31-len(suf)]+suf; n+=1
        self._used.add(cand); return cand

    def plan_sheet_names(self, subno, src_sheets):
        """부속서의 최종 시트명 계획: 시트가 1개면 부속서No, 여러 개면 'No (1)','No (2)'...
        로 통일 정리. 시트간 수식 참조도 이 맵으로 보정. 반환: {원본시트명: 최종시트명}"""
        m={}; n=len(src_sheets)
        for i,t in enumerate(src_sheets,1):
            nm=t[0]
            desired = subno if n==1 else f"{subno} ({i})"
            m[nm]=self._reserve(desired)
        return m

    def _reg_ct(self, pkgrel, ext, typ):
        ext_l=ext.lstrip('.').lower()
        if ext_l=='xml':
            ct=_XML_CT.get(typ.rsplit('/',1)[-1].lower())
            if ct: etree.SubElement(self.ctypes,CT+'Override',PartName='/'+pkgrel,ContentType=ct)
            return
        have={d.get('Extension') for d in self.ctypes if d.tag==CT+'Default'}
        if ext_l and ext_l not in have:
            etree.SubElement(self.ctypes,CT+'Default',Extension=ext_l,
                             ContentType=_DEF_CT.get(ext_l,'application/octet-stream'))

    def _write_rels(self, folder, base, rels_list):
        rdir=self.p(os.path.join(folder,'_rels')) if folder else self.p('_rels')
        os.makedirs(rdir,exist_ok=True)
        rels=etree.Element(PR+'Relationships',nsmap={None:NS['pr']})
        for rid,typ,tgt,mode in rels_list:
            a={'Id':rid,'Type':typ,'Target':tgt}
            if mode: a['TargetMode']=mode
            etree.SubElement(rels,PR+'Relationship',**a)
        etree.ElementTree(rels).write(os.path.join(rdir,base+'.rels'),
                                      xml_declaration=True,encoding='UTF-8',standalone=True)

    def _cp_part(self, src_root, owner_dir, target_rel, typ):
        """워크시트의 내부 관계 대상(comments/vml/table/printerSettings 등)을 패키지로 복사.
        파일명 충돌 시 자동 유니크화, 파트 자체의 rels(예: vml→media)도 재귀 복사.
        반환: 패키지 루트 기준 상대경로(예: 'xl/comments_2.xml')."""
        src_abs=_resolve_target(src_root,owner_dir,target_rel)
        if not os.path.exists(src_abs): return None
        pkg_rel=os.path.relpath(src_abs,src_root).replace('\\','/')
        folder=os.path.dirname(pkg_rel); base=os.path.basename(pkg_rel); stem,ext=os.path.splitext(base)
        newbase=base; i=2
        while os.path.exists(self.p(os.path.join(folder,newbase) if folder else newbase)):
            newbase='%s_%d%s'%(stem,i,ext); i+=1
        newpkgrel=(folder+'/'+newbase) if folder else newbase
        os.makedirs(self.p(folder) if folder else self.dir,exist_ok=True)
        shutil.copy(src_abs,self.p(newpkgrel))
        self._reg_ct(newpkgrel,ext,typ)
        sub=os.path.join(os.path.dirname(src_abs),'_rels',base+'.rels')
        if os.path.exists(sub):
            child=[]
            for rel in q(sub).getroot():
                rid=rel.get('Id'); rtyp=rel.get('Type'); rtgt=rel.get('Target'); rmode=rel.get('TargetMode')
                if rmode=='External': child.append((rid,rtyp,rtgt,'External')); continue
                cn=self._cp_part(src_root,os.path.dirname(src_abs),rtgt,rtyp)
                if cn:
                    t=os.path.relpath(cn,folder or '.').replace('\\','/')
                    child.append((rid,rtyp,t,None))
            if child: self._write_rels(folder,newbase,child)
        return newpkgrel

    def _strip_relid(self, root, rid):
        """대상이 없는 관계를 참조하는 요소(hyperlink/drawing 등) 제거 — 깨진 참조 방지."""
        for el in list(root.iter()):
            if el.get(R+'id')==rid or el.get(R+'embed')==rid:
                p=el.getparent()
                if p is not None: p.remove(el)

    def add_sheet(self,sd,spath,sst,xfm,name,replace_map=None,title=None,delete_block=False,
                  state=None,sheet_rename=None,strip_guide=True):
        root=q(os.path.join(sd,spath)).getroot()
        for c in root.iter(M+'c'):
            s=c.get('s')
            if s is not None and int(s) in xfm: c.set('s',str(xfm[int(s)]))
            if c.get('t')=='s':
                v=c.find(M+'v')
                if v is not None and v.text is not None:
                    txt=sst[int(v.text)]; c.set('t','inlineStr')
                    for ch in list(c): c.remove(ch)
                    is_=etree.SubElement(c,M+'is'); t=etree.SubElement(is_,M+'t'); t.text=txt; t.set(XMLSP,'preserve')
        for col in root.iter(M+'col'):
            s=col.get('style')
            if s is not None and int(s) in xfm: col.set('style',str(xfm[int(s)]))
        for row in root.iter(M+'row'):
            s=row.get('s')
            if s is not None and int(s) in xfm: row.set('s',str(xfm[int(s)]))
        # 조건부 서식 차등 스타일 인덱스 보정 (dxfs 병합 오프셋)
        doff=getattr(self,'_dxf_off',0)
        if doff:
            for cf in root.iter(M+'cfRule'):
                d=cf.get('dxfId')
                if d is not None: cf.set('dxfId',str(int(d)+doff))
        if replace_map:
            keys=sorted(replace_map, key=len, reverse=True)
            for c in root.iter(M+'c'):
                is_=c.find(M+'is')
                if is_ is not None:
                    tn=is_.find(M+'t')
                    if tn is not None and tn.text and ('AQI' in tn.text or 'AQP' in tn.text):
                        tx=tn.text
                        for o in keys:
                            if o in tx:
                                tx=_re.sub(_re.escape(o)+r'(?![0-9A-Za-z])', replace_map[o], tx)
                        if tx!=tn.text: tn.text=tx
        _del=None
        if title:
            tc=_find_title_cell(root)
            if tc is not None: _set_inline(tc, title)
        if delete_block:
            blk=_detect_block(root)
            if blk:
                cnt=_delete_rows(root, blk[0], blk[1]); _del=(blk[0], blk[1], cnt)
        # 본문 우측 '작성 방법 안내' 열 제거 (관리기준서/작업표준서 공통). 지침 열 없으면 무동작.
        if strip_guide: _strip_guide_cols(root)
        # 시트간 수식 참조: 원본 시트명 → 최종 시트명 치환 (이름이 바뀐 시트만)
        if sheet_rename:
            changes={o:nw for o,nw in sheet_rename.items() if o and nw and o!=nw}
            if changes:
                for c in root.iter(M+'c'):
                    fe=c.find(M+'f')
                    if fe is not None and fe.text and '!' in fe.text:
                        fe.text=_remap_sheet_refs(fe.text,changes)

        sno=self.ns_f()
        out='xl/worksheets/sheet%d.xml'%sno
        # ── 원본 워크시트의 모든 관계(rels) 보존 (drawing/hyperlink/comments/vml/table 등) ──
        ssub=os.path.dirname(spath)
        wsdir=os.path.dirname(os.path.join(sd,spath))   # 원본 워크시트 절대 폴더
        rp=os.path.join(sd,ssub,'_rels',os.path.basename(spath)+'.rels')
        sheet_rels=[]   # (Id, Type, Target, TargetMode|None)
        if os.path.exists(rp):
            for rel in q(rp).getroot():
                rid=rel.get('Id'); typ=rel.get('Type'); tgt=rel.get('Target'); mode=rel.get('TargetMode')
                if mode=='External':                         # 외부 하이퍼링크 등 — 그대로 보존
                    sheet_rels.append((rid,typ,tgt,'External'))
                elif typ.endswith('/drawing'):               # 스프레드시트 drawing(이미지/도형) — 앵커 보정
                    nd=self._cp_draw(sd,ssub,tgt,_del)
                    sheet_rels.append((rid,typ,'../drawings/drawing%d.xml'%nd,None))
                else:                                        # comments/vml/table/printerSettings 등
                    cn=self._cp_part(sd,wsdir,tgt,typ)
                    if cn: sheet_rels.append((rid,typ,os.path.relpath(cn,'xl/worksheets').replace('\\','/'),None))
                    else:  self._strip_relid(root,rid)       # 대상 없음 → 깨진 참조 제거
        else:
            de=root.find(M+'drawing')                        # rels 없는데 drawing 참조만 있으면 제거
            if de is not None: root.remove(de)
        os.makedirs(self.p('xl/worksheets'),exist_ok=True)
        etree.ElementTree(root).write(self.p(out),xml_declaration=True,encoding='UTF-8',standalone=True)
        if sheet_rels: self._write_rels('xl/worksheets','sheet%d.xml'%sno,sheet_rels)
        etree.SubElement(self.ctypes,CT+'Override',PartName='/'+out,ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml')
        rid='rId%d'%self.nr_f()
        etree.SubElement(self.wbrels,PR+'Relationship',Id=rid,Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet',Target='worksheets/sheet%d.xml'%sno)
        se=etree.SubElement(self.wb.find(M+'sheets'),M+'sheet'); se.set('name',name[:31]); se.set('sheetId',str(self.nsid_f())); se.set(R+'id',rid)
        if state in ('hidden','veryHidden'): se.set('state',state)   # 숨김 시트 상태 보존
        return out
    def ns_f(self): v=self.ns; self.ns+=1; return v
    def nr_f(self): v=self.nr; self.nr+=1; return v
    def nsid_f(self): v=self.nsid; self.nsid+=1; return v
    def _cp_draw(self,sd,ssub,dt,del_info=None):
        srcd=_resolve_target(sd,os.path.join(sd,ssub),dt); nno=self.nd; self.nd+=1
        droot=q(srcd).getroot()
        if del_info: _shift_anchors(droot, *del_info)
        rp=os.path.join(os.path.dirname(srcd),'_rels',os.path.basename(srcd)+'.rels'); nrels=[]
        if os.path.exists(rp):
            for rel in q(rp).getroot():
                typ=rel.get('Type'); tgt=rel.get('Target')
                if 'image' in typ:
                    si=_resolve_target(sd,os.path.dirname(srcd),tgt); ext=os.path.splitext(si)[1]
                    nin=self.ni; self.ni+=1; nm='image%d%s'%(nin,ext)
                    os.makedirs(self.p('xl/media'),exist_ok=True); shutil.copy(si,self.p('xl/media/'+nm))
                    nrels.append((rel.get('Id'),'../media/'+nm,typ,ext.lstrip('.').lower()))
        os.makedirs(self.p('xl/drawings'),exist_ok=True); out='xl/drawings/drawing%d.xml'%nno
        etree.ElementTree(droot).write(self.p(out),xml_declaration=True,encoding='UTF-8',standalone=True)
        if nrels:
            rdir=self.p('xl/drawings/_rels'); os.makedirs(rdir,exist_ok=True)
            rels=etree.Element(PR+'Relationships',nsmap={None:NS['pr']})
            for rid,tgt,typ,ext in nrels: etree.SubElement(rels,PR+'Relationship',Id=rid,Type=typ,Target=tgt)
            etree.ElementTree(rels).write(os.path.join(rdir,'drawing%d.xml.rels'%nno),xml_declaration=True,encoding='UTF-8',standalone=True)
        etree.SubElement(self.ctypes,CT+'Override',PartName='/'+out,ContentType='application/vnd.openxmlformats-officedocument.drawing+xml')
        have={d.get('Extension') for d in self.ctypes if d.tag==CT+'Default'}
        mime={'png':'image/png','jpeg':'image/jpeg','jpg':'image/jpeg','gif':'image/gif','emf':'image/x-emf','bmp':'image/bmp','wmf':'image/x-wmf','tiff':'image/tiff'}
        for _,_,_,ext in nrels:
            if ext not in have:
                etree.SubElement(self.ctypes,CT+'Default',Extension=ext,ContentType=mime.get(ext,'application/octet-stream')); have.add(ext)
        return nno
    def finalize_views(self):
        """모든 워크시트에 보기 설정 적용: 눈금선 해제·기본(Normal) 보기·페이지 구분선 제거."""
        order=(M+'sheetData', M+'cols', M+'sheetFormatPr')
        for f in self._ls('xl/worksheets'):
            if not f.endswith('.xml'): continue
            fp=self.p('xl/worksheets/'+f)
            root=q(fp).getroot()
            svs=root.find(M+'sheetViews')
            if svs is None:
                svs=etree.Element(M+'sheetViews')
                sv=etree.SubElement(svs,M+'sheetView'); sv.set('workbookViewId','0')
                idx=len(list(root))
                for i,ch in enumerate(list(root)):
                    if ch.tag in order: idx=i; break
                root.insert(idx, svs)
            for sv in svs.findall(M+'sheetView'):
                sv.set('showGridLines','0')
                v=sv.get('view')
                if v in ('pageBreakPreview','pageLayout') and 'view' in sv.attrib:
                    del sv.attrib['view']
            for tag in ('rowBreaks','colBreaks'):
                e=root.find(M+tag)
                if e is not None: root.remove(e)
            etree.ElementTree(root).write(fp,xml_declaration=True,encoding='UTF-8',standalone=True)
    def save(self,out):
        for root,fn in [(self.styles,'xl/styles.xml'),(self.wb,'xl/workbook.xml'),(self.wbrels,'xl/_rels/workbook.xml.rels'),(self.ctypes,'[Content_Types].xml')]:
            etree.ElementTree(root).write(self.p(fn),xml_declaration=True,encoding='UTF-8',standalone=True)
        if os.path.exists(out): os.remove(out)
        with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
            for dp,_,fns in os.walk(self.dir):
                for f in fns:
                    full=os.path.join(dp,f); z.write(full,os.path.relpath(full,self.dir))
def read_sst(sd):
    p=os.path.join(sd,'xl/sharedStrings.xml')
    if not os.path.exists(p): return []
    return [''.join(t.text or '' for t in si.iter(M+'t')) for si in q(p).getroot()]
def list_src_sheets(sd):
    wb=q(os.path.join(sd,'xl/workbook.xml')).getroot()
    r2t={r.get('Id'):r.get('Target') for r in q(os.path.join(sd,'xl/_rels/workbook.xml.rels')).getroot()}
    out=[]
    for s in wb.find(M+'sheets'):
        t=r2t[s.get(R+'id')].lstrip('/')
        if not t.startswith('xl/'): t='xl/'+t
        out.append((s.get('name'),t,s.get('state')))   # state: None/'hidden'/'veryHidden'
    return out
def extract(src,dest):
    if os.path.exists(dest): shutil.rmtree(dest)
    os.makedirs(dest)
    with zipfile.ZipFile(src) as z: z.extractall(dest)
    return dest
