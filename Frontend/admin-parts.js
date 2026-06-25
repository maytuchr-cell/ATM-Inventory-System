let allParts = [];
let allCategories = [];
let editingId = null;
let currentPage = 1;
const PAGE_SIZE = 50;
const IMG_BASE = API_BASE.replace(/\/api$/, '');

function resetPage() { currentPage = 1; }

async function init() {
  initLayout();
  applyLang();
  await Promise.all([loadCategories(), loadParts()]);
}

async function loadCategories() {
  try {
    allCategories = await api.categories.getAll({ isActive: true });
    const catFilter = document.getElementById('cat-filter');
    const catOpt0   = catFilter.querySelector('option[value=""]');
    catFilter.innerHTML = '';
    catFilter.appendChild(catOpt0);
    const noneOpt = document.createElement('option');
    noneOpt.value = '__none__'; noneOpt.textContent = '— No Category —';
    catFilter.appendChild(noneOpt);
    allCategories.forEach(c => {
      const o = document.createElement('option');
      o.value = c.id; o.textContent = c.name;
      catFilter.appendChild(o);
    });

    const fCat = document.getElementById('f-cat');
    const fOpt0 = fCat.querySelector('option[value=""]');
    fCat.innerHTML = '';
    fCat.appendChild(fOpt0);
    allCategories.forEach(c => {
      const o = document.createElement('option');
      o.value = c.id; o.textContent = c.name;
      fCat.appendChild(o);
    });
  } catch (e) { /* categories optional */ }
}

async function loadParts() {
  try {
    allParts = await api.parts.getAll();
    renderTable();
  } catch (e) {
    showToast(t('toast.network'), 'error');
    document.getElementById('parts-tbody').innerHTML =
      `<tr><td colspan="7" class="empty-state">${t('inv.empty')}</td></tr>`;
  }
}

