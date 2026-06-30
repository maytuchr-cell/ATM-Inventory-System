"""
ATM Inventory — Clean ERD (orthogonal lines + per-field descriptions), grouped by domain.
3 slides: Master Data · Stock & Ledger · Transactions
"""
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_CONNECTOR

NAVY=RGBColor(0x1C,0x35,0x57); BLUE=RGBColor(0x25,0x63,0xEB); TEAL=RGBColor(0x0D,0x94,0x88)
AMBER=RGBColor(0xB4,0x53,0x09); PURPLE=RGBColor(0x53,0x4A,0xB7); GREY=RGBColor(0x64,0x74,0x80)
GREEN=RGBColor(0x05,0x96,0x69); WHITE=RGBColor(0xFF,0xFF,0xFF); TEXT=RGBColor(0x1E,0x2D,0x40)
MUTED=RGBColor(0x6B,0x72,0x80); LIGHT=RGBColor(0xF5,0xF7,0xFA); ORANGE=RGBColor(0xF5,0xA6,0x23)
ORANGED=RGBColor(0xD8,0x5A,0x30); GREY_L=RGBColor(0xD6,0xDA,0xE0); ROW_ALT=RGBColor(0xF6,0xF8,0xFB)

prs=Presentation(); prs.slide_width=Inches(13.333); prs.slide_height=Inches(7.5)

def slide():
    s=prs.slides.add_slide(prs.slide_layouts[6])
    f=s.background.fill; f.solid(); f.fore_color.rgb=LIGHT
    return s
def box(s,x,y,w,h,fill,line=None,lw=0.75,radius=False):
    sh=s.shapes.add_shape(5 if radius else 1,Inches(x),Inches(y),Inches(w),Inches(h))
    sh.fill.solid(); sh.fill.fore_color.rgb=fill
    if line: sh.line.color.rgb=line; sh.line.width=Pt(lw)
    else: sh.line.fill.background()
    sh.shadow.inherit=False; return sh
def txt(s,t,x,y,w,h,size=9,bold=False,color=TEXT,align=PP_ALIGN.LEFT,italic=False,vmid=False,font="Calibri"):
    tb=s.shapes.add_textbox(Inches(x),Inches(y),Inches(w),Inches(h)); tf=tb.text_frame; tf.word_wrap=True
    tf.margin_left=Inches(0.02);tf.margin_right=Inches(0.02);tf.margin_top=Inches(0.0);tf.margin_bottom=Inches(0.0)
    if vmid: tf.vertical_anchor=MSO_ANCHOR.MIDDLE
    p=tf.paragraphs[0]; p.alignment=align; r=p.add_run(); r.text=t
    r.font.size=Pt(size); r.font.bold=bold; r.font.italic=italic; r.font.color.rgb=color; r.font.name=font
    return tb
def header(s,title,sub):
    box(s,0,0,13.333,0.62,NAVY); box(s,0,0.62,13.333,0.03,ORANGE)
    txt(s,title,0.3,0.06,9.5,0.5,size=17,bold=True,color=WHITE,vmid=True)
    txt(s,sub,0.32,0.66,12,0.22,size=9,color=MUTED,italic=True)

ENT={}
def entity(s,key,x,y,w,clr,bgc,fields,title=None):
    """fields: list of (keytag, 'FieldName', 'คำอธิบาย')"""
    rh=0.205; h=0.32+len(fields)*rh
    box(s,x,y,w,h,bgc,clr,1.25,radius=True)
    box(s,x,y,w,0.30,clr)
    txt(s,title or key,x+0.1,y+0.02,w-0.15,0.27,size=10,bold=True,color=WHITE,vmid=True)
    yy=y+0.33
    for i,(ic,fn,desc) in enumerate(fields):
        if i%2==1: box(s,x+0.03,yy,w-0.06,rh,ROW_ALT)
        if ic:
            kc={"PK":ORANGED,"UK":PURPLE,"FK":GREEN}.get(ic,MUTED)
            txt(s,ic,x+0.07,yy,0.3,rh,size=6.5,bold=True,color=kc,vmid=True)
        txt(s,fn,x+0.4,yy,w*0.42,rh,size=8,bold=(ic=="PK"),color=TEXT,vmid=True,font="Consolas")
        txt(s,desc,x+0.4+w*0.42,yy,w*0.58-0.45,rh,size=7.5,color=MUTED,vmid=True)
        yy+=rh
    ENT[key]=(x,y,w,h); return (x,y,w,h)

