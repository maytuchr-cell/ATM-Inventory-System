/* ── Blank Excel templates for every import on the site ──
   Each header list below mirrors what that page's parser actually matches, so a template filled
   in and uploaded as-is is guaranteed to be read. If a parser's header matching changes, change
   the matching entry here too:
     parts       → CatalogImportService.FindPartsSheet (Part Number + Part Description required)
     equivalent  → CatalogImportService.LinkEquivalents (Part Number + Same part no.)
     feContacts  → FeContactController.Import (prefers the "Contact list DataOne FE" sheet)
     goodsReceipt→ admin-goods-receipt.js buildLines — reads the FIRST sheet, by column POSITION
     dhlDaily    → DailyReportController.ParseFile — picks sheets by NAME, columns by header
   The data sheet(s) always come first and the instructions sheet last: goods receipt reads only
   the first sheet, and the parts/equivalent/FE parsers take the first sheet whose header row
   matches. Instruction rows are written as full sentences so no cell ever equals a header name
   exactly and gets mistaken for a data sheet. */

const IMPORT_TEMPLATES = {
    parts: {
        fileName: 'Template_Parts_Master.xlsx',
        sheets: [{
            name: 'Parts',
            headers: ['Part Number', 'Part Description', 'Main Unit', 'Sub Unit', 'Picture', 'Remark', 'Same part no.'],
            widths: [18, 40, 18, 14, 14, 22, 24],
        }],
        notes: [
            'ต้องกรอก: Part Number และ Part Description ทุกแถว',
            'Sub Unit: ใส่ "Upper Unit" หรือ "Lower Unit" (ค่าอื่นจะถูกเว้นว่าง)',
            'Remark: ใส่ประเภทเครื่อง เช่น ADM, ATM, CDM (หลายค่าคั่นด้วย , )',
            'Same part no.: รหัสอะไหล่ที่ใช้แทนกันได้ หลายรหัสคั่นด้วย , หรือ ; หรือขึ้นบรรทัดใหม่',
            'Part Number ที่มีอยู่แล้วในระบบจะถูกอัปเดต ที่ยังไม่มีจะถูกเพิ่มใหม่',
        ],
    },
    equivalent: {
        fileName: 'Template_Equivalent_Parts.xlsx',
        sheets: [{
            name: 'Equivalent',
            headers: ['Part Number', 'Same part no.'],
            widths: [20, 40],
        }],
        notes: [
            'Part Number: รหัสอะไหล่หลัก (ต้องมีอยู่ในระบบแล้ว)',
            'Same part no.: รหัสที่ใช้แทนกันได้ หลายรหัสคั่นด้วย , หรือ ; หรือขึ้นบรรทัดใหม่',
            'รหัสที่ไม่มีในระบบจะถูกข้ามและแจ้งในผลลัพธ์',
        ],
    },
    feContacts: {
        fileName: 'Template_FE_Contact_List.xlsx',
        sheets: [{
            name: 'Contact list DataOne FE',
            headers: ['FE ID', 'FEName', 'Tel.', 'Address', 'Postcode'],
            widths: [14, 26, 16, 50, 10],
        }],
        notes: [
            'ต้องกรอก: FE ID, FEName และ Address',
            'อย่าเปลี่ยนชื่อชีตข้อมูล (Contact list DataOne FE)',
            'FE ID ที่มีอยู่แล้วจะถูกอัปเดตทับ ที่ไม่อยู่ในไฟล์จะไม่ถูกลบ',
        ],
    },
    goodsReceipt: {
        fileName: 'Template_Goods_Receipt.xlsx',
        sheets: [{
            name: 'Goods Receipt',
            headers: ['No.', 'Part Number', 'D1 Part Description', 'Serial Number', 'Quantity', 'Status'],
            widths: [6, 20, 40, 22, 10, 12],
        }],
        notes: [
            'ห้ามสลับลำดับคอลัมน์ — ระบบอ่านตามตำแหน่งคอลัมน์ ไม่ใช่ชื่อหัว',
            'ต้องกรอก: Part Number และ Quantity (ตัวเลขมากกว่า 0)',
            'Status: ใส่ Good หรือ Bad (Defective / NG = Bad, ค่าอื่นหรือเว้นว่าง = Good)',
            'ข้อมูลต้องอยู่ในชีตแรกของไฟล์',
            'ถ้าใช้ไฟล์ .csv ให้เรียงคอลัมน์เป็น: PartNo, SerialNo, Qty, Condition',
        ],
    },
    dhlDaily: {
        fileName: 'Template_DHL_Daily_Report.xlsx',
        sheets: [
            { name: 'Outbound Order',
              headers: ['Order Date', 'Case No', 'FE Name', 'Part Number', 'Part Description', 'Serial Number', 'QTY', 'Inventory Status', 'FE Receive Date'],
              widths: [14, 22, 22, 18, 36, 20, 6, 16, 16] },
            { name: '24x7 ACTIVITY',
              headers: ['Order Date', 'Case No', 'FE Name', 'Part Number', 'Part Description', 'Serial Number', 'QTY', 'Inventory Status', 'FE Receive Date'],
              widths: [14, 22, 22, 18, 36, 20, 6, 16, 16] },
            { name: 'Return inbound',
              headers: ['Booking Date', 'System Received Date', 'WH Received Date', 'Case No', 'FE Name', 'Part Number', 'Part Description', 'Serial Number', 'QTY', 'INVENTORY STATUS', 'Problem'],
              widths: [14, 20, 18, 22, 22, 18, 36, 20, 6, 18, 24] },
            { name: 'Outbound Order Export',
              headers: ['Order Date', 'Case No', 'FE Name', 'Customer site', 'Address', 'Part Number', 'Part Description', 'Serial Number', 'QTY', 'Inventory Status'],
              widths: [14, 22, 22, 22, 40, 18, 36, 20, 6, 16] },
            { name: 'Inbound normal',
              headers: ['Shipped from', 'System Received Date', 'Part Number', 'Part Description', 'Serial Number', 'QTY', 'INVENTORY STATUS'],
              widths: [18, 20, 18, 36, 20, 6, 18] },
            { name: 'Minimum Stock',
              headers: ['Part Number', 'Part Description', 'AVAILABLE_QTY', 'MIN QTY', 'STOCK', 'INVENTORY_STS'],
              widths: [18, 36, 14, 10, 10, 16] },
        ],
        notes: [
            'ไฟล์นี้ปกติ DHL เป็นผู้ส่งมา — template นี้ใช้กรอกเองเมื่อจำเป็นเท่านั้น',
            'อย่าเปลี่ยนชื่อชีต ระบบเลือกอ่านแต่ละชีตตามชื่อ ใช้เฉพาะชีตที่มีข้อมูลได้ ชีตที่ว่างจะถูกข้าม',
            'วันที่ให้ใช้รูปแบบวันที่ของ Excel หรือพิมพ์เป็น ค.ศ. (เช่น 18/09/2026)',
            'Inventory Status: GOOD หรือ BAD',
        ],
    },
};

