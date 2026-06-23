let allVendors = [];
let editingId = null;

async function init() {
  initLayout();
  applyLang();
  await loadVendors();
}

async function loadVendors() {
  try {
    allVendors = await api.vendors.getAll();
    renderTable();
  } catch (e) {
    showToast(t('toast.network'), 'error');
    document.getElementById('vnd-tbody').innerHTML =
      `<tr><td colspan="6" class="empty-state">${t('error.connect')}</td></tr>`;
  }
}

function renderTable() {
  const typeFilter   = document.getElementById('type-filter').value;
  const statusFilter = document.getElementById('status-filter').value;

  let rows = allVendors.filter(v => {
    if (typeFilter   && v.vendorType !== typeFilter) return false;
    if (statusFilter !== '' && String(v.isActive) !== statusFilter) return false;
    return true;
  });

  const tbody = document.getElementById('vnd-tbody');
  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="6" class="empty-state">${t('vnd.empty')}</td></tr>`;
    return;
  }
  tbody.innerHTML = rows.map(v => {
    const typeKey   = `vnd.type.${v.vendorType}`;
    const typeBadge = v.vendorType === 'GRG'
      ? `<span class="badge badge-orange">${t(typeKey)}</span>`
      : `<span class="badge badge-gray">${t(typeKey)}</span>`;
    const statusBadge = v.isActive
      ? `<span class="badge badge-green">${t('lbl.active')}</span>`
      : `<span class="badge badge-gray">${t('lbl.inactive')}</span>`;
    const actions = v.isActive
      ? `<button class="btn btn-secondary btn-xs" onclick="openModal(${v.id})">${t('btn.edit')}</button>
         <button class="btn btn-danger btn-xs" onclick="deleteVendor(${v.id})">${t('btn.delete')}</button>`
      : '';
    return `<tr>
      <td><code>${v.code}</code></td>
      <td><strong>${v.name}</strong></td>
      <td>${typeBadge}</td>
      <td>${v.contactInfo ?? '—'}</td>
      <td>${statusBadge}</td>
      <td style="display:flex;gap:6px;">${actions}</td>
    </tr>`;
  }).join('');
}

function openModal(id = null) {
  editingId = id;
  const title = document.getElementById('modal-title');
  if (id) {
    const v = allVendors.find(x => x.id === id);
    if (!v) return;
    title.textContent = t('vnd.edit');
    document.getElementById('f-name').value    = v.name;
    document.getElementById('f-code').value    = v.code;
    document.getElementById('f-type').value    = v.vendorType;
    document.getElementById('f-contact').value = v.contactInfo ?? '';
  } else {
    title.textContent = t('vnd.add');
    document.getElementById('vnd-form').reset();
  }
  document.getElementById('modal-overlay').classList.remove('hidden');
}

function closeModal() {
  document.getElementById('modal-overlay').classList.add('hidden');
  editingId = null;
}

async function saveVendor(e) {
  e.preventDefault();
  const dto = {
    name:        document.getElementById('f-name').value.trim(),
    code:        document.getElementById('f-code').value.trim(),
    vendorType:  document.getElementById('f-type').value,
    contactInfo: document.getElementById('f-contact').value.trim() || null,
  };
  try {
    if (editingId) await api.vendors.update(editingId, dto);
    else           await api.vendors.create(dto);
    showToast(t('toast.saved'), 'success');
    closeModal();
    await loadVendors();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

async function deleteVendor(id) {
  if (!confirm(t('vnd.del.confirm'))) return;
  try {
    await api.vendors.remove(id);
    showToast(t('toast.deleted'), 'success');
    await loadVendors();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

function onLangChange() { applyLang(); renderTable(); }

init();
