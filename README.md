ATM Inventory System

ระบบจัดการสต็อกอะไหล่ ATM สำหรับ DataOne Asia (Thailand)
พัฒนาด้วย .NET 8 Web API + Vanilla JS Frontend + SQLite

Tech Stack
Layer	Technology
Backend	ASP.NET Core 8 Web API, EF Core, SQLite
Frontend	HTML/CSS/Vanilla JS (ไม่มี framework)
Launcher	.NET Console App (start.exe)
Auth	JWT (login ผ่าน /api/Auth/login)
โครงสร้างโปรเจกต์

ATM-Inventory-System/
├── Backend/
│ ├── Api/ # ASP.NET Core Web API (port 5128)
│ │ ├── Controllers/ # API endpoints
│ │ ├── Models/ # EF Core models
│ │ ├── Services/ # StockService, AuditService ฯลฯ
│ │ ├── Program.cs # startup + lightweight schema migrations
│ │ └── AtmInventory.db # SQLite (ไม่ commit เข้า git)
│ └── Api.Tests/ # xUnit tests (EF Core InMemory)
├── Frontend/ # Static HTML/JS/CSS (port 3000)
│ ├── shared/ # styles.css, layout.js, api.js, translations.js, toast.js
│ ├── login.html # หน้า login (Admin/Staff/Auditor/Tech)
│ ├── admin.html # Dashboard
│ ├── admin-parts.html # Parts Master (คลังกลาง/อยู่กับช่าง/รวม, แกลเลอรี่รูปหลายรูป)
│ ├── admin-goods-receipt.html # Goods Receipt + Import Excel
│ ├── admin-tickets.html # เบิก/คืนอะไหล่ (Ticket workflow) — รวมแท็บ Returns
│ ├── admin-equivalent-groups.html # จัดกลุ่มอะไหล่ทดแทน
│ ├── admin-tracking.html # ติดตาม Serial Number
│ ├── admin-history.html # ประวัติ/Audit log
│ ├── admin-categories.html # หมวดหมู่อะไหล่
│ ├── admin-locations.html # คลัง/สถานที่จัดเก็บ
│ ├── admin-vendors.html # ผู้ขาย/ซัพพลายเออร์
│ ├── admin-users.html # จัดการผู้ใช้งาน
│ ├── admin-atm-models.html # รุ่นตู้ ATM/ADM/CDM
│ └── tech.html # หน้าช่างเทคนิค (เบิก/คืนอะไหล่ของตัวเอง)
├── Launcher/ # start.exe source
└── start.exe # เปิดระบบทั้งหมดด้วย double-click

การติดตั้งและรัน
ข้อกำหนด
.NET 8 SDK
Python 3 (สำหรับ frontend server)
วิธีรัน

วิธีที่ 1 — Double-click start.exe (แนะนำ)

start.exe จะเปิด 2 หน้าต่างอัตโนมัติ:

Backend API: http://localhost:5128
Frontend: http://localhost:3000

และเปิด browser ไปที่ http://localhost:3000/login.html ให้เลย

วิธีที่ 2 — รัน manual

Terminal 1 — Backend:
cd Backend/Api
dotnet run

Terminal 2 — Frontend:
cd Frontend
python -m http.server 3000

Backend จะสร้าง AtmInventory.db (SQLite) อัตโนมัติตอนรันครั้งแรก และรัน lightweight migration ทุกครั้งที่ start (ดู Program.cs) — ไม่ต้องรัน dotnet ef เอง

Login
Role	Email	Password
System Admin	admin@atm.com	admin123
Staff	staff@atm.com	staff123
Auditor	auditor@atm.com	auditor123
Technician	tech@atm.com	tech123
ฟีเจอร์หลัก
Module	หน้า	คำอธิบาย
Dashboard	admin.html	สรุปสต็อก, Stock Alerts, Recurrent Failures (ช่าง+อะไหล่เดิมเบิกซ้ำใน 30 วัน)
Parts Master	admin-parts.html	จัดการอะไหล่, แยกสต็อก คลังกลาง / อยู่กับช่าง / รวม, แกลเลอรี่รูปได้หลายรูป (เลื่อนซ้าย-ขวา), ดูว่าใครถืออะไหล่อยู่บ้าง
Goods Receipt	admin-goods-receipt.html	รับอะไหล่เข้าคลัง, Import Excel (.xlsx)
เบิก/คืนอะไหล่ (Ticket)	admin-tickets.html, tech.html	Ticket workflow เต็มรูปแบบ: sync จาก Aservice → เบิก (รอ→เดินทาง→เบิก) → คืน (รอ→อนุมัติคืน→เดินทาง→คืน), บล็อกอนุมัติถ้าสต็อกไม่พอ, เปลี่ยนเป็นอะไหล่เทียบเคียงได้ (พร้อมโชว์ของที่ขอไปจริง), รองรับหลายใบเบิกต่อ 1 Ticket จาก Aservice, แนบรูป/หมายเหตุตอนเบิก-คืน
Equivalent Groups	admin-equivalent-groups.html	จัดกลุ่มอะไหล่ที่ใช้แทนกันได้, Import จาก Excel
Serial Tracking	admin-tracking.html	ติดตาม Serial Number ของอะไหล่
History	admin-history.html	ประวัติ/Audit log ของ Ticket ทั้งหมด
Categories / Locations / Vendors / Users / ATM Models	admin-*.html	ข้อมูล master ประกอบระบบ