def edge(s,a,b,label=None,clr=GREY):
    ax,ay,aw,ah=ENT[a]; bx,by,bw,bh=ENT[b]
    acx,acy=ax+aw/2,ay+ah/2; bcx,bcy=bx+bw/2,by+bh/2
    # connect on facing sides, elbow routing for tidy right angles
    if abs(acx-bcx) >= abs(acy-bcy):
        x1=ax+aw if bcx>acx else ax; y1=acy
        x2=bx    if bcx>acx else bx+bw; y2=bcy
    else:
        x1=acx; y1=ay+ah if bcy>acy else ay
        x2=bcx; y2=by    if bcy>acy else by+bh
    cn=s.shapes.add_connector(MSO_CONNECTOR.ELBOW,Inches(x1),Inches(y1),Inches(x2),Inches(y2))
    cn.line.color.rgb=clr; cn.line.width=Pt(1.6); cn.shadow.inherit=False
    # Direction markers: round dot at the "1" (parent) end, big arrow head at the "N" (child) end.
    from pptx.oxml.ns import qn
    ln=cn.line._get_or_add_ln()
    head=ln.makeelement(qn('a:headEnd'),{'type':'oval','w':'med','len':'med'})      # parent side (1)
    tail=ln.makeelement(qn('a:tailEnd'),{'type':'triangle','w':'lg','len':'lg'})     # child side (N) → arrow
    ln.append(head); ln.append(tail)
    if label:
        txt(s,label,(x1+x2)/2-0.3,(y1+y2)/2-0.12,0.7,0.2,size=7.5,bold=True,color=clr,italic=True,align=PP_ALIGN.CENTER)

def legend(s):
    y=7.15; x=0.4
    for tag,clr,d in [("PK",ORANGED,"Primary Key"),("UK",PURPLE,"Unique"),("FK",GREEN,"Foreign Key")]:
        txt(s,tag,x,y-0.02,0.35,0.22,size=8.5,bold=True,color=clr,vmid=True); x+=0.32
        txt(s,d,x,y-0.02,len(d)*0.075+0.3,0.22,size=8.5,color=MUTED,vmid=True); x+=len(d)*0.075+0.5
    txt(s,"●—▶ = ความสัมพันธ์ 1 : N  (วงกลม=ฝั่ง 1, หัวลูกศร=ฝั่ง N)",x,y-0.02,5,0.22,size=8.5,color=MUTED,italic=True,vmid=True)

# ══════════════════════════════════ SLIDE 1 — MASTER DATA ══════════════════════════════════
s=slide(); header(s,"Database Design — Master Data","ตารางข้อมูลหลัก + คำอธิบายทุก field   ·   เส้น = FK")

entity(s,"Category",0.35,1.0,2.7,BLUE,RGBColor(0xEE,0xF3,0xFE),[
 ("PK","Id","รหัส (auto)"),("UK","Name","ชื่อหมวด (Sub Unit)"),("","Description","รายละเอียด"),("","IsActive","สถานะใช้งาน")])
entity(s,"Vendor",0.35,2.55,2.7,BLUE,RGBColor(0xEE,0xF3,0xFE),[
 ("PK","Id","รหัส"),("UK","Code","รหัสผู้ขาย"),("","Name","ชื่อผู้ขาย"),("","VendorType","GRG/LOCAL"),("","ContactInfo","ติดต่อ")])
entity(s,"Location",0.35,4.35,2.7,BLUE,RGBColor(0xEE,0xF3,0xFE),[
 ("PK","Id","รหัส"),("UK","Code","รหัสคลัง"),("","Name","ชื่อคลัง/จุดเก็บ"),("","LocationType","ประเภท WH/GRG/SCRAP")])

entity(s,"Part",4.3,1.6,3.5,BLUE,RGBColor(0xE3,0xEC,0xFD),[
 ("PK","Id","รหัส (auto)"),("UK","PartNo","รหัสอะไหล่ (business key)"),("","PartName","ชื่อ/คำอธิบาย"),
 ("FK","CategoryId","→ Category (Sub Unit)"),("","Unit / MainUnit","หน่วย / กลุ่มหลัก"),
 ("","Min/Max/Reorder","นโยบายสต็อก"),("","CostPerUnit","ต้นทุน/หน่วย"),("","ImagePath","พาธรูป"),
 ("","IsActive","สถานะ (soft delete)"),("","RowVersion","กันแก้ทับ (concurrency)")],title="Part  (ตัวอะไหล่)")

entity(s,"AtmModel",8.6,1.0,4.3,PURPLE,RGBColor(0xF0,0xEF,0xFA),[
 ("PK","Id","รหัส"),("","ModelCode","รหัสกลุ่ม/รุ่น (Group Code)"),("","ModelName","ชื่อกลุ่ม/รุ่น"),
 ("","Manufacturer","ผู้ผลิต GRG/NCR"),("","IsActive","สถานะ")],title="AtmModel  (ATM Group)")
