/* ── Dynamic sidebar injector ── */
(function() {

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
        { key: 'nav.goodsreceipt', href: 'admin-goods-receipt.html', icon: 'solar:box-bold',                adminOnly: true },
        { key: 'nav.returns',      href: 'admin-returns.html',       icon: 'streamline:return-2-solid',     adminOnly: true },
        { key: 'nav.transfers',    href: 'admin-transfers.html',     icon: 'streamline:transfer-van-solid', adminOnly: true },
      ]
    },
    {
      key: 'nav.group.stockcontrol',
      labelEN: 'Stock Control', labelTH: 'ควบคุมสต็อก',
      icon: 'mdi:counter',
      adminOnly: true,
      items: [
        { key: 'nav.stockcount', href: 'admin-stockcount.html', icon: 'mdi:counter',         adminOnly: true },
        { key: 'nav.disposal',   href: 'admin-disposal.html',   icon: 'fa6-solid:trash-can', adminOnly: true },
      ]
    },
    {
      key: 'nav.group.reports',
      labelEN: 'Reports & Audit', labelTH: 'รายงานและตรวจสอบ',
      icon: 'mdi:file-report',
      adminOnly: true,
      items: [
        { key: 'nav.reports',  href: 'admin-reports.html',  icon: 'mdi:file-report',            adminOnly: true },
        { key: 'nav.history',  href: 'admin-history.html',  icon: 'ic:outline-history',         adminOnly: true },
        { key: 'nav.tracking', href: 'admin-tracking.html', icon: 'mdi:magnify',                adminOnly: true },
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
      ]
    },
    {
      key: 'nav.group.tools',
      labelEN: 'Tools', labelTH: 'เครื่องมือ',
      icon: 'mdi:tools',
      adminOnly: false,
      items: [
        { key: 'nav.workspace', href: 'tech.html', icon: 'mdi:tools', adminOnly: false },
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

  // ── Sidebar collapse ───────────────────────────────────────────────────────
  window.toggleSidebar = function() {
    const sidebar = document.querySelector('.sidebar');
    const collapsed = sidebar.classList.toggle('collapsed');
    localStorage.setItem('sidebarCollapsed', collapsed ? '1' : '0');
    // update hamburger icon
    const btn = document.getElementById('hamburger-btn');
    if (btn) btn.innerHTML = collapsed ? '☰' : '✕';
  };

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
    localStorage.removeItem('authToken');
    localStorage.removeItem('userRole');
    localStorage.removeItem('userEmail');
    window.location.href = 'login.html';
  }

  // ── Build sidebar ──────────────────────────────────────────────────────────
  window.initLayout = function() {
    const root = document.getElementById('sidebar-root');
    if (!root) return;

    const role  = localStorage.getItem('userRole') || 'tech';
    const email = localStorage.getItem('userEmail') || '';
    const page  = location.pathname.split('/').pop() || 'index.html';

    const isCollapsed    = localStorage.getItem('sidebarCollapsed') === '1';
    const savedGroups    = JSON.parse(localStorage.getItem('navGroups') || '{}');

    // Build nav HTML
    const navHtml = NAV_GROUPS
      .filter(g => !g.adminOnly || role === 'admin')
      .map(group => {
        const visibleItems = group.items.filter(i => !i.adminOnly || role === 'admin');
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
    const initials = email ? email[0].toUpperCase() : '?';

    root.innerHTML = `
      <aside class="sidebar ${isCollapsed ? 'collapsed' : ''}">

        <!-- Header + Hamburger -->
        <div class="sidebar-header">
          <div class="sidebar-logo-wrap">
            <img src="assets/logo.png" alt="Logo" style="width:100%;max-width:148px;height:auto;object-fit:contain;">
          </div>
          <button class="hamburger-btn" id="hamburger-btn" onclick="toggleSidebar()" title="Toggle sidebar">
            ${isCollapsed ? '☰' : '✕'}
          </button>
        </div>

        <!-- Grouped Nav -->
        <nav class="sidebar-nav">${navHtml}</nav>

        <!-- Footer -->
        <div class="sidebar-footer">
          <div class="user-row">
            <div class="user-avatar">${initials}</div>
            <div class="user-info">
              <div class="user-email">${email}</div>
              <div class="user-role">${roleLabel}</div>
            </div>
          </div>
          <div class="sidebar-footer-controls">
            <button class="ctrl-btn" style="flex:1;justify-content:center;" id="lang-btn" onclick="toggleLang()"></button>
            <button class="ctrl-btn" style="padding:0 10px;" onclick="toggleTheme()" title="Toggle theme">
              <svg id="theme-icon" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/>
              </svg>
            </button>
            <button class="ctrl-btn btn-danger btn-sm" style="padding:0 10px;" onclick="signOut()" title="Sign Out">⏏</button>
          </div>
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
      main.style.marginLeft = sidebar.classList.contains('collapsed')
        ? 'var(--sidebar-collapsed-w)'
        : 'var(--sidebar-w)';
    };
    updateMargin();
    // watch for class changes caused by toggleSidebar
    const obs = new MutationObserver(updateMargin);
    obs.observe(sidebar, { attributes: true, attributeFilter: ['class'] });
  }

  window.signOut = signOut;
})();