Stock Transfer / Stock Count / Disposal มี API รองรับแล้ว (StockTransferController, StockCountController, DisposalController) แต่ยังไม่มีหน้าจอ frontend แยกในตอนนี้

Ticket Workflow (เบิก/คืนอะไหล่)

ระบบ sync Ticket จาก Aservice ผ่าน POST /api/Ticket/sync (หรือช่างสร้างคำขอเองในหน้า tech.html) แล้วไหลผ่านสถานะ:

ขาเบิก: null (ยังไม่ส่งคำขอ) → รอ (รออนุมัติ) → เดินทาง (อนุมัติแล้ว รอช่างรับของ) → เบิก (รับของแล้ว — ตัดสต็อกจากคลังกลางเข้าคลังช่างตรงนี้)

ขาคืน: รอ (ช่างส่งคำขอคืน) → อนุมัติคืน (Admin ยืนยันรับคืน) → เดินทาง (ช่างจัดส่งของคืนแล้ว) → คืน (Admin ยืนยันได้รับของจริง — ตัดสต็อกจากคลังช่าง คืนเข้าคลังกลางตรงนี้ ตามสภาพ Good/Bad/Lost)

1 เลข Ticket จาก Aservice สามารถมีได้หลายใบเบิกอิสระต่อกัน (เช่น ของหน้างานไม่พอ ขอเพิ่มทีหลัง) ผ่านปุ่ม "เบิกเพิ่ม (ใบใหม่)" ในหน้าช่าง

Database

ใช้ SQLite ไฟล์ Backend/Api/AtmInventory.db (ไม่ commit เข้า git — อยู่ใน .gitignore)
สร้าง/migrate schema อัตโนมัติตอน backend รันครั้งแรกและทุกครั้งที่ start (ดู lightweight migration ใน Program.cs)

รูปภาพอะไหล่/แนบไฟล์เก็บนอก repo ที่ AssetPath ใน appsettings.json (ปกติคือ D:\ATMAssets) — ไม่ commit เข้า git เช่นกัน

ถ้าต้องการ reset ทั้งหมด: ลบไฟล์ AtmInventory.db แล้วรัน backend ใหม่ (จะ seed ผู้ใช้ + ข้อมูลตัวอย่างให้อัตโนมัติ)

Seed ข้อมูลตัวอย่าง

Seed:
POST http://localhost:5128/api/Demo/seed

ดูสถานะ:
GET http://localhost:5128/api/Demo/status

ล้างข้อมูล (เก็บ Parts/Categories ไว้):
DELETE http://localhost:5128/api/Demo/clear

API Endpoints หลัก

POST /api/Auth/login Login (คืน JWT)

GET /api/Parts ดึงรายการอะไหล่ทั้งหมด (แยกสต็อกคลังกลาง/อยู่กับช่าง)
POST /api/Parts เพิ่มอะไหล่ใหม่
GET /api/Parts/{id}/holders ใครถืออะไหล่ชิ้นนี้อยู่บ้าง
POST /api/Parts/{id}/images แนบรูปอะไหล่ (หลายรูปได้)

GET /api/Ticket ดึงรายการ Ticket ทั้งหมด
POST /api/Ticket/sync Sync Ticket จาก Aservice
POST /api/Ticket/additional-withdraw สร้างใบเบิกใหม่ในเลข Ticket เดิม
PUT /api/Ticket/{id}/lines/{lineId}/substitute เปลี่ยนเป็นอะไหล่เทียบเคียง
PUT /api/Ticket/{id}/approve อนุมัติคำขอเบิก
PUT /api/Ticket/{id}/receive ช่างยืนยันรับของ (ตัดสต็อกจริง)
PUT /api/Ticket/{id}/confirm-return Admin ยืนยันได้รับของคืน (คืนสต็อกจริง)

GET /api/GoodsReceipt ประวัติการรับอะไหล่
POST /api/GoodsReceipt สร้าง GR ใหม่
GET /api/Dashboard/alerts Stock Alerts
GET /api/Dashboard/recurrent-failures ช่าง+อะไหล่ที่เบิกซ้ำใน 30 วัน
GET /api/Report/audit-checklist Audit Trail
GET /api/Report/lifecycle Lifecycle Summary

Swagger UI: http://localhost:5128/swagger

Testing

cd Backend/Api.Tests
dotnet test

Test ครอบคลุม Ticket workflow ทั้งวงจร (sync → submit → approve → receive → return → confirm), การเปลี่ยนอะไหล่เทียบเคียง, และ multi-ใบเบิก
