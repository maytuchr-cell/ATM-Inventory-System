# ATM Inventory System

ระบบจัดการสต็อกอะไหล่ ATM สำหรับ DataOne Asia (Thailand)  
พัฒนาด้วย **.NET 9 Web API** + **Vanilla JS Frontend** + **SQLite**

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 9 Web API, EF Core, SQLite |
| Frontend | HTML/CSS/Vanilla JS (ไม่มี framework) |
| Launcher | .NET Console App (start.exe) |

---

## โครงสร้างโปรเจกต์

```
ATM-Inventory-System/
├── Backend/
│   └── Api/                  # ASP.NET Core Web API (port 5128)
│       ├── Controllers/      # API endpoints
│       ├── Models/           # EF Core models
│       ├── Services/         # StockService (business logic)
│       └── AppDbContext.cs
├── Frontend/                 # Static HTML/JS/CSS (port 3000)
│   ├── shared/               # styles.css, layout.js, api.js, translations.js
│   ├── admin.html            # Dashboard
│   ├── admin-parts.html      # Parts Master
│   ├── admin-goods-receipt.html  # Goods Receipt + Excel Import
│   ├── admin-reports.html    # Audit & Lifecycle Report + Excel Export
│   └── ...
├── Launcher/                 # start.exe source
└── start.exe                 # เปิดระบบทั้งหมดด้วย double-click
```

---

## การติดตั้งและรัน

