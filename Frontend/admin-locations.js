const LOCATION_TYPES = [
  'DHL_CENTER','RATCHABURANA','GRG','OL_TECHNICIAN','IN_TRANSIT',
  'TRANSPORT_HUB','AIRPORT','SCRAP','LOCAL_VENDOR'
];

let allLocations = [];
let editingId = null;

function buildTypeOptions(selectEl, selectedVal = '') {
  selectEl.innerHTML = `<option value="">${t('lbl.all')}</option>`;
  LOCATION_TYPES.forEach(type => {
    const o = document.createElement('option');
    o.value = type;
    o.textContent = t(`loc.type.${type}`);
    if (type === selectedVal) o.selected = true;
    selectEl.appendChild(o);
  });
}

async function init() {
  initLayout();
  applyLang();
  buildTypeOptions(document.getElementById('type-filter'));
  buildTypeOptions(document.getElementById('f-type'));
  await loadLocations();
}

async function loadLocations() {
  try {
    allLocations = await api.locations.getAll();
    updateLocationStats();
    renderTable();
  } catch (e) {
    showToast(t('toast.network'), 'error');
    document.getElementById('loc-tbody').innerHTML =
      `<tr><td colspan="9" class="empty-state">${t('error.connect')}</td></tr>`;
  }
}

function updateLocationStats() {
  const elTotal = document.getElementById('stat-total-locs');
  const elDhl = document.getElementById('stat-dhl-stock');
  const elDhlSub = document.getElementById('stat-dhl-sub');
  const elRat = document.getElementById('stat-rat-stock');
  const elRatSub = document.getElementById('stat-rat-sub');
  const elTech = document.getElementById('stat-tech-stock');
  const elTechSub = document.getElementById('stat-tech-sub');

  if (elTotal) elTotal.textContent = allLocations.length.toLocaleString() + ' แห่ง';

  const dhl = allLocations.find(l => l.code === 'DHL-BKK') || {};
  const rat = allLocations.find(l => l.code === 'WH-RAT' || l.locationType === 'RATCHABURANA') || {};
  const tech = allLocations.find(l => l.code === 'OL-TECH' || l.locationType === 'OL_TECHNICIAN') || {};

  if (elDhl) elDhl.textContent = (dhl.goodQty || 0).toLocaleString() + ' ชิ้น';
  if (elDhlSub) elDhlSub.textContent = `ดี ${(dhl.goodQty || 0).toLocaleString()} | ซ่อม ${(dhl.repairQty || 0).toLocaleString()} | S/N ${(dhl.serialUnitsCount || 0).toLocaleString()}`;

  if (elRat) elRat.textContent = (rat.repairQty || 0).toLocaleString() + ' ชิ้น';
  if (elRatSub) elRatSub.textContent = `รอซ่อม ${(rat.repairQty || 0).toLocaleString()} | S/N ${(rat.serialUnitsCount || 0).toLocaleString()} ตัว`;

  if (elTech) elTech.textContent = (tech.goodQty || 0).toLocaleString() + ' ชิ้น';
  if (elTechSub) elTechSub.textContent = `ช่างถือ ${(tech.goodQty || 0).toLocaleString()} | S/N ${(tech.serialUnitsCount || 0).toLocaleString()} ตัว`;
}

const LOCATION_ICONS = {
  DHL_CENTER:     { icon: 'mdi:warehouse', color: '#16a34a' },
  RATCHABURANA:   { icon: 'mdi:home-repair-service', color: '#ea580c' },
  GRG:            { icon: 'mdi:cog-box', color: '#0284c7' },
  OL_TECHNICIAN:  { icon: 'mdi:account-wrench', color: '#2563eb' },
  IN_TRANSIT:     { icon: 'mdi:truck-fast-outline', color: '#d97706' },
  AIRPORT:        { icon: 'mdi:airplane-takeoff', color: '#06b6d4' },
  SCRAP:          { icon: 'mdi:delete-sweep', color: '#64748b' },
  TRANSPORT_HUB:  { icon: 'mdi:transit-connection-variant', color: '#8b5cf6' },
  LOCAL_VENDOR:   { icon: 'mdi:storefront-outline', color: '#ec4899' }
};

