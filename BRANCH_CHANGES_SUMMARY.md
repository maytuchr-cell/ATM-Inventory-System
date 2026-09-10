# สรุปภาพรวมการเปลี่ยนแปลงใน Branch `ForDailyReport_Boom`

เอกสารนี้สรุปรายละเอียดการปรับปรุงระบบ พัฒนาฟีเจอร์ใหม่ การแก้ปัญหา และการจัดเตรียมฐานข้อมูลใน Branch **`ForDailyReport_Boom`** ของโครงการ **ATM Inventory System V2**

---

## 📌 สารบัญ (Table of Contents)
1. [เป้าหมายหลักของ Branch](#1-เป้าหมายหลักของ-branch)
2. [ระบบประมวลผล DHL Daily Report (Multi-Sheet Import Engine)](#2-ระบบประมวลผล-dhl-daily-report-multi-sheet-import-engine)
3. [การปรับปรุงระบบ Outbound ที่ไม่มีใบเบิก (Unmatched Outbound)](#3-การปรับปรุงระบบ-outbound-ที่ไม่มีใบเบิก-unmatched-outbound)
4. [ระบบเชื่อมต่อศูนย์ซ่อม SVOA ราษฎร์บูรณะ (WH-RAT)](#4-ระบบเชื่อมต่อศูนย์ซ่อม-svoa-ราษฎร์บูรณะ-wh-rat)
5. [การพัฒนาหน้าจอ Frontend และ UI Components](#5-การพัฒนาหน้าจอ-frontend-และ-ui-components)
6. [การล้างข้อมูล Mock & Seed Data (Clean Production Database)](#6-การล้างข้อมูล-mock--seed-data-clean-production-database)
7. [ชุดการทดสอบระบบอัตโนมัติ (Automated Test Suite)](#7-ชุดการทดสอบระบบอัตโนมัติ-automated-test-suite)
8. [รายการไฟล์ที่มีการเปลี่ยนแปลง (Files Modified / Added)](#8-รายการไฟล์ที่มีการเปลี่ยนแปลง-files-modified--added)

---

## 1. เป้าหมายหลักของ Branch
* พัฒนาระบบนำเข้าและประมวลผลรายงานรายวันจาก DHL (**DHL Daily Report Excel File**) ที่มีขนาดใหญ่ (~12–15 MB) ให้ทำงานได้อย่างถูกต้อง รวดเร็ว และรองรับหลายชีตพร้อมกัน
* ปรับปรุงกระบวนการกระทบยอดสต็อก (Reconciliation) ระหว่างคลังระบบกับยอดจริงของ DHL
* เชื่อมโยงวงจรชีวิตของอะไหล่ (เบิกจ่าย -> ส่งมอบ -> รับคืน -> ส่งซ่อม -> ซ่อมเสร็จ) ผ่านข้อมูล Excel
* ยกเลิกการเปิด Ticket หลอก เพื่อคงความถูกต้องของ Master Data ในระบบ
* เตรียมความพร้อมของฐานข้อมูลจริง (`AtmInventory.db`) โดยลบข้อมูลทดสอบทั้งหมด

---

## 2. ระบบประมวลผล DHL Daily Report (Multi-Sheet Import Engine)

ระบบพัฒนาขึ้นใน `DailyReportController.cs` รองรับการอ่านและประมวลผลข้อมูลครอบคลุมทั้ง 5 ชีตหลักของ DHL:

| ชื่อชีตใน Excel | วัตถุประสงค์และการทำงานในระบบ | ผลกระทบต่อสต็อกและสถานะ |
|---|---|---|
| **Minimum Stock** | ใช้เป็นยอดตั้งต้น และกระทบยอดสต็อกคงเหลือ (Reconciliation) | ปรับค่า MinStock และเปรียบเทียบ Diff ยอดคลังระบบ vs DHL |
| **Inbound normal** | รับอะไหล่เข้าคลัง (ทั้งอะไหล่ใหม่, คืนจากช่าง, และซ่อมเสร็จจาก WH-RAT) | เพิ่มสต็อก `DHL-BKK`, ปิดสถานะใบคืน, ปรับ `PartUnits` เป็น `InStock` / `InRepair` |
| **Outbound Order** | จ่ายอะไหล่ปกติให้ช่างตามรอบงาน | ตัดสต็อก `DHL-BKK`, เพิ่มสต็อกมือช่าง, อัปเดตใบเบิกเป็น "เดินทาง/เบิก", บันทึก `Issued` |
| **24x7 ACTIVITY** | จ่ายอะไหล่ด่วน 24 ชั่วโมงให้ช่าง | ทำงานเช่นเดียวกับ Outbound Order |
| **Outbound Orde Export** | ส่งอะไหล่เสีย/ชำรุดไปศูนย์ซ่อม SVOA ราษฎร์บูรณะ | ย้ายสต็อกเสียจาก `DHL-BKK` ไป `WH-RAT`, เปลี่ยน Location ของ `PartUnits` เป็น `WH-RAT` |

### ฟังก์ชันพิเศษที่เพิ่มเข้ามา:
* **Baseline Cutoff Date (3 ก.ย. 2026)**: ตรวจสอบวันที่จ่ายออก หากเกิดขึ้นก่อนวันตั้งต้น snapshot จะไม่ทำการหักสต็อกคลังกลางซ้ำซ้อน แต่ยังคงบันทึก Serial Number ให้ถูกต้อง
* **ตรวจจับรายการซ้ำซ้อน (`AlreadyImported`)**: หากมีการ Re-import ไฟล์เดิม ระบบจะข้ามแถวที่เคยตัดสต็อกไปแล้วอัตโนมัติ
* **ระบบย้อนกลับรายแถว (`UndoRow`)**: ผู้ดูแลระบบสามารถกดย้อนกลับผลของการ Import แต่ละแถวได้ เพื่อความปลอดภัยสูงสุด

---

## 3. การปรับปรุงระบบ Outbound ที่ไม่มีใบเบิก (Unmatched Outbound)

### ปัญหาเดิม:
ก่อนหน้านี้ เมื่อพบรายการจ่ายออกจาก DHL แต่ไม่พบเลขที่เคสหรือใบเบิกเดิมในระบบ ระบบจะพยายามสร้าง Ticket และ WithdrawBatch จำลองขึ้นมาอัตโนมัติ ซึ่งก่อให้เกิดปัญหา:
* ข้อมูลใน Ticket ไม่สมบูรณ์ (ไม่มีรหัสตู้ ATM, ไม่มีระดับความสำคัญ/SLA, ไม่มีคำอธิบายงาน)
* ทำให้เกิด Ticket ขยะในระบบจำนวนมาก

### แนวทางแก้ไขใหม่:
1. **ปิดการ Auto-create Ticket โดยสมบูรณ์**: รายการที่ไม่มีใบเบิกเดิมจะถูกจัดเป็นประเภท `OutboundUnmatched` (จ่ายออกนอกระบบ / ไม่ผูกใบเบิก)
2. **ปรับปรุงสต็อกและบันทึก Serial Number โดยตรง**:
   * ตัดสต็อกคลังกลาง `DHL-BKK`
   * เพิ่มสต็อกเสมือนในคลังช่าง
   * บันทึกหรือสร้าง `PartUnit` ให้มีสถานะเป็น `Issued` (ส่งมอบให้ช่างแล้ว) เพื่อให้สามารถติดตาม Serial Number ต่อไปได้
3. **ไม่มีการสร้าง Ticket หรือ WithdrawBatch หลอกในฐานข้อมูล**

---

## 4. ระบบเชื่อมต่อศูนย์ซ่อม SVOA ราษฎร์บูรณะ (WH-RAT)

เพิ่ม Workflow การส่งซ่อมและรับคืนจากการซ่อมอย่างสมบูรณ์:
1. **การส่งซ่อม (ชีต Outbound Orde Export)**:
   * ตัดยอด `BadQty` ออกจากคลังกลาง `DHL-BKK`
   * เพิ่มยอด `BadQty` เข้าคลังศูนย์ซ่อม `WH-RAT` (LocationId: 10)
   * อัปเดตตำแหน่งของ Serial Number ใน `PartUnits` ให้ไปอยู่ที่ `WH-RAT`
2. **การรับคืนหลังซ่อมเสร็จ (ชีต Inbound normal)**:
   * เมื่อศูนย์ซ่อมส่งกลับมา DHL ด้วยสถานะ `GOOD`
   * ระบบตัดยอดซ่อมออกจาก `WH-RAT` และเพิ่มยอดสต็อกดีเข้า `DHL-BKK`
   * ปรับสถานะ Serial Number เป็น `InStock` (สภาพ Good) พร้อมใช้งาน

---

## 5. การพัฒนาหน้าจอ Frontend และ UI Components

1. **`admin-dhl-report.html`**:
   * หน้าหลักสำหรับอัปโหลดและนำเข้า Daily Report
   * มีปุ่ม **Preview** แสดงผลการจำลองการนำเข้า พร้อมตารางสรุปผล Reconciliation (Diff สต็อก)
   * ตารางแสดงรายการแยกแท็บตามประเภทผลลัพธ์ (`OutboundConfirmed`, `OutboundUnmatched`, `ReturnConfirmed`, `ExportRepair`, ฯลฯ)
   * รองรับการดูประวัติย้อนหลัง (**History Tab**) และปุ่ม **Undo** ย้อนกลับแต่ละรายการ
2. **`admin-serials.html`**:
   * หน้าระบบสืบค้นและติดตาม Serial Number รายชิ้น
   * แสดงสถานะปัจจุบัน (`InStock`, `InRepair`, `Issued`), สภาพ (`Good`, `Bad`), และตำแหน่งปัจจุบัน
   * มี Timeline แสดงประวัติการเคลื่อนย้ายตั้งแต่รับเข้า, จ่ายออก, ส่งซ่อม, จนถึงคืนคลัง
3. **`admin-locations.js` & `admin-tickets.html`**:
   * เพิ่ม Badge และตัวเลือกแสดงผลของคลังศูนย์ซ่อมราษฎร์บูรณะ (`WH-RAT`)
   * ปรับปรุงหน้าจอรายการใบเบิกให้แสดงข้อมูลชัดเจน ไม่สับสน

---

## 6. การล้างข้อมูล Mock & Seed Data (Clean Production Database)

เพื่อความพร้อมในการใช้งานจริงในสภาพแวดล้อม Production:
* **ลบโค้ด Seed อัตโนมัติ**: ลบฟังก์ชัน `seedDemoData` ออกจาก `Program.cs` ถาวร ป้องกันไม่ให้ระบบสร้างข้อมูลจำลองขึ้นมาใหม่ตอน Start App
* **ล้างข้อมูลในฐานข้อมูลจริง (`AtmInventory.db`)**:
  * ลบ Ticket และ Batch ทดสอบทั้งหมด (`ASV-SEED-001` ถึง `006`)
  * ลบ Goods Receipt ทดสอบ (`GR-2025-001`)
  * ลบอะไหล่ทดสอบที่ติดป้าย `(demo)` ออกจากระบบ
* **สถานะฐานข้อมูลปัจจุบัน**:
  * **Tickets (ใบเบิก)**: `0` ใบ (สะอาด 100% พร้อมให้เริ่มสร้างใบเบิกจริง)
  * **WithdrawBatches**: `0` รายการ
  * **PartUnits (S/N จริงจาก Excel)**: `2,205` ชิ้น
  * **สต็อกรวม**: `23,962` ชิ้นดี / `839` ชิ้นรอซ่อม / `0` ชิ้นเสีย

---

## 7. ชุดการทดสอบระบบอัตโนมัติ (Automated Test Suite)

ระบบมีชุดทดสอบครอบคลุมทั้ง Unit Tests และ Integration Tests รวม **78 ข้อ**:
* `DailyReportControllerTests.cs`: ทดสอบ Logic การจับคู่, การตัดสต็อก, การคืนของ, การปิด auto-ticket, การป้องกันการนำเข้าซ้ำ, การ Undo, และการ Sync & Adjust ผ่าน `AdjustReconcile`
* `DailyReportRealFileTests.cs`: ทดสอบการ Parse ข้อมูลจากไฟล์ Excel จริงของ DHL และการคำนวณกระทบยอดแบบแยก Good/Repair
* **ผลการทดสอบล่าสุด**:
  ```text
  Passed! - Failed: 0, Passed: 77, Skipped: 1, Total: 78, Duration: 1 m 20 s
  ```

---

## 8. ระบบกระทบยอดและปรับปรุงสต็อก (Reconciliation & Audit-Logged Sync)

### การแก้ปัญหายอดคงเหลือไม่สอดคล้องกับ DHL:
1. **แยกประเภทสต็อก Good vs Bad (Repair) อย่างชัดเจน**:
   - ในชีต Minimum Stock ของ DHL มีอะไหล่ 64 รายการที่ปรากฏ 2 บรรทัด (แยกของดี `GOOD` และของเสีย `BAD`)
   - เดิมระบบรวมยอดเข้าด้วยกัน ทำให้เกิดผลต่างเท็จ (False Diff) เช่น UPS Santak 46 ตัวในสถานะเสีย ถูกนำไปเทียบกับของดี
   - ระบบใหม่ทำการ Group By `PartNo` และเปรียบเทียบแยกเป็น:
     - `DhlGoodQty` เทียบกับ `SystemGoodQty` (ผลต่าง: `DiffGood`)
     - `DhlBadQty` เทียบกับ `SystemRepairQty` (ผลต่าง: `DiffRepair`)
   - ทำให้ยอดกระทบยอดแม่นยำขึ้นทันที โดย 741/796 รายการ (93.1%) ของดีตรงกันสมบูรณ์
2. **ระบบแจ้งเตือนเมื่อข้ามไฟล์ (Date Gap Warning)**:
   - ตรวจสอบวันที่มีการ Import ล่าสุด หากผู้ใช้อัปโหลดไฟล์ข้ามช่วงวัน (เช่น จาก 03 ก.ย. ข้ามไป 07 ก.ย. โดยยังไม่ได้นำเข้า 04-05 ก.ย.) ระบบจะแสดงแถบแจ้งเตือนสีส้มทันที เพื่อป้องกันยอดสต็อกคลาดเคลื่อนจากการขาดข้อมูลการตัดจ่าย
3. **ฟังก์ชันปรับยอดสต็อกพร้อมบันทึก Audit Trail (Sync & Adjust)**:
   - ผู้ดูแลระบบสามารถกดปุ่ม **"ปรับยอด"** ในตาราง Reconcile ได้โดยตรง
   - มี Modal ให้ตรวจสอบยอดเดิม -> ยอดใหม่ พร้อมบังคับเลือกเหตุผลในการปรับปรุง (เช่น Physical Cycle Count, Warehouse Shrinkage)
   - บันทึกการเปลี่ยนแปลงลง `StockMovements` ด้วยประเภท `StockCountAdjust` และบันทึก `AuditLogs` อย่างโปร่งใส ตรวจสอบย้อนหลังได้ทุกครั้ง

---

## 9. รายการไฟล์ที่มีการเปลี่ยนแปลง (Files Modified / Added)

### Backend
* `Backend/Api/Controllers/DailyReportController.cs`: Engine หลักสำหรับ Import Excel, Matching, Stock Adjustments, แยก Good/Bad Reconcile, Date Gap Warning, และ Sync & Adjust API
* `Backend/Api/Program.cs`: เพิ่ม Location WH-RAT, ล้าง Seed Demo Data, ปรับ DB Migrations
* `Backend/Api/Models/DailyReportImport.cs`: โมเดลเก็บประวัติ Batch และแถวการ Import
* `Backend/Api.Tests/DailyReportControllerTests.cs`: ชุด Unit Tests รวมการทดสอบ `AdjustReconcile`
* `Backend/Api.Tests/DailyReportRealFileTests.cs`: ชุด Integration Tests ทดสอบกับไฟล์ Excel จริงด้วย Safe Shared Streams

### Frontend
* `Frontend/admin-dhl-report.html`: หน้าจอจัดการ DHL Daily Report, ตาราง Reconcile แบบแยก Good/Repair, แถบแจ้งเตือน Date Gap, และ Modal Sync & Adjust
* `Frontend/shared/api.js`: เพิ่ม `api.dailyReport.adjustReconcile`
* `Frontend/admin-serials.html`: หน้าจอสืบค้นและติดตาม Serial Number
* `Frontend/admin-locations.js`: ตัวจัดการป้ายชื่อและคลังสินค้า
* `Frontend/admin-tickets.html`: ปรับปรุงมุมมองรายการใบเบิก
* `Frontend/version.json`: อัปเดตเวอร์ชันระบบ

### Documentation
* `BRANCH_CHANGES_SUMMARY.md`: เอกสารสรุปการเปลี่ยนแปลงของ Branch นี้
* `DAILY_REPORT_DATA_MANAGEMENT.md`: คู่มือการจัดการและนำเข้าข้อมูล Daily Report
* `DAILY_REPORT_INTEGRATION_PLAN.md`: แผนการทำงานและสถาปัตยกรรมของ Daily Report Engine