function renderTable() {
  const search  = document.getElementById('search-input').value.toLowerCase();
  const catId   = document.getElementById('cat-filter').value;
  const status  = document.getElementById('status-filter').value;

  const filtered = allParts.filter(p => {
    if (search) {
      const inName   = p.partName.toLowerCase().includes(search);
      const inNo     = p.partNo.toLowerCase().includes(search);
      const inSerial = (p.serialNo || '').toLowerCase().includes(search)
                    || (p.serialNos || []).some(s => s.toLowerCase().includes(search));
      if (!inName && !inNo && !inSerial) return false;
    }
    if (catId === '__none__' && p.categoryId != null) return false;
    if (catId && catId !== '__none__' && String(p.categoryId) !== catId) return false;
    if (status !== '' && String(p.isActive) !== status) return false;
    return true;
  });

  const total     = filtered.length;
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  if (currentPage > totalPages) currentPage = totalPages;

  const start = (currentPage - 1) * PAGE_SIZE;
  const rows  = filtered.slice(start, start + PAGE_SIZE);

  // result count
  const countEl = document.getElementById('parts-result-count');
  if (countEl) countEl.textContent = total ? `${start + 1}–${Math.min(start + PAGE_SIZE, total)} of ${total} parts` : '';

  const tbody = document.getElementById('parts-tbody');
  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="7" class="empty-state">${t('parts.empty')}</td></tr>`;
    renderPagination(0, 1);
    return;
  }

  tbody.innerHTML = rows.map(p => {
    const catName = p.category?.name ?? (allCategories.find(c => c.id === p.categoryId)?.name ?? '—');
    const stockClass = p.stockQuantity <= p.reorderPoint ? 'danger' : 'ok';
    const statusBadge = p.isActive
      ? `<span class="badge badge-green">${t('lbl.active')}</span>`
      : `<span class="badge badge-gray">${t('lbl.inactive')}</span>`;
    const actions = p.isActive
      ? `<button class="btn btn-secondary btn-xs" onclick="openModal(${p.id})">${t('btn.edit')}</button>
         <button class="btn btn-danger btn-xs" onclick="deletePart(${p.id})">${t('btn.deactivate')}</button>`
      : `<button class="btn btn-secondary btn-xs" onclick="restorePart(${p.id})">${t('btn.restore')}</button>`;
    const imgIcon = p.imagePath
      ? `<iconify-icon icon="material-symbols:image-outline" width="15" style="vertical-align:-3px;color:var(--orange)"></iconify-icon> `
      : '';
    return `<tr>
      <td><code>${p.partNo}</code></td>
      <td><a href="#" class="part-name-link" onclick="openPartDetail(${p.id});return false;">${imgIcon}<strong>${p.partName}</strong></a></td>
      <td>${catName}</td>
      <td><span class="stock-pill ${stockClass}">${p.stockQuantity} ${t('inv.units')}</span></td>
      <td>${p.minStock}</td>
      <td>${statusBadge}</td>
      <td style="white-space:nowrap;display:flex;gap:6px;">${actions}</td>
    </tr>`;
  }).join('');

  renderPagination(total, totalPages);
}

/* ── Part detail popup (catalog view) ── */
function openPartDetail(id) {
  const p = allParts.find(x => x.id === id);
  if (!p) return;
  const catName = p.category?.name ?? (allCategories.find(c => c.id === p.categoryId)?.name ?? '—');
  const dash = v => (v == null || v === '') ? '—' : v;

  const imgHtml = p.imagePath
    ? `<img src="${IMG_BASE}${p.imagePath}" alt="${p.partName}"
           style="max-width:100%;max-height:320px;object-fit:contain;border-radius:10px;border:1px solid var(--border);background:#fff"
           onerror="this.parentElement.innerHTML='<div class=&quot;pd-noimg&quot;>'+t('parts.pd.noimg')+'</div>'">`
    : `<div class="pd-noimg">${t('parts.pd.noimg')}</div>`;

  const rows = [
    ['parts.pd.partno', `<code>${p.partNo}</code>`],
    ['parts.pd.desc',   `<strong>${dash(p.partName)}</strong>`],
    ['parts.pd.main',   dash(p.mainUnit)],
    ['parts.pd.sub',    dash(catName)],
    ['parts.pd.stock',  `${p.stockQuantity}`],
    ['parts.pd.remark', dash(p.remark)],
  ].map(([k, v]) => `
    <div class="pd-row">
      <div class="pd-label">${t(k)}</div>
      <div class="pd-value">${v}</div>
    </div>`).join('');

  document.getElementById('pd-image').innerHTML = imgHtml;
  document.getElementById('pd-fields').innerHTML = rows;
  document.getElementById('pd-title').textContent = p.partNo;
  document.getElementById('part-detail-overlay').classList.remove('hidden');
}

function closePartDetail() {
  document.getElementById('part-detail-overlay').classList.add('hidden');
}

function renderPagination(total, totalPages) {
  const pg = document.getElementById('parts-pagination');
  if (!pg) return;
  if (totalPages <= 1) { pg.innerHTML = ''; return; }

  const prev = currentPage > 1;
  const next = currentPage < totalPages;

  // page buttons: show at most 7 around current
  let pages = [];
  for (let i = 1; i <= totalPages; i++) {
    if (i === 1 || i === totalPages || (i >= currentPage - 2 && i <= currentPage + 2)) pages.push(i);
    else if (pages[pages.length - 1] !== '…') pages.push('…');
  }

  pg.innerHTML = `
    <span style="color:var(--text-secondary);">Page ${currentPage} of ${totalPages}</span>
    <div style="display:flex;gap:4px;align-items:center;">
      <button class="btn btn-secondary btn-xs" onclick="goPage(${currentPage-1})" ${prev?'':'disabled'}>‹ Prev</button>
      ${pages.map(p => p === '…'
        ? `<span style="padding:0 4px;color:var(--text-secondary);">…</span>`
        : `<button class="btn btn-xs ${p===currentPage?'btn-primary':'btn-secondary'}" onclick="goPage(${p})">${p}</button>`
      ).join('')}
      <button class="btn btn-secondary btn-xs" onclick="goPage(${currentPage+1})" ${next?'':'disabled'}>Next ›</button>
    </div>`;
}

function goPage(p) {
  currentPage = p;
  renderTable();
  document.querySelector('.tbl-wrap')?.scrollTo(0, 0);
}

function openModal(id = null) {
  editingId = id;
  const overlay = document.getElementById('modal-overlay');
  const title   = document.getElementById('modal-title');

  if (id) {
    const p = allParts.find(x => x.id === id);
    if (!p) return;
    title.textContent = t('parts.edit');
    document.getElementById('f-partno').value   = p.partNo;
    document.getElementById('f-partname').value = p.partName;
    document.getElementById('f-ordernum').value = p.orderNumber ?? '';
    document.getElementById('f-stock').value    = p.stockQuantity;
    document.getElementById('f-cat').value      = p.categoryId ?? '';
    document.getElementById('f-min').value      = p.minStock;
    document.getElementById('f-max').value      = p.maxStock;
    document.getElementById('f-reorder').value  = p.reorderPoint;
    document.getElementById('f-cat-ref').value  = p.catalogueRef ?? '';
  } else {
    title.textContent = t('parts.add');
    document.getElementById('part-form').reset();
    document.getElementById('f-stock').value   = '0';
    document.getElementById('f-min').value     = '1';
    document.getElementById('f-max').value     = '100';
    document.getElementById('f-reorder').value = '3';
  }

  overlay.classList.remove('hidden');
}

function closeModal() {
  document.getElementById('modal-overlay').classList.add('hidden');
  editingId = null;
}

async function savePart(e) {
  e.preventDefault();
  const dto = {
    partNo:       document.getElementById('f-partno').value.trim(),
    partName:     document.getElementById('f-partname').value.trim(),
    orderNumber:  document.getElementById('f-ordernum').value.trim(),
    unit:         'pcs',
    stockQuantity:parseInt(document.getElementById('f-stock').value) || 0,
    categoryId:   document.getElementById('f-cat').value ? parseInt(document.getElementById('f-cat').value) : null,
    minStock:     parseInt(document.getElementById('f-min').value) || 1,
    maxStock:     parseInt(document.getElementById('f-max').value) || 100,
    reorderPoint: parseInt(document.getElementById('f-reorder').value) || 3,
    costPerUnit:  null,
    catalogueRef: document.getElementById('f-cat-ref').value.trim() || null,
  };

  try {
    if (editingId) {
      await api.parts.update(editingId, dto);
    } else {
      await api.parts.create(dto);
    }
    showToast(t('toast.saved'), 'success');
    closeModal();
    await loadParts();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

async function deletePart(id) {
  if (!confirm(t('parts.del.confirm'))) return;
  try {
    await api.parts.remove(id);
    showToast(t('toast.deleted'), 'success');
    await loadParts();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

async function restorePart(id) {
  if (!confirm(t('parts.rest.confirm'))) return;
  try {
    await api.parts.restore(id);
    showToast(t('toast.restored'), 'success');
    await loadParts();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

function onLangChange() {
  applyLang();
  renderTable();
}

init();