function renderTable() {
  const typeFilter   = document.getElementById('type-filter').value;
  const statusFilter = document.getElementById('status-filter').value;

  let rows = allLocations.filter(l => {
    if (typeFilter   && l.locationType !== typeFilter) return false;
    if (statusFilter !== '' && String(l.isActive) !== statusFilter) return false;
    return true;
  });

  const tbody = document.getElementById('loc-tbody');
  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="9" class="empty-state">${t('loc.empty')}</td></tr>`;
    return;
  }
  tbody.innerHTML = rows.map(l => {
    const locMeta = LOCATION_ICONS[l.locationType] || { icon: 'mdi:map-marker-outline', color: 'var(--text-secondary)' };
    const typeBadge = `<span class="badge badge-orange">${t('loc.type.' + l.locationType) || l.locationType}</span>`;
    const statusBadge = l.isActive
      ? `<span class="badge badge-green">${t('lbl.active')}</span>`
      : `<span class="badge badge-gray">${t('lbl.inactive')}</span>`;
    const actions = l.isActive
      ? `<button class="btn btn-secondary btn-xs" onclick="openModal(${l.id})">${t('btn.edit')}</button>
         <button class="btn btn-danger btn-xs" onclick="deleteLocation(${l.id})">${t('btn.delete')}</button>`
      : '';

    const goodCell = (l.goodQty || 0) > 0
      ? `<strong style="color:#16a34a;">${(l.goodQty || 0).toLocaleString()}</strong> <span style="font-size:11px;color:var(--text-secondary);">ชิ้น</span>`
      : `<span style="color:var(--text-muted);font-size:12px;">0</span>`;

    const repairCell = (l.repairQty || 0) > 0
      ? `<strong style="color:#ea580c;">${(l.repairQty || 0).toLocaleString()}</strong> <span style="font-size:11px;color:var(--text-secondary);">ชิ้น</span>`
      : `<span style="color:var(--text-muted);font-size:12px;">0</span>`;

    const snCell = (l.serialUnitsCount || 0) > 0
      ? `<a href="admin-serials.html?search=${encodeURIComponent(l.code)}" class="badge" style="background:rgba(249,115,22,0.12);color:var(--orange);text-decoration:none;font-weight:700;" title="คลิกดู Serial Numbers ในคลังนี้">${(l.serialUnitsCount || 0).toLocaleString()} ตัว</a>`
      : `<span style="color:var(--text-muted);font-size:12px;">—</span>`;

    const skusCell = (l.partTypesCount || 0) > 0
      ? `<span style="font-weight:600;">${(l.partTypesCount || 0).toLocaleString()}</span> <span style="font-size:11px;color:var(--text-secondary);">รายการ</span>`
      : `<span style="color:var(--text-muted);font-size:12px;">0</span>`;

    return `<tr>
      <td><code>${l.code}</code></td>
      <td>
        <div style="display:inline-flex; align-items:center; gap:8px;">
          <iconify-icon icon="${locMeta.icon}" width="18" style="color:${locMeta.color}; flex-shrink:0;"></iconify-icon>
          <strong>${l.name}</strong>
        </div>
      </td>
      <td>${typeBadge}</td>
      <td style="text-align:right;">${goodCell}</td>
      <td style="text-align:right;">${repairCell}</td>
      <td style="text-align:center;">${snCell}</td>
      <td style="text-align:right;">${skusCell}</td>
      <td>${statusBadge}</td>
      <td style="display:flex;gap:6px;">${actions}</td>
    </tr>`;
  }).join('');
}

function openModal(id = null) {
  editingId = id;
  buildTypeOptions(document.getElementById('f-type'));
  const title = document.getElementById('modal-title');
  if (id) {
    const l = allLocations.find(x => x.id === id);
    if (!l) return;
    title.textContent = t('loc.edit');
    document.getElementById('f-name').value = l.name;
    document.getElementById('f-code').value = l.code;
    buildTypeOptions(document.getElementById('f-type'), l.locationType);
  } else {
    title.textContent = t('loc.add');
    document.getElementById('loc-form').reset();
    buildTypeOptions(document.getElementById('f-type'));
  }
  document.getElementById('modal-overlay').classList.remove('hidden');
}

function closeModal() {
  document.getElementById('modal-overlay').classList.add('hidden');
  editingId = null;
}

async function saveLocation(e) {
  e.preventDefault();
  const dto = {
    name:         document.getElementById('f-name').value.trim(),
    code:         document.getElementById('f-code').value.trim(),
    locationType: document.getElementById('f-type').value,
  };
  try {
    if (editingId) await api.locations.update(editingId, dto);
    else           await api.locations.create(dto);
    showToast(t('toast.saved'), 'success');
    closeModal();
    await loadLocations();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

async function deleteLocation(id) {
  if (!confirm(t('loc.del.confirm'))) return;
  try {
    await api.locations.remove(id);
    showToast(t('toast.deleted'), 'success');
    await loadLocations();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

function onLangChange() {
  applyLang();
  buildTypeOptions(document.getElementById('type-filter'));
  renderTable();
}

init();