### ข้อกำหนด
- [.NET 9 SDK](https://dotnet.microsoft.com/download) — ตรวจสอบด้วย `dotnet --list-sdks` ว่ามี `9.x` แล้ว (ใช้ net9.0 เพราะ Visual Studio 2022 ยังไม่รองรับ target net10.0 ใน IDE ต้องใช้ VS 2026/18.x ถึงจะ target net10 ได้)
- Python 3 (สำหรับ frontend server)

### วิธีรัน

**วิธีที่ 1 — Double-click `start.exe`** (แนะนำ)

`start.exe` จะเปิด 2 หน้าต่างอัตโนมัติ:
- Backend API: `http://localhost:5128`
- Frontend: `http://localhost:3000`

และเปิด browser ไปที่ `http://localhost:3000/login.html` ให้เลย

---

**วิธีที่ 2 — รัน manual**

```bash
# Terminal 1 — Backend
cd Backend/Api
"C:\Program Files\dotnet\dotnet.exe" run

# Terminal 2 — Frontend
cd Frontend
python -m http.server 3000
```

---

## Login

| Role | Email | Password |
|---|---|---|
| Admin | admin@atm.com | admin123 |
| Technician | tech@atm.com | tech123 |

---

## ฟีเจอร์หลัก (Phase 1)

| Module | หน้า | คำอธิบาย |
|---|---|---|
| Dashboard | admin.html | สรุปสต็อก, Stock Alerts, Top Parts |
| Parts Master | admin-parts.html | จัดการอะไหล่ทั้งหมด, ค้นหาด้วย Serial No. |
| Goods Receipt | admin-goods-receipt.html | รับอะไหล่เข้า, **Import Excel (.xlsx)** จาก GRG |
| Issue / Ticket | admin.html | เบิกอะไหล่ให้ช่าง, อนุมัติ/ปฏิเสธ |
| Returns | admin-returns.html | คืนอะไหล่จากช่าง |
| Stock Transfer | admin-transfers.html | โอนสต็อกระหว่างคลัง |
| Stock Count | admin-stockcount.html | นับสต็อกและ reconcile |
| Disposal | admin-disposal.html | ทำลายอะไหล่ชำรุด |
| Reports | admin-reports.html | Audit Checklist + Lifecycle, **Export Excel** |
| Serial Tracking | admin-tracking.html | ติดตาม Serial Number |
| Equivalent Groups | admin-equivalent-groups.html | จัดกลุ่มอะไหล่ทดแทน |

---

## Excel Import (Goods Receipt)

รองรับไฟล์ `.xlsx` จาก GRG ที่มี column:

| Column | ระบบ |
|---|---|
| Part Number | Part No. |
| D1 Part Description | Part Name (auto-create ถ้าไม่มีในระบบ) |
| Serial Number | Serial No. |
| Quantity | Qty |
| Status (GOOD/BAD) | Condition |

> ระบบจะ **auto-create Part** ถ้า Part No. ยังไม่มีในระบบ

---

## Seed ข้อมูลตัวอย่าง

```bash
# Seed
POST http://localhost:5128/api/Demo/seed

# ดูสถานะ
GET http://localhost:5128/api/Demo/status

# ล้างข้อมูล (เก็บ Parts/Categories ไว้)
DELETE http://localhost:5128/api/Demo/clear
```

---

## Database

ใช้ **SQLite** ไฟล์ `Backend/Api/AtmInventory.db` เป็นค่า default  
สร้างอัตโนมัติตอน backend รันครั้งแรก (EnsureCreated)

> ถ้าต้องการ reset ทั้งหมด: ลบไฟล์ `AtmInventory.db` แล้วรัน backend ใหม่

### ย้ายไปใช้ MySQL

โปรเจกต์รองรับสลับไปใช้ MySQL ได้แล้วผ่าน `Database:Provider` ใน `appsettings.json`
(`"Sqlite"` ค่า default หรือ `"MySql"`) ดูรายละเอียดวิธีสลับ, ไฟล์ migration
(`Backend/Api/Migrations/MySql/`), และ SQL script พร้อมใช้ได้ที่
[`Backend/Api/Migrations/MySql/README.md`](Backend/Api/Migrations/MySql/README.md)

> **หมายเหตุ:** ไฟล์ `database/AtmInventory.db` ในโฟลเดอร์ root เป็นข้อมูลจาก branch/เวอร์ชันเก่าที่ schema
> ไม่ตรงกับโค้ดปัจจุบัน (ขาด `StockQuantity`, ไม่มีตาราง `PartImages`/`PartUnits`/`TicketAttachments`) —
> **ห้ามเอาไปใช้กับโค้ดชุดนี้ตรงๆ** ให้ปล่อยให้แอปสร้าง/seed ข้อมูลใหม่ตามโค้ดปัจจุบันแทน

---

## Deploy ขึ้น IIS

รัน `Publish-IIS.ps1` (หรือดับเบิลคลิก `Publish-IIS.bat`) คำสั่งเดียว จะได้โฟลเดอร์ `publish/`
พร้อมก็อปปี้ขึ้นเซิร์ฟเวอร์ทันที ไม่ต้อง build/copy เองทีละไฟล์:

```powershell
.\Publish-IIS.ps1                 # framework-dependent (เซิร์ฟเวอร์ต้องลง ASP.NET Core 9 Hosting Bundle)
.\Publish-IIS.ps1 -SelfContained  # bundle .NET runtime มาด้วย ไม่ต้องลง Hosting Bundle บนเซิร์ฟเวอร์ (ไฟล์ใหญ่ขึ้น)
```

โครงสร้างที่ได้ (ตรงกับ IIS: 1 site + `/api` เป็น sub-application):

```
publish/            → เอาไปวางเป็น physical path ของ IIS site (root = Frontend)
publish/api/         → เอา subfolder นี้ตั้งเป็น IIS Application "/api" (คลิกขวา → Convert to Application, แยก App Pool)
                       มี web.config ติดมาด้วยแล้ว ใช้ ASP.NET Core Module ตรง
```

ก่อน publish จริงไป production ต้องตั้ง `Database:Provider=MySql` และใส่ connection string
ให้ MySQL/`Sparepart_DB` (ผ่าน environment variable บน IIS app pool หรือ `appsettings.Production.json`
ที่ไม่ commit ขึ้น git) — ดูรายละเอียดที่
[`Backend/Api/Migrations/MySql/README.md`](Backend/Api/Migrations/MySql/README.md)

> `Launcher`/`start.exe` **ไม่เกี่ยวกับการ deploy ขึ้น IIS เลย** — ตัวนั้นไว้ใช้เปิดระบบบนเครื่อง dev
> เท่านั้น (ดับเบิลคลิกแล้วเปิด backend + frontend แยกกันเป็น cmd window) บน IIS จริง ตัว IIS
> จะคุม process ของ backend เองทั้งหมดผ่าน `web.config`

---

## API Endpoints หลัก

```
GET    /api/Parts              ดึงรายการอะไหล่ทั้งหมด
POST   /api/Parts              เพิ่มอะไหล่ใหม่
GET    /api/GoodsReceipt       ประวัติการรับอะไหล่
POST   /api/GoodsReceipt       สร้าง GR ใหม่
GET    /api/Dashboard/alerts   Stock Alerts
GET    /api/Report/audit-checklist  Audit Trail
GET    /api/Report/lifecycle   Lifecycle Summary
```

Swagger UI: `http://localhost:5128/swagger`
