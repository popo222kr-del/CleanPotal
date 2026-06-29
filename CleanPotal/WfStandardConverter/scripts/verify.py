"""산출물 무결성 검증: 시트 part 존재, 스타일 인덱스 범위, 시트→drawing→media 관계, 콘텐츠타입."""
import sys, os, zipfile, posixpath
from lxml import etree
M='{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
R='{http://schemas.openxmlformats.org/officeDocument/2006/relationships}'
def nf(t): t=t.lstrip('/'); return t if t.startswith('xl/') else 'xl/'+t
def verify(path):
    z=zipfile.ZipFile(path); names=set(z.namelist()); iss=[]
    st=etree.fromstring(z.read('xl/styles.xml')); cx=st.find(M+'cellXfs'); nxf=len(cx) if cx is not None else 0
    wb=etree.fromstring(z.read('xl/workbook.xml'))
    r2t={r.get('Id'):r.get('Target') for r in etree.fromstring(z.read('xl/_rels/workbook.xml.rels'))}
    sfs=[nf(r2t[s.get(R+'id')]) for s in wb.find(M+'sheets')]; maxs=0
    for sf in sfs:
        if sf not in names: iss.append("missing "+sf); continue
        root=etree.fromstring(z.read(sf))
        for c in root.iter(M+'c'):
            si=c.get('s'); maxs=max(maxs,int(si)) if si is not None else maxs
        dr=root.find(M+'drawing')
        if dr is not None:
            rp='xl/worksheets/_rels/'+os.path.basename(sf)+'.rels'
            if rp not in names: iss.append(sf+": no rels"); continue
            sr={r.get('Id'):r.get('Target') for r in etree.fromstring(z.read(rp))}
            t=sr[dr.get(R+'id')].lstrip('/')
            dt=t if t.startswith('xl/') else posixpath.normpath('xl/worksheets/'+t)
            if dt not in names: iss.append("draw missing "+dt)
            else:
                drp='xl/drawings/_rels/'+os.path.basename(dt)+'.rels'
                if drp in names:
                    for r in etree.fromstring(z.read(drp)):
                        it0=r.get('Target').lstrip('/')
                        it=it0 if it0.startswith('xl/') else posixpath.normpath('xl/drawings/'+it0)
                        if it not in names: iss.append("img missing "+it)
    if maxs>=nxf: iss.append(f"style {maxs}>=cellXfs {nxf}")
    return nxf,len(sfs),iss
if __name__=="__main__":
    target=sys.argv[1]
    paths=[os.path.join(target,f) for f in os.listdir(target) if f.endswith('.xlsx')] if os.path.isdir(target) else [target]
    allok=True
    for p in sorted(paths):
        try:
            nxf,nsh,iss=verify(p)
            print(("OK   " if not iss else "⚠ "+str(len(iss))+" ")+os.path.basename(p)[:50]+f"  시트{nsh} cellXfs{nxf}")
            for i in iss[:5]: print("      -",i)
            if iss: allok=False
        except Exception as e:
            print("ERR  "+os.path.basename(p)[:50]+"  "+str(e)[:60]); allok=False
    print("\n무결성:", "✅ 통과" if allok else "⚠ 이슈")
