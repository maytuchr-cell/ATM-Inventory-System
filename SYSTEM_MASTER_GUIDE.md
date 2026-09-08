# 📘 คู่มือสถาปัตยกรรมและการทำงานฉบับสมบูรณ์ (ATM Inventory System V2)
**ATM Spare Parts Management System** — DataOne Asia (Thailand)  
*เอกสารสรุปภาพรวมโค้ดเบส โครงสร้างระบบ วงจรการทำงาน และฐานข้อมูล เพื่อเป็นคู่มืออ้างอิงกลาง (Single Source of Truth)*

---

## 📌 สารบัญ (Table of Contents)
1. [ภาพรวมระบบและเทคโนโลยี (System Overview & Tech Stack)](#1-ภาพรวมระบบและเทคโนโลยี)
2. [สรุปความเปลี่ยนแปลงสถาปัตยกรรมสำคัญ (Latest Architecture Refinements)](#2-สรุปความเปลี่ยนแปลงสถาปัตยกรรมสำคัญ)
3. [โครงสร้างโฟลเดอร์และไฟล์ในโปรเจกต์ (Project Directory Structure)](#3-โครงสร้างโฟลเดอร์และไฟล์ในโปรเจกต์)
4. [บทบาทและระบบความปลอดภัย (Roles & Authentication)](#4-บทบาทและระบบความปลอดภัย)
5. [วงจรชีวิตใบเบิก-คืนอะไหล่ (Withdraw & Return Workflow)](#5-วงจรชีวิตใบเบิก-คืนอะไหล่)
6. [ระบบรายงานประจำวัน DHL (DHL Daily Report Reconciliation)](#6-ระบบรายงานประจำวัน-dhl)
7. [โครงสร้างฐานข้อมูลและคลังสินค้า (Database & Inventory Model)](#7-โครงสร้างฐานข้อมูลและคลังสินค้า)
8. [คู่มือการติดตั้งและ Deploy บน IIS (Setup, Publish & Operations)](#8-คู่มือการติดตั้งและ-deploy-บน-iis)
9. [การแก้ปัญหาพบบ่อยและการดู Log (Troubleshooting Guide)](#9-การแก้ปัญหาพบบ่อยและการดู-log)

---

## 1. ภาพรวมระบบและเทคโนโลยี

ระบบ **ATM Inventory System V2** เป็นเว็บแอปพลิเคชันสำหรับบริหารจัดการคลังอะไหล่ตู้ ATM, ADM, CDM สำหรับช่างเทคนิคภาคสนาม (Field Engineers) และฝ่ายบริหารคลังสินค้า โดยเชื่อมต่อกับระบบ Aservice, API_KMM, และระบบรายงานคลังของ DHL

### 🛠️ Tech Stack
* **Backend API:** ASP.NET Core 8.0 Web API (`net8.0`) เขียนด้วยภาษา C#
* **Frontend:** Responsive Vanilla HTML5 / CSS3 / JavaScript (ES6+), ไม่ใช้ Framework ใหญ่ รองรับการใช้งานทั้ง Desktop และมือถือ
* **Database (Dual Provider Support):**
  * **Local Development:** SQLite (`AtmInventory.db`)
  * **Production Server:** MySQL 8.0+ (`Sparepart_DB` บนเซิร์ฟเวอร์ `172.22.100.22`)
  * *สลับการทำงานผ่านการตั้งค่า `"DatabaseProvider": "Sqlite" | "MySql"`*
* **Web Server:** Microsoft IIS 10+ (รันผ่าน ASP.NET Core Module V2 - InProcess)
* **Library สำคัญ:**
  * `Microsoft.EntityFrameworkCore` 8.0
  * `ClosedXML` 0.105.1 (สำหรับอ่าน/เขียนไฟล์ Excel .xlsx ของ DHL)
  * `Microsoft.AspNetCore.Authentication.JwtBearer` (สำหรับ JWT Auth)

---

## 2. สรุปความเปลี่ยนแปลงสถาปัตยกรรมสำคัญ

เพื่อให้สอดคล้องกับหน้างานจริง โค้ดชุดปัจจุบันได้รับการ Refactor สำคัญดังนี้:

1. **ย้ายคลังสินค้าหลัก (Central Warehouse):**
   * เปลี่ยนคลังหลักจาก `WH-RAT` (ราษฎร์บูรณะ) ไปเป็น **`DHL-BKK` (DHL Center Bangkok)** โดยสต็อกส่วนใหญ่และอะไหล่ที่พร้อมส่งจะถูกตัด/เติมที่ `DHL-BKK` เป็นหลัก
2. **ระบบคืนอะไหล่ตามใบเบิก (Batch-Scoped Returns — "คืนตามใบเบิก"):**
   * 1 เคสงาน (`ExternalTicketNo`) สามารถสร้างใบเบิกได้หลายใบ (`WithdrawBatches`) เช่น เบิกเพิ่มรอบ 2
   * การส่งคืนอะไหล่ถูกเปลี่ยนเป็น **"คืนตามใบเบิกนั้นๆ โดยตรง"** ไม่นำอะไหล่มารวมปนกัน และไม่อนุญาตให้ใส่อะไหล่นอกใบเบิก
   * ยกเลิก `ReturnController` เดิม และรวมการทำงานทั้งหมดเข้าสู่ [`TicketController.cs`](Backend/Api/Controllers/TicketController.cs)
3. **กฎการคืนอะไหล่ (Strict Return Integrity):**
   * บังคับผลรวมจำนวนอะไหล่ที่คืน: **`ดี` + `เสีย` + `สูญหาย (Lost)` = `จำนวนที่เบิกไป 100%`**
4. **การป้องกัน Demo Data บน Production:**
   * ใน [`Program.cs`](Backend/Api/Program.cs) เพิ่มการตรวจสอบ `app.Environment.IsDevelopment()` ก่อนสร้าง Ticket จำลอง เพื่อป้องกันไม่ให้ข้อมูล Seed ทดสอบหลุดเข้าไปในฐานข้อมูล Production

---

## 3. โครงสร้างโฟลเดอร์และไฟล์ในโปรเจกต์

```text
ATM-Inventory-System_V2/
├── Backend/
│   ├── Api/                               # ASP.NET Core 8 Web API
│   │   ├── Controllers/                   # 25 Web API Controllers
│   │   │   ├── AuthController.cs          # ล็อกอิน, JWT, ตรวจสอบสิทธิ์
│   │   │   ├── TicketController.cs        # ควบคุม Ticket, ใบเบิก, ใบคืน, SLA
│   │   │   ├── PartsController.cs         # Master อะไหล่, ผู้ถือครอง (Holders)
│   │   │   ├── DailyReportController.cs   # นำเข้าและกระทบยอดรายงานประจำวัน DHL
│   │   │   ├── GoodsReceiptController.cs  # รับอะไหล่เข้าสต็อก
│   │   │   ├── FeContactController.cs     # จัดการข้อมูลที่อยู่และรหัสช่าง FE
│   │   │   ├── DashboardController.cs     # สถิติและข้อมูลภาพรวม
│   │   │   └── ... (Categories, Locations, Users, Vendors, etc.)
│   │   ├── Models/                        # Entity Data Models (EF Core)
│   │   │   ├── Ticket.cs                  # หัวตั๋วงาน (Case No, ช่าง)
│   │   │   ├── WithdrawBatch.cs           # ใบเบิกแต่ละรอบ และสถานะใบคืน
│   │   │   ├── TicketPartLine.cs          # รายการอะไหล่ในใบเบิก/ใบคืน
│   │   │   ├── Part.cs                    # ข้อมูล Master อะไหล่
│   │   │   ├── PartStock.cs               # ยอดสต็อกแยกตามคลัง
│   │   │   ├── PartUnit.cs                # อะไหล่รายชิ้น (Serial Number Tracking)
│   │   │   ├── FeContact.cs               # ข้อมูลช่าง DHL FE
│   │   │   └── ...
│   │   ├── Services/                      # Business Logic Services
│   │   │   ├── StockService.cs            # ปรับปรุงสต็อกและบันทึก Ledger
│   │   │   ├── AuditService.cs            # บันทึกประวัติการกระทำ (Audit Log)
│   │   │   └── KmmAuthService.cs          # Proxy ตรวจสอบสิทธิ์ช่างกับ API_KMM
│   │   ├── AppDbContext.cs                # EF Core Database Context (31 Tables)
│   │   ├── Program.cs                     # Startup, DI, Pipeline, Migrations
│   │   ├── appsettings.json               # คอนฟิกค่าพื้นฐาน (Provider, KMM, JWT)
│   │   └── web.config                     # IIS ANCM Configuration
│   └── Api.Tests/                         # ชุดทดสอบ Unit Tests (74 Tests ผ่าน 100%)
├── Frontend/                              # เว็บไซต์หน้าบ้าน (Static Web)
│   ├── login.html                         # หน้าล็อกอิน Admin / Staff / Auditor
│   ├── login-tech.html                    # หน้าล็อกอินเฉพาะช่างเทคนิค (Mobile UI)
│   ├── admin.html                         # Admin Dashboard ภาพรวมระบบ
│   ├── admin-tickets.html                 # จัดการ Ticket ใบเบิก/ใบคืน (พร้อมตัวลูกกลิ้ง Scrollable Table & แบ่งหน้า)
│   ├── admin-parts.html / .js             # ทะเบียนอะไหล่, ยอดสต็อก, ดู Holders
│   ├── admin-serials.html                 # ทะเบียน Serial Number Registry (ดูผู้ถือครองช่าง/เลขเคส, Quick Timeline Modal)
│   ├── admin-dhl-report.html              # ระบบกระทบยอดรายงานประจำวัน DHL (Preview & Commit, Auto-Ticket)
│   ├── admin-shortage-report.html         # รายงานอะไหล่ขาดมือ/รอจัดหา
│   ├── admin-fe-contacts.html             # นำเข้า/จัดการสมุดที่อยู่ช่าง FE
│   ├── admin-goods-receipt.html / .js     # รับของเข้าสต็อก (PO / Manual)
│   ├── admin-equivalent-groups.html / .js # กลุ่มอะไหล่เทียบเคียง (Substitutes)
│   ├── admin-history.html                 # ประวัติใบเบิก-คืนทั้งหมด
│   ├── admin-tracking.html / .js          # ค้นหาประวัติ Timeline ของ Serial Number (พร้อมปุ่มย้อนกลับ)
│   ├── admin-audit-log.html               # ตรวจสอบประวัติการทำงานในระบบ
│   ├── tech.html                          # หน้าจอทำงานหลักของช่าง (เบิก/รับ/คืน)
│   ├── tech-addresses.html                # สมุดที่อยู่ส่วนตัวของช่าง
│   └── shared/                            # ไฟล์ที่ใช้ร่วมกัน
│       ├── api.js                         # Central API Fetch Client & Base URL
│       ├── layout.js                      # แถบเมนู Sidebar / Topbar / Theme
│       ├── styles.css                     # ดีไซน์กลางและชุดสี
│       └── translations.js                # ระบบสลับภาษา (TH / EN)
├── Launcher/                              # โปรแกรมเปิด Dev Environment (`start.exe`)
├── Publish-IIS.ps1                        # สคริปต์ Build Package พร้อม Deploy
└── Setup-IIS.ps1                          # สคริปต์ตั้งค่า IIS Application บนเซิร์ฟเวอร์
```

---

## 4. บทบาทและระบบความปลอดภัย

ระบบใช้การพิสูจน์ตัวตนด้วย **JWT Bearer Token**:

| บทบาท (Role) | สิทธิ์การเข้าถึง | หน้าจอที่ใช้งาน |
| :--- | :--- | :--- |
| **SystemAdmin** | สิทธิ์สูงสุดในระบบ บริหารจัดการผู้ใช้ อนุมัติเบิก/คืน ดูแล Master Data ทั้งหมด | ทุกหน้าจอ `admin-*.html` |
| **Staff** | จัดการสต็อกสินค้า อนุมัติเบิก รับของเข้าสต็อก โอนย้ายสต็อก สลับอะไหล่เทียบเคียง | หน้าคลัง `admin-parts`, `admin-goods-receipt`, `admin-tickets` ฯลฯ |
| **Auditor** | สิทธิ์อ่านอย่างเดียว (Read-Only) ห้ามแก้ไขข้อมูล ดู Audit Log, ประวัติ, รายงาน | Dashboard, History, Tracking, Reports |
| **Tech** | ดูงานที่มอบหมาย ทำเรื่องขอเบิก กดยืนยันรับของ และทำเรื่องส่งคืนอะไหล่ | `tech.html`, `tech-addresses.html` |

### 🔐 การล็อกอินของช่าง (Hybrid Authentication):
1. ช่างกรอก **รหัสพนักงาน/อีเมล** และ **รหัสผ่าน** ที่ `login-tech.html`
2. ระบบจะตรวจเช็คในตาราง `Users` ของฐานข้อมูลระบบก่อน
3. หากไม่พบ ระบบจะเรียกไปยัง **API_KMM** (`https://172.22.100.12:9445`) เพื่อตรวจสอบกับระบบ Aservice กลาง
4. หากผ่าน ระบบจะดึงชื่อ-สกุล และแผนก มาสร้างบัญชีในตาราง `Users` อัตโนมัติ (Auto-Provisioning) และออก JWT Token ให้

---

## 5. วงจรชีวิตใบเบิก-คืนอะไหล่

### 5.1 ขาเบิกอะไหล่ (Withdraw Lifecycle)
1. **ขอเบิก (`รอ` / `รออะไหล่`):**
   * ช่างกดขอเบิกอะไหล่ใน `tech.html` ➡️ ระบบสร้าง `WithdrawBatch` พร้อมออกเลขที่ใบเบิก `WD-YYYY-NNNNN`
   * ระบบตรวจสอบสต็อกคลังกลาง `DHL-BKK` ทันที:
     * หากของพอ ➡️ สถานะเป็น **`รอ`** (รอ Admin อนุมัติ)
     * หากของไม่พอ ➡️ สถานะเป็น **`รออะไหล่`** (Admin สามารถสลับอะไหล่เทียบเคียงใน `admin-tickets.html` ได้)
2. **อนุมัติ (`รอส่งเมล DHL`):**
   * Admin ตรวจสอบและกดปุ่ม **"อนุมัติ"** พร้อมเลือกระดับ SLA
   * **สต็อกจะถูกตัดออกจาก `DHL-BKK` ทันที ณ ขั้นตอนนี้**
3. **จัดส่ง (`เดินทาง`):**
   * Admin ส่งอีเมลแจ้งขนส่ง DHL เรียบร้อย แล้วกด **"ส่งเมลสำเร็จ"** ➡️ สถานะเปลี่ยนเป็น **`เดินทาง`**
   * ระบบประทับเวลา `EmailSentAt` (ถ้าช่างไม่กดรับเกิน 24 ชม. จะมี Banner เตือนในหน้าช่าง)
4. **ช่างรับของ (`เบิก`):**
   * เมื่อของถึงมือ ช่างกดปุ่ม **`✅ ยืนยันรับของ`**
   * สถานะเปลี่ยนเป็น **`เบิก`** และสต็อกจะถูกบันทึกเข้าบัญชีถือครองของช่าง (**`OL_TECHNICIAN`**)
   * ประทับเวลา `batch.UpdatedAt = DateTime.Now` (เริ่มนับเวลาถือครองอะไหล่)

---

### 5.2 ขาคืนอะไหล่ (Return Lifecycle — "คืนตามใบเบิก")
1. **ช่างแจ้งส่งคืน (`รอ`):**
   * ช่างกดปุ่ม **`📥 คืนตามใบเบิก`** ที่ใบเบิกใบเดิม
   * ต้องแจกแจงสภาพอะไหล่: **`ดี` + `เสีย` + `สูญหาย` = จำนวนที่เบิกไปทั้งหมด**
   * เมื่อส่งเรื่อง สถานะการคืนของใบเบิกนั้น (`ReturnStatus`) จะกลายเป็น **`รอ`**
2. **Admin ตรวจรับคำขอ (`อนุมัติคืน`):**
   * Admin กดปุ่ม **"ยืนยัน"** คำขอคืน
3. **Admin ประสานงาน DHL (`กำลังเดินทางรับคืน`):**
   * Admin ส่งอีเมลให้ DHL ไปรับของที่ช่าง แล้วกด **"ส่งเมลสำเร็จ"**
4. **ช่างส่งมอบของให้ DHL (`เดินทาง`):**
   * ช่างส่งของให้ DHL เรียบร้อย แล้วกด **"🚚 จัดส่งแล้ว"**
5. **ของถึงคลัง DHL และตรวจรับ (`คืน`):**
   * เมื่อของถึงคลัง Admin ตรวจสอบและกด **"คืนสำเร็จ"**
   * **สต็อกจะถูกตัดออกจากช่าง (`OL_TECHNICIAN`) และโอนกลับเข้าคลัง `DHL-BKK`:**
     * อะไหล่สภาพ "ดี" ➡️ เข้าช่องสต็อกดี (`GoodQty`)
     * อะไหล่สภาพ "เสีย" ➡️ เข้าช่องสต็อกรอซ่อม (`RepairQty`)
     * อะไหล่สภาพ "สูญหาย" ➡️ บันทึกตัดยอดและบันทึกประวัติ

---

## 6. ระบบรายงานประจำวัน DHL

ใช้สำหรับนำเข้าไฟล์ Excel รายวันของ DHL เพื่อกระทบยอดสต็อกและอัปเดตสถานะอัตโนมัติ

### 📑 5 ชีตที่ระบบอ่านข้อมูล:
1. **`Return inbound`** : รายการที่ช่างส่งคืนเข้าคลัง DHL
2. **`Outbound Order `** : รายการที่ DHL จ่ายของออกไปให้ช่าง
3. **`24x7 ACTIVITY`** : รายการเบิกจ่ายด่วน 24 ชั่วโมง
4. **`Inbound normal`** : รายการอะไหล่ที่ซ่อมเสร็จจากศูนย์ซ่อม (SVOA / D1 Room Repair) ส่งกลับเข้าคลัง
5. **`Minimum Stock`** : รายการตรวจนับสต็อกจริงในคลัง DHL เพื่อเปรียบเทียบยอด (Reconciliation)

### ⏱️ กฎวันตั้งต้น Baseline Date (`2026-09-03 23:59:59`):
* **รายการที่เกิดก่อน/ในวันที่ 03 ก.ย. 2026 (`PriorToBaseline`):**
  * ระบบจะไม่บวก/ลบตัวเลขสต็อกซ้ำ (เพื่อป้องกันยอดสต็อกเบิ้ล เพราะยอดก่อนหน้านั้นถูกนับเป็นสต็อกตั้งต้นแล้ว)
  * แต่ระบบจะ **บันทึก Serial Number ทั้งหมดเข้าตาราง `PartUnits`** เพื่อใช้สืบค้นประวัติ
* **รายการที่เกิดขึ้นหลังวันที่ 03 ก.ย. 2026:**
  * ปรับปรุงยอดสต็อกจริงเข้า/ออกตามสภาพทันที
* **ระบบ Undo:** หาก Import ผิดพลาด สามารถกดยกเลิกผลกระทบเฉพาะแถวได้ในหน้าประวัติ

### 🔄 การสร้างตั๋วงานและใบเบิกอัตโนมัติ (Auto-Ticket & Auto-Withdrawal):
เมื่อไฟล์ Daily Report มีรายการเบิกจ่าย (`Outbound Order ` หรือ `24x7 ACTIVITY`) แต่ยังไม่มี Ticket ในระบบ:
* ระบบจะทำการสร้าง `Ticket` และ `WithdrawBatch` ให้โดยอัตโนมัติ (สถานะ `เบิก` / Issued)
* จับคู่รหัสช่าง `FE ID` ในรายงานกับตาราง **`FeContacts`** เพื่อระบุชื่อช่างจริง, แผนก, และเบอร์ติดต่อ
* ออกเลขที่ใบเบิก `WD-AUTO-YYYY-NNNNN` เพื่อให้ทุกอะไหล่ที่ออกจากคลัง DHL มีเอกสารอ้างอิงและสอบย้อนกลับได้ 100%

### 🏷️ วงจรชีวิต Serial Number และทะเบียนกลาง (`admin-serials.html`):
* **Physical Truth จาก Daily Report:** S/N จะถูกผูกกับช่างเมื่อ DHL สแกนจ่ายจริงใน `Outbound Order` (สถานะ `Issued`, ถือครองโดยช่าง, ผูก Case No) และปลดภาระเมื่อ DHL สแกนรับใน `Return inbound` (สถานะ `InStock` หรือ `InRepair`)
* **ทะเบียน Serial Registry (`admin-serials.html`):**
  * สรุปยอด KPI แบบเรียลไทม์ (ทั้งหมด, ในคลัง, ช่างถือ, ส่งซ่อม, จำหน่าย) พร้อมกดคลิกเพื่อกรองข้อมูลแบบ Toggle
  * ค้นหาได้ทั้ง Serial Number, Part No, ชื่ออะไหล่ และชื่อช่างผู้ถือครอง
  * **Quick Timeline Modal:** กดที่ Serial Number หรือปุ่มประวัติเพื่อดูประวัติการเคลื่อนไหว (StockMovements) และรายละเอียดตั๋วงานได้ทันทีโดยไม่ต้องเปลี่ยนหน้า

### 📑 การจัดการตาราง Ticket ข้อมูลขนาดใหญ่ (`admin-tickets.html`):
* เพื่อรองรับข้อมูลใบเบิก-คืนที่มีมากกว่า 400+ รายการ หน้าจอได้รับการปรับปรุง:
  * **Sticky Header Scroll Container:** มีแถบเลื่อน (Scrollbar) ภายในตาราง พร้อมหัวตารางตรึงอยู่ด้านบนตลอดเวลา ไม่เลื่อนหลุดหน้าจอ
  * **ระบบแบ่งหน้า (Pagination):** เลือกแสดงผลได้ทั้ง 50, 100, 200 แถว หรือ All เพื่อประสิทธิภาพการเรนเดอร์ที่รวดเร็วและลื่นไหล

---

## 7. โครงสร้างฐานข้อมูลและคลังสินค้า

### 📍 7 คลังสินค้าหลักในระบบ (`Locations`):
1. `DHL-BKK` (DHL Center Bangkok) — **คลังสินค้าหลักของระบบ**
2. `WH-RAT` (Ratchaburana Warehouse) — คลังราษฎร์บูรณะ
3. `GRG-BKK` (GRG Bangkok Hub) — คลังอะไหล่แบรนด์ GRG
4. `APT-BKK` (Suvarnabhumi Airport Hub) — คลังนำเข้าสนามบิน
5. `SCRAP-01` (Scrap / Disposal Yard) — คลังซากรอทำลาย
6. `OL-TECH` (On-hand Technician Stock) — สต็อกถือครองของช่างทั่วประเทศ
7. `TRANSIT-01` (In-Transit) — สถานะระหว่างขนส่ง

### 🗄️ ตารางฐานข้อมูลหลัก (Core Tables):
* **`Parts`** : ทะเบียนอะไหล่ (PartNo, PartName, Min/Max/ReorderPoint, DeviceType, CategoryId)
* **`PartStocks`** : ตารางยอดคงเหลือ (PartId, LocationId, GoodQty, RepairQty)
* **`PartUnits`** : รายการอะไหล่รายชิ้นที่มี Serial Number (SerialNo, PartId, LocationId, Status: InStock/Issued/InRepair/Scrapped)
* **`Tickets`** : ตั๋วงานหลัก (ExternalTicketNo, TechEmail, TechName, TechDept, UpdatedAt)
* **`WithdrawBatches`** : ใบเบิกแต่ละรอบ (WithdrawSlipNo, Status, ReturnStatus, UpdatedAt, EmailSentAt)
* **`TicketPartLines`** : รายการอะไหล่ในตั๋ว (TicketId, WithdrawBatchId, PartNo, Quantity, ConfirmedQty, Condition)
* **`StockMovements`** : สมุดบัญชีสต็อก (Ledger) บันทึกทุกความเคลื่อนไหวเข้า-ออก ไม่มีการลบประวัติ
* **`FeContacts`** : สมุดรายชื่อและที่อยู่ช่างของ DHL สำหรับ Auto-fill ในหน้าฟอร์มช่าง

---

## 8. คู่มือการติดตั้งและ Deploy บน IIS

### 8.1 การรันบนเครื่องพัฒนา (Local Development):
1. ดับเบิลคลิกไฟล์ **`Launcher/bin/Debug/net8.0/ATM-Launcher.exe`** (หรือรัน `dotnet run` ที่ `Launcher/`)
2. ระบบจะเปิด:
   * Backend API: `http://localhost:5128`
   * Frontend: `http://localhost:3000/login.html`

### 8.2 การ Build & Publish ขึ้น Production:
รันคำสั่ง PowerShell ที่โฟลเดอร์ Root ของโปรเจกต์:
```powershell
powershell -ExecutionPolicy Bypass -File .\Publish-IIS.ps1
```
* โฟลเดอร์ผลลัพธ์จะอยู่ที่: **`E:\Playground\ATM-Inventory-System_V2\publish\`**
  * `publish\` ➡️ นำไปวางเป็น Root ของ IIS Website (Frontend)
  * `publishpi\` ➡️ นำไปตั้งเป็น IIS Application `/api` (Backend API)

### 8.3 การตั้งค่าบน Windows Server (ครั้งแรก):
1. ต้องติดตั้ง **ASP.NET Core 8.0 Hosting Bundle** บน Windows Server ก่อน
2. นำสคริปต์ [`Setup-IIS.ps1`](Setup-IIS.ps1) ไปรันบนเซิร์ฟเวอร์เพื่อผูก App Pool และคอนฟิกค่า MySQL:
   ```powershell
   .\Setup-IIS.ps1 -MySqlConnection "server=172.22.100.22;port=3306;database=Sparepart_DB;user=workbench_user;password=...;"
   ```
3. สั่งรีสตาร์ท IIS:
   ```powershell
   iisreset
   ```

---

## 9. การแก้ปัญหาพบบ่อยและการดู Log

### ❌ ปัญหา: HTTP 500: เซิร์ฟเวอร์ API ขัดข้อง
1. **เช็ค Hosting Bundle:** บนเซิร์ฟเวอร์เปิด PowerShell รัน `dotnet --list-runtimes` ต้องมี `Microsoft.AspNetCore.App 8.0.x`
2. **เช็คการต่อ Database:** รัน `Test-NetConnection -ComputerName 172.22.100.22 -Port 3306` ว่าต่อ MySQL ได้หรือไม่
3. **ดู Log ละเอียดตรงจุดเกิดเหตุ:**
   * เปิดไฟล์ Log ของระบบที่: `C:\...\publishpi\logs\stdout_*.log`
   * หรือเปิด **Event Viewer** (`eventvwr.msc`) ➡️ **Windows Logs** ➡️ **Application** ดู Event สีแดงจาก `IIS AspNetCore Module V2`

### ❌ ปัญหา: Database Locked (กรณีใช้ SQLite)
* เกิดจากมีโปรแกรม (เช่น DB Browser หรือ API) เปิดค้างอยู่
* ก่อนก็อปปี้ทับไฟล์ `AtmInventory.db` ให้สั่งหยุด App Pool ก่อนเสมอ:
  ```powershell
  Stop-WebAppPool -Name "ATM-Inventory-API"
  # นำไฟล์ไปวางทับ
  Start-WebAppPool -Name "ATM-Inventory-API"
  ```
* หากพบไฟล์ `AtmInventory.db-wal` หรือ `.db-shm` แสดงว่าฐานข้อมูลกำลังทำงานอยู่ เมื่อสั่งหยุด Service ไฟล์ชั่วคราวเหล่านี้จะถูกรวมเข้าไฟล์หลักและหายไปเองอัตโนมัติ

---
*เอกสารนี้จัดทำขึ้นโดย Antigravity AI เพื่อเป็นคู่มือหลักประจำโปรเจกต์ ATM Inventory System V2*
