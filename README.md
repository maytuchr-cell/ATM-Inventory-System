# ATM Inventory System

ระบบจัดการสต็อกอะไหล่ ATM สำหรับ DataOne Asia (Thailand)  
พัฒนาด้วย **.NET 10 Web API** + **Vanilla JS Frontend** + **SQLite**

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10 Web API, EF Core, SQLite |
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
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
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

ใช้ **SQLite** ไฟล์ `Backend/Api/AtmInventory.db`  
สร้างอัตโนมัติตอน backend รันครั้งแรก (EnsureCreated)

> ถ้าต้องการ reset ทั้งหมด: ลบไฟล์ `AtmInventory.db` แล้วรัน backend ใหม่

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