let _xlsxLoading = null;
function ensureXlsx() {
    if (window.XLSX) return Promise.resolve();
    if (_xlsxLoading) return _xlsxLoading;
    _xlsxLoading = new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = 'https://cdn.sheetjs.com/xlsx-0.20.3/package/dist/xlsx.full.min.js';
        s.onload = () => resolve();
        s.onerror = () => { _xlsxLoading = null; reject(new Error('โหลดตัวสร้างไฟล์ Excel ไม่สำเร็จ')); };
        document.head.appendChild(s);
    });
    return _xlsxLoading;
}

async function downloadImportTemplate(key) {
    const tpl = IMPORT_TEMPLATES[key];
    if (!tpl) return;
    try {
        await ensureXlsx();
        const wb = XLSX.utils.book_new();
        for (const sh of tpl.sheets) {
            const ws = XLSX.utils.aoa_to_sheet([sh.headers]);
            ws['!cols'] = sh.widths.map(w => ({ wch: w }));
            XLSX.utils.book_append_sheet(wb, ws, sh.name);
        }
        if (tpl.notes?.length) {
            const ws = XLSX.utils.aoa_to_sheet([['วิธีกรอก'], ...tpl.notes.map(n => ['• ' + n])]);
            ws['!cols'] = [{ wch: 90 }];
            XLSX.utils.book_append_sheet(wb, ws, 'คำแนะนำ');
        }
        XLSX.writeFile(wb, tpl.fileName);
    } catch (e) {
        (window.showToast || alert)(e.message || 'สร้าง template ไม่สำเร็จ', 'error');
    }
}
