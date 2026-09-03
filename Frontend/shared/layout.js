/* ── Dynamic sidebar injector ── */
(function() {

  // Shown at the bottom of every page's sidebar — bump this by hand on a real release, it's not
  // tied to the ?v=N cache-bust query string (that's about busting browser caches, not telling
  // the user what version they're on).
  const APP_VERSION = 'v1.0.0';

  // ── Inject Iconify once ───────────────────────────────────────────────────
  if (!document.querySelector('script[src*="iconify-icon"]')) {
    const s = document.createElement('script');
    s.src = 'https://code.iconify.design/iconify-icon/2.1.0/iconify-icon.min.js';
    document.head.appendChild(s);
  }

  function icon(name, size) {
    return `<iconify-icon icon="${name}" width="${size||18}" height="${size||18}" style="display:block"></iconify-icon>`;
  }

  // ── Nav groups (accordion sections) ──────────────────────────────────────
  const NAV_GROUPS = [
    {
      key: 'nav.group.overview',
      labelEN: 'Overview', labelTH: 'ภาพรวม',
      icon: 'ix:dashboard-filled',
      adminOnly: true,
      items: [
        { key: 'nav.dashboard', href: 'admin.html', icon: 'ix:dashboard-filled', adminOnly: true },
      ]
    },
    {
      key: 'nav.group.operations',
      labelEN: 'Operations', labelTH: 'การดำเนินงาน',
      icon: 'solar:box-bold',
      adminOnly: true,
      items: [
        { key: 'nav.tickets',      href: 'admin-tickets.html',       icon: 'mdi:clipboard-check-outline',   adminOnly: true },
        { key: 'nav.dailyreport',  href: 'admin-dhl-report.html',    icon: 'mdi:truck-check-outline',       adminOnly: true },
        { key: 'nav.goodsreceipt', href: 'admin-goods-receipt.html', icon: 'solar:box-bold',                adminOnly: true },
      ]
    },
    {
      key: 'nav.group.reports',
      labelEN: 'Reports & Audit', labelTH: 'รายงานและตรวจสอบ',
      icon: 'mdi:file-report',
      adminOnly: true,
      items: [
        { key: 'nav.history',  href: 'admin-history.html',  icon: 'ic:outline-history',         adminOnly: true },
        { key: 'nav.tracking', href: 'admin-tracking.html', icon: 'mdi:magnify',                adminOnly: true },
        { key: 'nav.auditlog', href: 'admin-audit-log.html', icon: 'mdi:clipboard-text-clock-outline', adminOnly: true },
        { key: 'nav.shortagereport', href: 'admin-shortage-report.html', icon: 'mdi:package-variant-remove', adminOnly: true },
      ]
    },
    {
      key: 'nav.group.masterdata',
      labelEN: 'Master Data', labelTH: 'ข้อมูลหลัก',
      icon: 'bx:hexagon',
      adminOnly: true,
      items: [
        { key: 'nav.parts',      href: 'admin-parts.html',      icon: 'mdi:hexagon-outline',         adminOnly: true },
        { key: 'nav.categories', href: 'admin-categories.html', icon: 'bxs:category-alt',            adminOnly: true },
        { key: 'nav.locations',  href: 'admin-locations.html',  icon: 'weui:location-filled',        adminOnly: true },
        { key: 'nav.vendors',    href: 'admin-vendors.html',    icon: 'fa6-solid:warehouse',         adminOnly: true },
        { key: 'nav.atmmodels',  href: 'admin-atm-models.html', icon: 'streamline-plump:cog-solid',  adminOnly: true },
        { key: 'nav.equivgroups', href: 'admin-equivalent-groups.html', icon: 'mdi:vector-link',      adminOnly: true },
      ]
    },
    {
      key: 'nav.group.admin',
      labelEN: 'Administration', labelTH: 'ผู้ดูแลระบบ',
      icon: 'mdi:shield-account',
      adminOnly: true,
      items: [
        { key: 'nav.users', href: 'admin-users.html', icon: 'mdi:account-multiple', adminOnly: true, systemAdminOnly: true },
      ]
    },
    {
      key: 'nav.group.tools',
      labelEN: 'Tools', labelTH: 'เครื่องมือ',
      icon: 'mdi:tools',
      adminOnly: false,
      items: [
        { key: 'nav.workspace', href: 'tech.html', icon: 'mdi:tools', adminOnly: false },
        { key: 'nav.addresses', href: 'tech-addresses.html', icon: 'mdi:map-marker', adminOnly: false },
      ]
    },
  ];

  // ── Theme ──────────────────────────────────────────────────────────────────
  function applyTheme(th) {
    document.documentElement.setAttribute('data-theme', th);
    localStorage.setItem('theme', th);
    const icon = document.getElementById('theme-icon');
    if (icon) {
      const MOON = `<path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9z"/>`;
      const SUN  = `<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/>`;
      icon.innerHTML = th === 'dark' ? MOON : SUN;
    }
  }

  window.toggleTheme = function() {
    const current = document.documentElement.getAttribute('data-theme') || 'light';
    applyTheme(current === 'dark' ? 'light' : 'dark');
  };

  // ── Sidebar collapse (desktop: icon-only width) / open (mobile: off-canvas) ──
  const isMobile = () => window.matchMedia('(max-width: 768px)').matches;

  window.toggleSidebar = function() {
    const sidebar = document.querySelector('.sidebar');
    const btn = document.getElementById('hamburger-btn');
    if (isMobile()) {
      const open = sidebar.classList.toggle('mobile-open');
      if (btn) btn.innerHTML = open ? '✕' : '☰';
      return;
    }
    const collapsed = sidebar.classList.toggle('collapsed');
    localStorage.setItem('sidebarCollapsed', collapsed ? '1' : '0');
    if (btn) btn.innerHTML = collapsed ? '☰' : '✕';
  };

  // Tapping a nav link, or the page behind the off-canvas sidebar, closes it on mobile.
  document.addEventListener('click', e => {
    if (!isMobile()) return;
    const sidebar = document.querySelector('.sidebar');
    if (!sidebar || !sidebar.classList.contains('mobile-open')) return;
    if (sidebar.contains(e.target)) {
      if (e.target.closest('.sidebar-nav-item')) toggleSidebar();
      return;
    }
    toggleSidebar();
  });

  // ── Group accordion ────────────────────────────────────────────────────────
  window.toggleNavGroup = function(groupKey) {
    const el = document.querySelector(`.nav-group[data-key="${groupKey}"]`);
    if (!el) return;
    const sidebar = document.querySelector('.sidebar');
    // Don't collapse groups when sidebar is collapsed (icons-only mode)
    if (sidebar && sidebar.classList.contains('collapsed')) return;

    const open = el.classList.toggle('open');
    // persist
    const saved = JSON.parse(localStorage.getItem('navGroups') || '{}');
    saved[groupKey] = open;
    localStorage.setItem('navGroups', JSON.stringify(saved));
  };

  // ── Sign out ───────────────────────────────────────────────────────────────
  function signOut() {
    const role = (localStorage.getItem('userRole') || '').toLowerCase();
    const isAdminSide = ['systemadmin', 'staff', 'auditor', 'admin'].includes(role);
    localStorage.removeItem('authToken');
    localStorage.removeItem('userRole');
    localStorage.removeItem('userEmail');
    localStorage.removeItem('userName');
    window.location.href = isAdminSide ? 'login.html' : 'login-tech.html';
  }

  // ── Build sidebar ──────────────────────────────────────────────────────────
  window.initLayout = function() {
    const root = document.getElementById('sidebar-root');
    if (!root) return;

    const role  = localStorage.getItem('userRole') || 'tech';
    const email = localStorage.getItem('userEmail') || '';
    const displayName = localStorage.getItem('userName') || email;
    const page  = location.pathname.split('/').pop() || 'index.html';

    // Role flags (4 roles: SystemAdmin / Staff / Auditor / Tech; "admin" kept for legacy)
    const r = role.toLowerCase();
    const isAdminSide   = ['systemadmin', 'staff', 'auditor', 'admin'].includes(r);
    const isSystemAdmin = (r === 'systemadmin' || r === 'admin');
    const isReadOnly    = (r === 'auditor');
    // Auditor sees everything but cannot write. Hide write controls (the unambiguous write
    // button classes are hidden via CSS); for the mixed .btn-primary class, tag the write
    // ones with [data-write] here — but leave read actions (Export / Search) visible.
    document.body.classList.toggle('readonly-mode', isReadOnly);
    if (isReadOnly) {
      const READ_ONCLICK = /export|search|dosearch|view|detail|toggle|filter/i;
      const tagWriteButtons = (rootEl) => {
        rootEl.querySelectorAll('.btn-primary:not([data-write])').forEach(btn => {
          const on = (btn.getAttribute('onclick') || '') + ' ' + (btn.textContent || '');
          if (!READ_ONCLICK.test(on)) btn.setAttribute('data-write', '');
        });
      };
      tagWriteButtons(document);
      // Re-tag dynamically rendered buttons (table rows, modals opened later).
      new MutationObserver(muts => {
        for (const m of muts) for (const n of m.addedNodes)
          if (n.nodeType === 1) tagWriteButtons(n.matches?.('.btn-primary') ? n.parentElement || document : n);
      }).observe(document.body, { childList: true, subtree: true });
    }

    const isCollapsed    = localStorage.getItem('sidebarCollapsed') === '1';
    const savedGroups    = JSON.parse(localStorage.getItem('navGroups') || '{}');

    // Build nav HTML
    const navHtml = NAV_GROUPS
      .filter(g => !g.adminOnly || isAdminSide)
      .map(group => {
        const visibleItems = group.items.filter(i =>
          (!i.adminOnly || isAdminSide) && (!i.systemAdminOnly || isSystemAdmin));
        if (!visibleItems.length) return '';

        // Check if any item in this group is the active page
        const groupHasActive = visibleItems.some(i => i.href === page);

        // Open if: has active page, OR was previously opened and not explicitly closed
        const isOpen = groupHasActive || (savedGroups[group.key] !== false && savedGroups[group.key] !== undefined ? savedGroups[group.key] : groupHasActive);

        const groupLabel = (typeof t === 'function' && getLang() === 'th') ? group.labelTH : group.labelEN;

        const itemsHtml = visibleItems.map(item => {
          const active  = page === item.href ? 'active' : '';
          const label   = typeof t === 'function' ? t(item.key) : item.key;
          return `<a class="sidebar-nav-item ${active}" href="${item.href}" data-label="${label}">
            <span class="sidebar-nav-icon">${icon(item.icon)}</span>
            <span class="sidebar-nav-label">${label}</span>
          </a>`;
        }).join('');

        return `
          <div class="nav-group ${isOpen ? 'open' : ''}" data-key="${group.key}">
            <div class="nav-group-header" onclick="toggleNavGroup('${group.key}')">
              <span class="nav-group-label">${groupLabel}</span>
              <span class="nav-group-chevron">▶</span>
            </div>
            <div class="nav-group-items">${itemsHtml}</div>
          </div>`;
      }).join('');

    const roleLabel = role === 'admin'
      ? (typeof t === 'function' ? t('admin.role') : 'Administrator')
      : (typeof t === 'function' ? t('tech.role')  : 'Technician');
    const initials = displayName ? displayName[0].toUpperCase() : '?';

    root.innerHTML = `
      <aside class="sidebar ${isCollapsed ? 'collapsed' : ''}">

        <!-- Header + Hamburger -->
        <div class="sidebar-header">
          <div class="sidebar-logo-wrap">
            <img src="assets/logo.png" alt="Logo" style="width:100%;max-width:148px;height:auto;object-fit:contain;">
          </div>
          <button class="hamburger-btn" id="hamburger-btn" onclick="toggleSidebar()" title="สลับแถบเมนู">
            ${isMobile() || isCollapsed ? '☰' : '✕'}
          </button>
        </div>

        <!-- Grouped Nav -->
        <nav class="sidebar-nav">${navHtml}</nav>

        <!-- Footer -->
        <div class="sidebar-footer">
          <div class="user-row">
            <div class="user-avatar">${initials}</div>
            <div class="user-info">
              <div class="user-email">${displayName}</div>
              <div class="user-role">${roleLabel}</div>
            </div>
          </div>
          <div class="sidebar-footer-controls">
            <button class="ctrl-btn" style="flex:1;justify-content:center;" id="lang-btn" onclick="toggleLang()"></button>
            <button class="ctrl-btn" style="padding:0 10px;" onclick="toggleTheme()" title="สลับธีม">
              <svg id="theme-icon" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/>
              </svg>
            </button>
            <button class="ctrl-btn btn-danger btn-sm" style="padding:0 10px;" onclick="signOut()" title="ออกจากระบบ">⏏</button>
          </div>
          <div class="sidebar-version">ATM Inventory ${APP_VERSION}</div>
        </div>

      </aside>`;

    applyTheme(localStorage.getItem('theme') || 'light');
    if (typeof applyLang === 'function') applyLang();

    // Sync main-content margin when sidebar collapses
    _syncMainMargin();
  };

  function _syncMainMargin() {
    const sidebar = document.querySelector('.sidebar');
    const main    = document.querySelector('.main-content');
    if (!sidebar || !main) return;
    const updateMargin = () => {
      // Mobile: sidebar is off-canvas (overlay), so main-content stays full width — an inline
      // margin-left here would permanently beat the @media(max-width:768px) rule in styles.css
      // since inline styles win over stylesheet rules regardless of viewport.
      main.style.marginLeft = isMobile() ? '0'
        : sidebar.classList.contains('collapsed') ? 'var(--sidebar-collapsed-w)'
        : 'var(--sidebar-w)';
    };
    updateMargin();
    // watch for class changes caused by toggleSidebar, and viewport crossing the mobile breakpoint
    const obs = new MutationObserver(updateMargin);
    obs.observe(sidebar, { attributes: true, attributeFilter: ['class'] });
    window.addEventListener('resize', updateMargin);
  }

  window.signOut = signOut;
})();