entity(s,"AtmModelPart",8.6,2.65,4.3,PURPLE,RGBColor(0xF0,0xEF,0xFA),[
 ("PK","Id","รหัส"),("FK","AtmModelId","→ AtmModel"),("FK","PartId","→ Part"),
 ("","PartNo","snapshot — UK(Model,Part)")],title="AtmModelPart  (กลุ่ม↔อะไหล่)")
entity(s,"EquivalentGroup",8.6,4.25,2.05,TEAL,RGBColor(0xE9,0xF6,0xF4),[
 ("PK","Id","รหัส"),("","Name","ชื่อกลุ่มทดแทน"),("","CreatedAt","เวลาสร้าง")])
entity(s,"EquivGroupMember",10.85,4.25,2.05,TEAL,RGBColor(0xE9,0xF6,0xF4),[
 ("PK","Id","รหัส"),("FK","GroupId","→ Group"),("FK","PartId","→ Part"),("","PartNo","snapshot")])

edge(s,"Category","Part","1:N")
edge(s,"AtmModel","AtmModelPart","1:N")
edge(s,"AtmModelPart","Part","N:1")
edge(s,"EquivalentGroup","EquivGroupMember","1:N")
edge(s,"EquivGroupMember","Part","N:1")
legend(s)

# ══════════════════════════════════ SLIDE 2 — STOCK & LEDGER ══════════════════════════════════
ENT.clear()
s=slide(); header(s,"Database Design — Stock & Ledger","สต็อกต่อคลัง · ชิ้นซีเรียล · บัญชีเคลื่อนไหว   ·   เส้น = FK")

entity(s,"Part",0.4,2.5,2.7,BLUE,RGBColor(0xE3,0xEC,0xFD),[
 ("PK","Id","รหัสอะไหล่"),("UK","PartNo","รหัส (business key)"),("","PartName","ชื่อ"),
 ("","(ยอด)","= SUM(PartStock.GoodQty)")],title="Part")
entity(s,"Location",0.4,5.0,2.7,BLUE,RGBColor(0xE3,0xEC,0xFD),[
 ("PK","Id","รหัสคลัง"),("UK","Code","รหัส"),("","Name","ชื่อคลัง")],title="Location")

entity(s,"PartStock",3.95,1.4,3.0,TEAL,RGBColor(0xE9,0xF6,0xF4),[
 ("PK","Id","รหัส"),("FK","PartId","→ Part"),("FK","LocationId","→ Location"),
 ("","GoodQty","จำนวนสภาพดี"),("","DefectiveQty","จำนวนเสีย"),("","UpdatedAt","เวลายอดเปลี่ยน"),
 ("","RowVersion","กันยอดทับ"),("","UQ","(PartId,LocationId)")],title="PartStock  (ยอดในคลัง)")
entity(s,"PartUnit",3.95,4.7,3.0,AMBER,RGBColor(0xFB,0xF1,0xE7),[
 ("PK","Id","รหัส"),("FK","PartId","→ Part"),("FK","LocationId","→ Location"),
 ("UK","SerialNo","ซีเรียล (ห้ามซ้ำ)"),("","Condition","Good/Defective"),
 ("","ExpiryDate","วันหมดอายุ"),("","Status","InStock/Issued/Disposed")],title="PartUnit  (ชิ้นซีเรียล)")

entity(s,"StockMovement",7.8,2.0,5.1,TEAL,RGBColor(0xE9,0xF6,0xF4),[
 ("PK","Id","รหัส"),("","MovementType","GR/Issue/Return/Transfer/Disposal/Adjust"),
 ("FK","PartId","→ Part (Restrict)"),("","PartNo","snapshot ของ PartNo"),
 ("FK","PartUnitId","→ PartUnit (ชิ้นซีเรียล ถ้ามี)"),("","From/ToLocationId","คลังออก/เข้า"),
 ("","Qty / Condition","จำนวน / สภาพ"),("","RefType / RefId","อ้างเอกสารต้นทาง"),
 ("","Cost","ต้นทุน ณ ตอนขยับ"),("","UserName","ใครทำ"),("","Timestamp","เมื่อไหร่")],
 title="StockMovement  (Ledger — บันทึกทุกการขยับ)")

edge(s,"Part","PartStock","1:N")
edge(s,"Location","PartStock","1:N")
edge(s,"Part","PartUnit","1:N")
edge(s,"Location","PartUnit","1:N")
edge(s,"Part","StockMovement","1:N")
edge(s,"PartUnit","StockMovement","1:N")
legend(s)

