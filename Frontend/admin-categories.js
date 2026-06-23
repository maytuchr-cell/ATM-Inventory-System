let allCategories = [];
let allParts = [];
let editingId = null;

async function init() {
  initLayout();
  applyLang();
  await Promise.all([loadParts(), loadCategories()]);
}

async function loadParts() {
  try { allParts = await api.parts.getAll(); } catch (e) { allParts = []; }
}

async function loadCategories() {
  try {
    allCategories = await api.categories.getAll();
    renderTable();
  } catch (e) {
    showToast(t('toast.network'), 'error');
    document.getElementById('cat-tbody').innerHTML =
      `<tr><td colspan="6" class="empty-state">${t('error.connect')}</td></tr>`;
  }
}

function renderTable() {
  const tbody = document.getElementById('cat-tbody');
  if (!allCategories.length) {
    tbody.innerHTML = `<tr><td colspan="6" class="empty-state">${t('cat.empty')}</td></tr>`;
    return;
  }
  tbody.innerHTML = allCategories.map((c, i) => {
    const count = allParts.filter(p => p.categoryId === c.id && p.isActive).length;
    const statusBadge = c.isActive
      ? `<span class="badge badge-green">${t('lbl.active')}</span>`
      : `<span class="badge badge-gray">${t('lbl.inactive')}</span>`;
    const actions = c.isActive
      ? `<button class="btn btn-secondary btn-xs" onclick="openModal(${c.id})">${t('btn.edit')}</button>
         <button class="btn btn-danger btn-xs" onclick="deleteCategory(${c.id})">${t('btn.delete')}</button>`
      : '';
    return `<tr>
      <td>${i + 1}</td>
      <td><strong>${c.name}</strong></td>
      <td>${c.description ?? '—'}</td>
      <td><span class="badge badge-orange">${count}</span></td>
      <td>${statusBadge}</td>
      <td style="display:flex;gap:6px;">${actions}</td>
    </tr>`;
  }).join('');
}

function openModal(id = null) {
  editingId = id;
  const title = document.getElementById('modal-title');
  if (id) {
    const c = allCategories.find(x => x.id === id);
    if (!c) return;
    title.textContent = t('cat.edit');
    document.getElementById('f-name').value = c.name;
    document.getElementById('f-desc').value = c.description ?? '';
  } else {
    title.textContent = t('cat.add');
    document.getElementById('cat-form').reset();
  }
  document.getElementById('modal-overlay').classList.remove('hidden');
}

function closeModal() {
  document.getElementById('modal-overlay').classList.add('hidden');
  editingId = null;
}

async function saveCategory(e) {
  e.preventDefault();
  const dto = {
    name:        document.getElementById('f-name').value.trim(),
    description: document.getElementById('f-desc').value.trim() || null,
  };
  try {
    if (editingId) await api.categories.update(editingId, dto);
    else           await api.categories.create(dto);
    showToast(t('toast.saved'), 'success');
    closeModal();
    await loadCategories();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

async function deleteCategory(id) {
  if (!confirm(t('cat.del.confirm'))) return;
  try {
    await api.categories.remove(id);
    showToast(t('toast.deleted'), 'success');
    await loadCategories();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

function onLangChange() { applyLang(); renderTable(); }

init();
