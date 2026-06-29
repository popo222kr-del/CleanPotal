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
        for m in list(mc):
            a,b=m.get('ref').split(':'); ar=_rn(a); br=_rn(b)
            if ar>=s and br<=e: mc.remove(m); continue
            def adj(ad):
                r=_rn(ad); return _cp(ad)+str(r-count if r>e else r)
            m.set('ref', adj(a)+':'+adj(b))
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
    def add_sheet(self,sd,spath,sst,xfm,name,replace_map=None,title=None,delete_block=False):
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
        de=root.find(M+'drawing'); sno=self.ns_f(); srel=None
        if de is not None:
            rp=os.path.join(sd,os.path.dirname(spath),'_rels',os.path.basename(spath)+'.rels')
            rid=de.get(R+'id'); dt=None
            if os.path.exists(rp):
                for rel in q(rp).getroot():
                    if rel.get('Id')==rid and 'drawing' in rel.get('Type'): dt=rel.get('Target')
            if dt:
                nd=self._cp_draw(sd,os.path.dirname(spath),dt,_del); de.set(R+'id','rId1'); srel=('rId1','../drawings/drawing%d.xml'%nd)
            else: root.remove(de)
        out='xl/worksheets/sheet%d.xml'%sno
        os.makedirs(self.p('xl/worksheets'),exist_ok=True)
        etree.ElementTree(root).write(self.p(out),xml_declaration=True,encoding='UTF-8',standalone=True)
        if srel:
            rdir=self.p('xl/worksheets/_rels'); os.makedirs(rdir,exist_ok=True)
            rels=etree.Element(PR+'Relationships',nsmap={None:NS['pr']})
            etree.SubElement(rels,PR+'Relationship',Id=srel[0],Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing',Target=srel[1])
            etree.ElementTree(rels).write(os.path.join(rdir,'sheet%d.xml.rels'%sno),xml_declaration=True,encoding='UTF-8',standalone=True)
        etree.SubElement(self.ctypes,CT+'Override',PartName='/'+out,ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml')
        rid='rId%d'%self.nr_f()
        etree.SubElement(self.wbrels,PR+'Relationship',Id=rid,Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet',Target='worksheets/sheet%d.xml'%sno)
        se=etree.SubElement(self.wb.find(M+'sheets'),M+'sheet'); se.set('name',name[:31]); se.set('sheetId',str(self.nsid_f())); se.set(R+'id',rid)
        return out
    def ns_f(self): v=self.ns; self.ns+=1; return v
    def nr_f(self): v=self.nr; self.nr+=1; return v
    def nsid_f(self): v=self.nsid; self.nsid+=1; return v
    def _cp_draw(self,sd,ssub,dt,del_info=None):
        srcd=os.path.normpath(os.path.join(sd,ssub,dt)); nno=self.nd; self.nd+=1
        droot=q(srcd).getroot()
        if del_info: _shift_anchors(droot, *del_info)
        rp=os.path.join(os.path.dirname(srcd),'_rels',os.path.basename(srcd)+'.rels'); nrels=[]
        if os.path.exists(rp):
            for rel in q(rp).getroot():
                typ=rel.get('Type'); tgt=rel.get('Target')
                if 'image' in typ:
                    si=os.path.normpath(os.path.join(os.path.dirname(srcd),tgt)); ext=os.path.splitext(si)[1]
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
        out.append((s.get('name'),t))
    return out
def extract(src,dest):
    if os.path.exists(dest): shutil.rmtree(dest)
    os.makedirs(dest)
    with zipfile.ZipFile(src) as z: z.extractall(dest)
    return dest
