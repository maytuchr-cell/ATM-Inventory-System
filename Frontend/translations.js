const TRANSLATIONS = {
  en: {
    /* ── Common ── */
    'signout': 'Sign Out',
    'connecting': 'Connecting to server…',
    'error.connect': '⚠ Unable to connect to server',

    /* ── Status ── */
    'status.pending':  'Pending',
    'status.awaiting': 'Awaiting Pickup',
    'status.received': 'Received',
    'status.rejected': 'Rejected',
    'status.overdue':  'Overdue Return',

    /* ── Login ── */
    'login.system':   'Spare Parts Management System',
    'login.welcome':  'Welcome back',
    'login.sub':      'Sign in to access your workspace.',
    'login.label':    'Email Address',
    'login.pwlabel':  'Password',
    'login.ph':       'you@atm.com',
    'login.btn':      'Sign In',
    'login.demo':     'Demo Accounts',
    'login.error':    'Invalid email. Please use admin@atm.com or tech@atm.com',
    'login.features.1': 'Real-time inventory stock levels',
    'login.features.2': 'Ticket-based approval workflow',
    'login.features.3': '5-day SLA return tracking',
    'login.features.4': 'Overdue alert & history audit',

    /* ── Admin Navbar ── */
    'admin.title':    'Admin Dashboard',
    'admin.role':     'Administrator',
    'admin.history':  'History & Tracking',

    /* ── Inventory ── */
    'inv.section':  'Inventory Stock',
    'inv.partid':   'Part ID',
    'inv.partname': 'Part Name',
    'inv.stock':    'Stock Quantity',
    'inv.units':    'units',
    'inv.left':     'left',
    'inv.empty':    '⚠ Unable to connect to server',

    /* ── Tickets (Admin) ── */
    'tkt.section':     'Withdrawal Requests',
    'tkt.ticket':      'Ticket',
    'tkt.tech':        'Technician',
    'tkt.dept':        'Department',
    'tkt.requested':   'Requested Part',
    'tkt.issued':      'Issued Part',
    'tkt.status':      'Status',
    'tkt.action':      'Action',
    'tkt.pending.review': 'Pending review',
    'tkt.approve':     'Approve',
    'tkt.reject':      'Reject',
    'tkt.none':        '🎉 No pending requests at this time.',
    'tkt.confirm.approve': 'Approve and issue: "{part}"?',
    'tkt.confirm.reject':  'Reject this withdrawal request?',
    'tkt.err.approve': 'Error: approval failed.',

    /* ── Tech Navbar ── */
    'tech.title': 'Tech Workspace',
    'tech.role':  'Technician',

    /* ── Tech Form ── */
    'form.section':  'Create Withdrawal Ticket',
    'form.name':     'Full Name',
    'form.dept':     'Department / Region',
    'form.phone':    'Phone Number',
    'form.part':     'Select Part',
    'form.ph.name':  'e.g. John Smith',
    'form.ph.dept':  'e.g. South Service Zone',
    'form.ph.phone': '08X-XXX-XXXX',
    'form.ph.part':  '— Select a part —',
    'form.submit':   'Submit Withdrawal Request',
    'form.submitting': 'Submitting…',
    'form.outofstock': 'Out of stock',
    'form.instock':    'In stock',
    'form.err.create': 'Error creating ticket. Please try again.',
    'form.err.server': 'Cannot connect to server. Please check the backend.',

    /* ── Tech History ── */
    'my.section':   'My Ticket History',
    'my.colid':     'Ticket ID',
    'my.colreq':    'Requested Part',
    'my.colissued': 'Issued Part',
    'my.colstatus': 'Status',
    'my.coldue':    'Return Deadline',
    'my.colaction': 'Action',
    'my.confirm':   'Confirm Receipt',
    'my.empty':     'No tickets found. Create your first request above.',
    'my.none.due':  '—',
    'my.confirm.receive': 'Confirm receipt of parts? The 5-day return SLA will start immediately.',

    /* ── History Page ── */
    'hist.title':      'History & Tracking',
    'hist.back':       'Back to Dashboard',
    'hist.section':    'Search & Filter Records',
    'hist.slabel':     'Search Technician',
    'hist.sph':        'Name, department, phone, or TK-xxxx…',
    'hist.flabel':     'Filter by Part',
    'hist.fall':       '— All Parts —',
    'hist.overdue':    'Overdue Returns Only',
    'hist.showing':    'Showing',
    'hist.records':    'records',
    'hist.colticket':  'Ticket',
    'hist.coltech':    'Technician',
    'hist.coldept':    'Department',
    'hist.colpart':    'Part Issued',
    'hist.coldate':    'Request Date',
    'hist.colstatus':  'Status',
    'hist.coldue':     'Return Deadline',
    'hist.noresults':  'No records match your filters.',
    'hist.awaiting':   'Awaiting Pickup',
    'hist.on':         'on',
    'hist.past':       'Past deadline',
    'hist.rejected':   'Rejected',
  },

  th: {
    /* ── Common ── */
    'signout': 'ออกจากระบบ',
    'connecting': 'กำลังเชื่อมต่อ…',
    'error.connect': '⚠ ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ได้',

    /* ── Status ── */
    'status.pending':  'รอดำเนินการ',
    'status.awaiting': 'อนุมัติ – รอรับของ',
    'status.received': 'รับของแล้ว',
    'status.rejected': 'ไม่อนุมัติ',
    'status.overdue':  'ค้างส่งคืน',

    /* ── Login ── */
    'login.system':   'ระบบจัดการคลังอะไหล่ ATM',
    'login.welcome':  'ยินดีต้อนรับ',
    'login.sub':      'เข้าสู่ระบบเพื่อใช้งานระบบคลังอะไหล่',
    'login.label':    'อีเมล',
    'login.pwlabel':  'รหัสผ่าน',
    'login.ph':       'you@atm.com',
    'login.btn':      'เข้าสู่ระบบ',
    'login.demo':     'บัญชีทดสอบ',
    'login.error':    'อีเมลไม่ถูกต้อง กรุณาใช้ admin@atm.com หรือ tech@atm.com',
    'login.features.1': 'ดูสต็อกอะไหล่แบบ Real-time',
    'login.features.2': 'ระบบขอเบิกและอนุมัติแบบตั๋ว',
    'login.features.3': 'ติดตาม SLA คืนซากภายใน 5 วัน',
    'login.features.4': 'แจ้งเตือนและตรวจสอบประวัติการเบิก',

    /* ── Admin Navbar ── */
    'admin.title':   'หน้าควบคุมผู้ดูแลระบบ',
    'admin.role':    'ผู้ดูแลระบบ',
    'admin.history': 'ประวัติและติดตาม',

    /* ── Inventory ── */
    'inv.section':  'คลังอะไหล่',
    'inv.partid':   'รหัสอะไหล่',
    'inv.partname': 'ชื่ออะไหล่',
    'inv.stock':    'จำนวนคงเหลือ',
    'inv.units':    'ชิ้น',
    'inv.left':     'คงเหลือ',
    'inv.empty':    '⚠ ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ได้',

    /* ── Tickets (Admin) ── */
    'tkt.section':     'คำขอเบิกอะไหล่',
    'tkt.ticket':      'ตั๋ว',
    'tkt.tech':        'ช่างเทคนิค',
    'tkt.dept':        'แผนก',
    'tkt.requested':   'อะไหล่ที่ขอ',
    'tkt.issued':      'อะไหล่ที่จ่าย',
    'tkt.status':      'สถานะ',
    'tkt.action':      'การดำเนินการ',
    'tkt.pending.review': 'รอพิจารณา',
    'tkt.approve':     'อนุมัติ',
    'tkt.reject':      'ปฏิเสธ',
    'tkt.none':        '🎉 ไม่มีคำขอเบิกในขณะนี้',
    'tkt.confirm.approve': 'ยืนยันอนุมัติและจ่าย: "{part}"?',
    'tkt.confirm.reject':  'ยืนยันปฏิเสธคำขอเบิกนี้?',
    'tkt.err.approve': 'เกิดข้อผิดพลาด กรุณาลองใหม่',

    /* ── Tech Navbar ── */
    'tech.title': 'พื้นที่ทำงานช่าง',
    'tech.role':  'ช่างเทคนิค',

    /* ── Tech Form ── */
    'form.section':  'สร้างใบเบิกอะไหล่',
    'form.name':     'ชื่อ-นามสกุล',
    'form.dept':     'แผนก / สังกัดเขต',
    'form.phone':    'เบอร์โทรศัพท์',
    'form.part':     'เลือกอะไหล่',
    'form.ph.name':  'เช่น สมชาย ใจดี',
    'form.ph.dept':  'เช่น ศูนย์บริการโซนใต้',
    'form.ph.phone': '08X-XXX-XXXX',
    'form.ph.part':  '— เลือกรายการอะไหล่ —',
    'form.submit':   'ส่งคำขอเบิกอะไหล่',
    'form.submitting': 'กำลังส่งข้อมูล…',
    'form.outofstock': 'สินค้าหมด',
    'form.instock':    'ในคลัง',
    'form.err.create': 'เกิดข้อผิดพลาด กรุณาลองใหม่',
    'form.err.server': 'เชื่อมต่อไม่ได้ กรุณาตรวจสอบเซิร์ฟเวอร์',

    /* ── Tech History ── */
    'my.section':   'ประวัติการเบิกของฉัน',
    'my.colid':     'รหัสตั๋ว',
    'my.colreq':    'อะไหล่ที่ขอ',
    'my.colissued': 'อะไหล่ที่ได้รับ',
    'my.colstatus': 'สถานะ',
    'my.coldue':    'กำหนดคืน (SLA)',
    'my.colaction': 'การจัดการ',
    'my.confirm':   'ยืนยันรับของ',
    'my.empty':     'ยังไม่มีประวัติการเบิก กรุณาสร้างคำขอใหม่',
    'my.none.due':  '—',
    'my.confirm.receive': 'ยืนยันรับอะไหล่? ระบบจะเริ่มนับเวลาส่งคืน 5 วัน',

    /* ── History Page ── */
    'hist.title':     'ประวัติและติดตามสถานะ',
    'hist.back':      'กลับหน้าหลัก',
    'hist.section':   'ค้นหาและกรองข้อมูล',
    'hist.slabel':    'ค้นหาช่าง',
    'hist.sph':       'ชื่อ, แผนก, เบอร์โทร หรือ TK-xxxx…',
    'hist.flabel':    'กรองตามอะไหล่',
    'hist.fall':      '— ดูทั้งหมด —',
    'hist.overdue':   'ค้างส่งคืนเท่านั้น',
    'hist.showing':   'แสดง',
    'hist.records':   'รายการ',
    'hist.colticket': 'ตั๋ว',
    'hist.coltech':   'ช่าง',
    'hist.coldept':   'แผนก',
    'hist.colpart':   'อะไหล่ที่จ่าย',
    'hist.coldate':   'วันที่เบิก',
    'hist.colstatus': 'สถานะ',
    'hist.coldue':    'กำหนดส่งคืน',
    'hist.noresults': 'ไม่พบข้อมูลที่ตรงกับเงื่อนไข',
    'hist.awaiting':  'รอรับของ',
    'hist.on':        'เมื่อ',
    'hist.past':      'เลยกำหนดแล้ว',
    'hist.rejected':  'ไม่อนุมัติ',
  }
};