# ══════════════════════════════════ SLIDE 3 — TRANSACTIONS ══════════════════════════════════
ENT.clear()
s=slide(); header(s,"Database Design — Transactions","รับเข้า · เบิก/คืน · โอน · นับสต็อก · ทำลาย   ·   ทุกตารางอ้าง Part ด้วย FK")

entity(s,"GoodsReceipt",0.35,1.0,3.0,AMBER,RGBColor(0xFB,0xF1,0xE7),[
 ("PK","Id","รหัส"),("","ReceiptNo","เลขใบรับ"),("","Source","GRG/LocalVendor"),
 ("FK","VendorId","→ Vendor"),("FK","LocationId","→ Location"),("","HandlingCost","ค่าจัดการ")],title="GoodsReceipt  (ใบรับเข้า)")
entity(s,"GoodsReceiptLine",0.35,3.3,3.0,AMBER,RGBColor(0xFB,0xF1,0xE7),[
 ("PK","Id","รหัส"),("FK","GoodsReceiptId","→ GoodsReceipt"),("FK","PartId","→ Part"),
 ("","Qty / Condition","จำนวน/สภาพ"),("","SerialNo","ซีเรียล (→ สร้าง PartUnit)")],title="GoodsReceiptLine")

entity(s,"Ticket",0.35,5.3,3.0,GREEN,RGBColor(0xEA,0xF6,0xF0),[
 ("PK","TicketId","รหัส"),("","Tech...","ข้อมูลช่าง"),("","Req/ApprovedPartNo","อะไหล่ที่ขอ/อนุมัติ"),
 ("","Status","Pending/Approved/..."),("","AttachmentPath","รูปอาการเสีย")],title="Ticket  (ใบเบิก)")
entity(s,"ReturnRequest",3.75,5.3,3.0,GREEN,RGBColor(0xEA,0xF6,0xF0),[
 ("PK","Id","รหัส"),("FK","TicketId","→ Ticket (บังคับ)"),("FK","PartId","→ Part"),
 ("","Condition","สภาพที่คืน"),("","Location From/To","ต้นทาง/ปลายทาง")],title="ReturnRequest  (คืน)")

entity(s,"StockTransfer",7.0,1.0,3.0,AMBER,RGBColor(0xFB,0xF1,0xE7),[
 ("PK","Id","รหัส"),("FK","PartId","→ Part"),("","Qty","จำนวน"),
 ("","From/ToLocationId","คลังต้นทาง/ปลายทาง"),("","Status","Pending→Received")],title="StockTransfer  (โอน)")
entity(s,"DisposalRequest",7.0,3.3,3.0,AMBER,RGBColor(0xFB,0xF1,0xE7),[
 ("PK","Id","รหัส"),("FK","PartId","→ Part"),("","SerialNo","ซีเรียล (→ unit Disposed)"),
 ("","LocationId","คลัง (ปกติ Scrap)"),("","ReasonCode","เหตุผล"),("","Status","Pending/Approved/Disposed")],title="DisposalRequest  (ทำลาย)")

entity(s,"StockCount",10.25,1.0,2.75,AMBER,RGBColor(0xFB,0xF1,0xE7),[
 ("PK","Id","รหัส"),("","CountType","Cycle/Annual"),("","Period","งวด"),("","Status","Draft/.../Completed")],title="StockCount  (นับสต็อก)")
entity(s,"StockCountLine",10.25,3.0,2.75,AMBER,RGBColor(0xFB,0xF1,0xE7),[
 ("PK","Id","รหัส"),("FK","StockCountId","→ StockCount"),("FK","PartId","→ Part"),
 ("","System/PhysicalQty","ยอดระบบ/นับจริง"),("","Variance","ผลต่าง")],title="StockCountLine")

# Part reference note (Part lives on slide 1/2; here all FK to Part shown as tag)
txt(s,"หมายเหตุ: ทุกตารางในหน้านี้เชื่อม Part ด้วย FK PartId (เก็บ PartNo เป็น snapshot) — ดูตาราง Part ในสไลด์ Master Data / Stock",
    0.35,7.05,12.6,0.3,size=8.5,color=TEAL,italic=True)
edge(s,"GoodsReceipt","GoodsReceiptLine","1:N")
edge(s,"Ticket","ReturnRequest","1:N")
edge(s,"StockCount","StockCountLine","1:N")

out="D:/ATMApi/ATM-Inventory-System/ATM_Inventory_ERD_Clean.pptx"
prs.save(out); print("Saved:",out,"slides:",len(prs.slides._sldIdLst))
