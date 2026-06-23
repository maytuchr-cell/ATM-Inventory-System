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
    renderTable();
  } catch (e) {
    showToast(t('toast.network'), 'error');
    document.getElementById('loc-tbody').innerHTML =
      `<tr><td colspan="5" class="empty-state">${t('error.connect')}</td></tr>`;
  }
}

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
    tbody.innerHTML = `<tr><td colspan="5" class="empty-state">${t('loc.empty')}</td></tr>`;
    return;
  }
  tbody.innerHTML = rows.map(l => {
    const typeBadge = `<span class="badge badge-orange">${t('loc.type.' + l.locationType) || l.locationType}</span>`;
    const statusBadge = l.isActive
      ? `<span class="badge badge-green">${t('lbl.active')}</span>`
      : `<span class="badge badge-gray">${t('lbl.inactive')}</span>`;
    const actions = l.isActive
      ? `<button class="btn btn-secondary btn-xs" onclick="openModal(${l.id})">${t('btn.edit')}</button>
         <button class="btn btn-danger btn-xs" onclick="deleteLocation(${l.id})">${t('btn.delete')}</button>`
      : '';
    return `<tr>
      <td><code>${l.code}</code></td>
      <td><strong>${l.name}</strong></td>
      <td>${typeBadge}</td>
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