/* ────────────────────────────────────────────
   Core i18n helpers — included on every page
   ──────────────────────────────────────────── */

function getLang() {
  return localStorage.getItem('lang') || 'en';
}

function t(key) {
  const lang = getLang();
  return (TRANSLATIONS[lang] && TRANSLATIONS[lang][key]) || (TRANSLATIONS['en'][key]) || key;
}

function applyLang() {
  const lang = getLang();
  // Update text nodes
  document.querySelectorAll('[data-i18n]').forEach(el => {
    const key = el.getAttribute('data-i18n');
    el.textContent = t(key);
  });
  // Update placeholders
  document.querySelectorAll('[data-i18n-ph]').forEach(el => {
    el.placeholder = t(el.getAttribute('data-i18n-ph'));
  });
  // Update lang-toggle button label
  const btn = document.getElementById('lang-btn');
  if (btn) btn.innerHTML = lang === 'en'
    ? `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg> TH`
    : `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg> EN`;

  // Switch font family for Thai readability
  document.body.style.fontFamily = lang === 'th'
    ? "'Sarabun', 'DM Sans', sans-serif"
    : "'DM Sans', sans-serif";
}

function toggleLang() {
  const next = getLang() === 'en' ? 'th' : 'en';
  localStorage.setItem('lang', next);
  applyLang();
  // Re-render dynamic content if handler defined on page
  if (typeof onLangChange === 'function') onLangChange();
}
