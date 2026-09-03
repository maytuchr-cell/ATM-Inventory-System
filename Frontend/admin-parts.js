let allParts = [];
let allCategories = [];
let editingId = null;
let currentPage = 1;
const PAGE_SIZE = 50;

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
      `<tr><td colspan="9" class="empty-state">${t('inv.empty')}</td></tr>`;
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
    tbody.innerHTML = `<tr><td colspan="9" class="empty-state">${t('parts.empty')}</td></tr>`;
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
    const whQty   = p.warehouseStock ?? 0;
    const techQty = p.techStock ?? 0;
    return `<tr>
      <td><code>${p.partNo}</code></td>
      <td><a href="#" class="part-name-link" onclick="openPartDetail(${p.id});return false;">${imgIcon}<strong>${p.partName}</strong></a></td>
      <td>${catName}</td>
      <td>${whQty} ${t('inv.units')}</td>
      <td>${techQty
          ? `<a href="#" style="color:var(--orange);text-decoration:underline;text-underline-offset:2px;" onclick="openHoldersModal(${p.id});return false;">${techQty} ${t('inv.units')}</a>`
          : `<span style="color:var(--text-secondary);">—</span>`}</td>
      <td><span class="stock-pill ${stockClass}">${p.stockQuantity} ${t('inv.units')}</span></td>
      <td>${p.minStock}</td>
      <td>${statusBadge}</td>
      <td style="white-space:nowrap;display:flex;gap:6px;">${actions}</td>
    </tr>`;
  }).join('');

  renderPagination(total, totalPages);
}

/* ── Part detail popup (catalog view) ── */
let pdImages = [];
let pdIndex = 0;

function openPartDetail(id) {
  const p = allParts.find(x => x.id === id);
  if (!p) return;
  const catName = p.category?.name ?? (allCategories.find(c => c.id === p.categoryId)?.name ?? '—');
  const dash = v => (v == null || v === '') ? '—' : v;

  // Prefer the multi-photo gallery; fall back to the single legacy ImagePath.
  pdImages = (p.images && p.images.length)
    ? p.images.map(i => i.filePath)
    : (p.imagePath ? [p.imagePath] : []);
  pdIndex = 0;
  renderPdImage(p.partName);

  const rows = [
    ['parts.pd.partno', `<code>${p.partNo}</code>`],
    ['parts.pd.desc',   `<strong>${dash(p.partName)}</strong>`],
    ['parts.pd.main',   dash(p.mainUnit)],
    ['parts.pd.sub',    dash(catName)],
    ['parts.pd.stock',  `${p.stockQuantity} รวม &nbsp;(คลังกลาง ${p.warehouseStock ?? 0} / อยู่กับช่าง ${p.techStock ?? 0})`],
    ['parts.pd.remark', dash(p.remark)],
  ].map(([k, v]) => `
    <div class="pd-row">
      <div class="pd-label">${t(k)}</div>
      <div class="pd-value">${v}</div>
    </div>`).join('');

  document.getElementById('pd-fields').innerHTML = rows;
  document.getElementById('pd-title').textContent = p.partNo;
  document.getElementById('part-detail-overlay').classList.remove('hidden');
}

function renderPdImage(partName) {
  const box = document.getElementById('pd-image');
  if (!pdImages.length) {
    box.innerHTML = `<div class="pd-noimg">${t('parts.pd.noimg')}</div>`;
    return;
  }
  const showArrows = pdImages.length > 1;
  box.innerHTML = `
    <div style="position:relative;width:100%;display:flex;align-items:center;justify-content:center;">
      ${showArrows ? `<button type="button" class="pd-nav pd-nav-prev" onclick="pdPrev()" aria-label="Previous">‹</button>` : ''}
      <img src="${IMG_BASE}${pdImages[pdIndex]}" alt="${partName}"
           style="max-width:100%;max-height:320px;object-fit:contain;border-radius:10px;border:1px solid var(--border);background:#fff"
           onerror="this.parentElement.innerHTML='<div class=&quot;pd-noimg&quot;>'+t('parts.pd.noimg')+'</div>'">
      ${showArrows ? `<button type="button" class="pd-nav pd-nav-next" onclick="pdNext()" aria-label="Next">›</button>` : ''}
    </div>
    ${showArrows ? `<div class="pd-dots">${pdImages.map((_, i) =>
        `<span class="pd-dot ${i === pdIndex ? 'active' : ''}" onclick="pdGoto(${i})"></span>`).join('')}</div>` : ''}
  `;
}

function pdPrev() { pdIndex = (pdIndex - 1 + pdImages.length) % pdImages.length; renderPdImage(document.getElementById('pd-title').textContent); }
function pdNext() { pdIndex = (pdIndex + 1) % pdImages.length; renderPdImage(document.getElementById('pd-title').textContent); }
function pdGoto(i) { pdIndex = i; renderPdImage(document.getElementById('pd-title').textContent); }

function closePartDetail() {
  document.getElementById('part-detail-overlay').classList.add('hidden');
}

/* ── Photo gallery (edit mode) — upload/remove multiple photos per part ── */
function renderGallery(p) {
  const wrap = document.getElementById('gallery-thumbs');
  if (!wrap) return;
  const images = p.images || [];
  wrap.innerHTML = images.map(img => `
    <div class="gallery-thumb">
      <img src="${IMG_BASE}${img.filePath}" alt="${img.fileName}">
      <button type="button" class="gallery-thumb-remove" onclick="removePartImage(${p.id}, ${img.partImageId})" title="${t('btn.delete') || 'Remove'}">✕</button>
    </div>`).join('') || `<span style="font-size:12px;color:var(--text-muted);">${t('parts.pd.noimg')}</span>`;
}

async function uploadPartImages(files) {
  if (!editingId || !files || !files.length) return;
  for (const file of Array.from(files)) {
    try {
      await api.parts.uploadImage(editingId, file);
    } catch (e) {
      showToast?.(e.message || t('toast.error'), 'error');
    }
  }
  await loadParts();
  const p = allParts.find(x => x.id === editingId);
  if (p) renderGallery(p);
}

async function removePartImage(partId, imageId) {
  try {
    await api.parts.deleteImage(partId, imageId);
    await loadParts();
    const p = allParts.find(x => x.id === partId);
    if (p) renderGallery(p);
  } catch (e) {
    showToast?.(e.message || t('toast.error'), 'error');
  }
}

/* ── Holders modal — who currently has this part checked out ── */
async function openHoldersModal(partId) {
  const part = allParts.find(x => x.id === partId);
  document.getElementById('holders-title').textContent = `ใครถือ "${part?.partName || ''}" อยู่บ้าง`;
  const body = document.getElementById('holders-body');
  body.innerHTML = `<div style="text-align:center;color:var(--text-muted);padding:20px;font-size:13px;">กำลังโหลด...</div>`;
  document.getElementById('holders-overlay').classList.remove('hidden');
  try {
    const holders = await api.parts.holders(partId);
    if (!holders.length) {
      body.innerHTML = `<div style="text-align:center;color:var(--text-muted);padding:20px;font-size:13px;">ไม่พบข้อมูลผู้ถือครอง</div>`;
      return;
    }
    body.innerHTML = `<div class="tbl-wrap"><table>
      <thead><tr><th>ช่าง</th><th>แผนก / โซน</th><th>Ticket</th><th>สถานะ</th><th>จำนวน</th></tr></thead>
      <tbody>${holders.map(h => `
        <tr>
          <td>${h.techName || '—'}</td>
          <td>${h.techDept || '—'}</td>
          <td><code>${h.externalTicketNo}</code></td>
          <td>${h.status}</td>
          <td class="fw-600">${h.quantity}</td>
        </tr>`).join('')}
      </tbody>
    </table></div>`;
  } catch (e) {
    body.innerHTML = `<div style="text-align:center;color:var(--red);padding:20px;font-size:13px;">${e.message || t('toast.error')}</div>`;
  }
}
function closeHoldersModal() {
  document.getElementById('holders-overlay').classList.add('hidden');
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

let batchItems = [];        // parts added in the current Add session
let currentDeviceType = null;
let batchAdded = 0;         // whether any part was created (to know if we must refresh)

function setDeviceType(val) {
  currentDeviceType = (currentDeviceType === val) ? null : val;
  document.querySelectorAll('#devtype-toggle .dt-btn').forEach(b =>
    b.classList.toggle('active', b.dataset.dt === currentDeviceType));
}

function openModal(id = null) {
  editingId = id;
  const overlay = document.getElementById('modal-overlay');
  const modal   = overlay.querySelector('.modal');
  const title   = document.getElementById('modal-title');
  const submitBtn = document.getElementById('modal-submit-btn');
  const cancelBtn = document.getElementById('modal-cancel-btn');

  if (id) {
    // ── EDIT (single) ──
    modal.classList.add('edit-mode');
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
    submitBtn.textContent = t('btn.save');
    cancelBtn.textContent = t('btn.cancel');
    setStockFieldMode(false);
    renderGallery(p);
  } else {
    // ── ADD (batch) ──
    modal.classList.remove('edit-mode');
    title.textContent = t('parts.add');
    document.getElementById('part-form').reset();
    document.getElementById('f-stock').value   = '0';
    document.getElementById('f-min').value     = '1';
    document.getElementById('f-max').value     = '100';
    document.getElementById('f-reorder').value = '3';
    // batch header
    document.getElementById('b-addedby').value = localStorage.getItem('userEmail') || '';
    document.getElementById('b-lot').value = '';
    document.getElementById('b-project').value = '';
    document.getElementById('b-date').value = new Date().toISOString().slice(0, 10); // auto today
    currentDeviceType = null;
    document.querySelectorAll('#devtype-toggle .dt-btn').forEach(b => b.classList.remove('active'));
    batchItems = []; batchAdded = 0;
    renderBatchList();
    submitBtn.textContent = t('parts.batch.add');
    cancelBtn.textContent = t('parts.batch.done');
    setStockFieldMode(true);
  }

  overlay.classList.remove('hidden');
}

function renderBatchList() {
  const tbody = document.getElementById('batch-tbody');
  document.getElementById('batch-count').textContent = batchItems.length;
  if (!batchItems.length) {
    tbody.innerHTML = `<tr><td colspan="5" class="empty-state">${t('parts.batch.empty')}</td></tr>`;
    return;
  }
  tbody.innerHTML = batchItems.map((it, i) => `
    <tr>
      <td style="color:var(--text-muted)">${i + 1}</td>
      <td><code>${it.partNo}</code></td>
      <td>${it.partName}</td>
      <td>${it.deviceType ? `<span class="badge badge-orange">${it.deviceType}</span>` : '—'}</td>
      <td>${it.qty}</td>
    </tr>`).join('');
}

// Route form submit: edit → update one part; add → append to batch.
function onFormSubmit(e) {
  e.preventDefault();
  if (editingId) return updatePart();
  return addToBatch();
}

// Stock field is an editable "opening balance" only when creating a new part.
// When editing, it's read-only because on-hand stock changes flow through Goods Receipt / Issue.
function setStockFieldMode(isNew) {
  const input = document.getElementById('f-stock');
  const label = document.getElementById('f-stock-label');
  const hint  = document.getElementById('f-stock-hint');
  if (isNew) {
    input.readOnly = false;
    input.style.opacity = '';
    if (label) label.textContent = t('parts.lbl.stockinit');
    if (hint) hint.style.display = 'none';
  } else {
    input.readOnly = true;
    input.style.opacity = '0.6';
    if (label) label.textContent = t('parts.lbl.stock');
    if (hint) { hint.textContent = t('parts.hint.stockmanaged'); hint.style.display = 'block'; }
  }
}

async function closeModal() {
  document.getElementById('modal-overlay').classList.add('hidden');
  const wasBatch = batchAdded > 0;
  editingId = null;
  if (wasBatch) { batchAdded = 0; await loadParts(); }  // refresh table with newly added parts
}

// Reads the part-entry fields (used by both add-to-batch and edit).
function readPartDto() {
  return {
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
}

// ── ADD (batch): create the part with the shared batch header, append to the session list ──
async function addToBatch() {
  const dto = readPartDto();
  dto.deviceType = currentDeviceType;
  dto.addedBy    = document.getElementById('b-addedby').value.trim() || null;
  dto.lot        = document.getElementById('b-lot').value.trim() || null;
  dto.project    = document.getElementById('b-project').value.trim() || null;
  const d = document.getElementById('b-date').value;
  dto.addedDate  = d ? new Date(d).toISOString() : null;

  try {
    await api.parts.create(dto);
    batchItems.push({ partNo: dto.partNo, partName: dto.partName, deviceType: dto.deviceType, qty: dto.stockQuantity });
    batchAdded++;
    renderBatchList();
    // clear part-specific fields, keep the batch header + device type + category for the next item
    ['f-partno', 'f-partname', 'f-ordernum', 'f-cat-ref'].forEach(id => document.getElementById(id).value = '');
    document.getElementById('f-stock').value = '0';
    document.getElementById('f-partno').focus();
    showToast(t('parts.batch.toast'), 'success');
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

// ── EDIT (single) ──
async function updatePart() {
  try {
    await api.parts.update(editingId, readPartDto());
    showToast(t('toast.saved'), 'success');
    document.getElementById('modal-overlay').classList.add('hidden');
    editingId = null;
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

// ── Import Parts from Excel (preview -> confirm) ──
let partsImportFile = null;
let partsImportPreview = null;

async function handlePartsImportFile(input) {
  const file = input.files[0];
  input.value = ''; // allow re-selecting the same file next time
  if (!file) return;
  if (!file.name.toLowerCase().endsWith('.xlsx')) {
    showToast('รองรับเฉพาะไฟล์ .xlsx', 'error');
    return;
  }
  partsImportFile = file;
  document.getElementById('parts-import-filename').textContent = file.name;
  document.getElementById('parts-import-loading').style.display = 'block';
  document.getElementById('parts-import-body').style.display = 'none';
  document.getElementById('pi-confirm-btn').disabled = true;
  document.getElementById('parts-import-overlay').classList.remove('hidden');

  try {
    partsImportPreview = await api.parts.importPreview(file);
    renderPartsImportPreview(partsImportPreview);
  } catch (e) {
    showToast(e.message || 'อ่านไฟล์ไม่สำเร็จ', 'error');
    closePartsImportModal();
  }
}

function renderPartsImportPreview(p) {
  document.getElementById('parts-import-loading').style.display = 'none';
  document.getElementById('parts-import-body').style.display = 'block';
  document.getElementById('pi-new-count').textContent = p.newCount;
  document.getElementById('pi-update-count').textContent = p.updateCount;
  document.getElementById('pi-error-count').textContent = p.errorCount;
  document.getElementById('pi-total-count').textContent = p.totalRows;

  const statusColor = { New: 'var(--green)', Update: '#d97706', Error: 'var(--red)' };
  const statusLabel = { New: 'ใหม่', Update: 'ซ้ำ', Error: 'ผิดพลาด' };
  document.getElementById('pi-rows-tbody').innerHTML = p.rows.map(r => `
    <tr>
      <td style="padding:5px 8px;">${r.rowNumber}</td>
      <td style="padding:5px 8px;font-family:monospace;">${r.partNo || '—'}</td>
      <td style="padding:5px 8px;">${r.partName || '—'}</td>
      <td style="padding:5px 8px;color:${statusColor[r.status]};font-weight:600;">${statusLabel[r.status]}</td>
      <td style="padding:5px 8px;color:var(--text-secondary);">${r.note || ''}</td>
    </tr>
  `).join('');

  document.getElementById('pi-confirm-btn').disabled = (p.newCount + p.updateCount) === 0;
}

function closePartsImportModal() {
  document.getElementById('parts-import-overlay').classList.add('hidden');
  partsImportFile = null;
  partsImportPreview = null;
}

async function confirmPartsImport() {
  if (!partsImportFile) return;
  const project = document.getElementById('pi-project').value.trim();
  const overwrite = document.getElementById('pi-overwrite').checked;
  const btn = document.getElementById('pi-confirm-btn');
  btn.disabled = true;
  try {
    const result = await api.parts.importConfirm(partsImportFile, project, overwrite);
    closePartsImportModal();
    showPartsImportResult(result);
    await loadParts();
  } catch (e) {
    showToast(e.message || 'Import ไม่สำเร็จ', 'error');
    btn.disabled = false;
  }
}

function showPartsImportResult(r) {
  document.getElementById('parts-import-result-body').innerHTML = `
    <div style="font-size:13px;font-weight:700;margin-bottom:8px;">อะไหล่ (Parts)</div>
    <div class="import-stats" style="display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:16px;">
      <div style="background:var(--bg-subtle);border-radius:8px;padding:10px;text-align:center;">
        <div style="font-size:20px;font-weight:800;color:var(--green);">${r.parts.inserted}</div>
        <div style="font-size:10.5px;color:var(--text-secondary);">เพิ่มใหม่</div>
      </div>
      <div style="background:var(--bg-subtle);border-radius:8px;padding:10px;text-align:center;">
        <div style="font-size:20px;font-weight:800;color:#d97706;">${r.parts.updated}</div>
        <div style="font-size:10.5px;color:var(--text-secondary);">อัปเดต</div>
      </div>
      <div style="background:var(--bg-subtle);border-radius:8px;padding:10px;text-align:center;">
        <div style="font-size:20px;font-weight:800;color:var(--text-secondary);">${r.parts.skipped}</div>
        <div style="font-size:10.5px;color:var(--text-secondary);">ข้าม (ซ้ำ)</div>
      </div>
      <div style="background:var(--bg-subtle);border-radius:8px;padding:10px;text-align:center;">
        <div style="font-size:20px;font-weight:800;color:var(--red);">${r.parts.errorCount}</div>
        <div style="font-size:10.5px;color:var(--text-secondary);">ผิดพลาด</div>
      </div>
    </div>
    <div style="font-size:13px;font-weight:700;margin-bottom:8px;">กลุ่มอะไหล่ทดแทน (Equivalent Groups) — จาก "Same part no."</div>
    <div class="import-stats" style="display:grid;grid-template-columns:repeat(3,1fr);gap:10px;">
      <div style="background:var(--bg-subtle);border-radius:8px;padding:10px;text-align:center;">
        <div style="font-size:20px;font-weight:800;color:var(--orange);">${r.equivalents.groupsCreated}</div>
        <div style="font-size:10.5px;color:var(--text-secondary);">กลุ่มใหม่</div>
      </div>
      <div style="background:var(--bg-subtle);border-radius:8px;padding:10px;text-align:center;">
        <div style="font-size:20px;font-weight:800;color:var(--orange);">${r.equivalents.groupsMerged}</div>
        <div style="font-size:10.5px;color:var(--text-secondary);">รวมเข้ากลุ่มเดิม</div>
      </div>
      <div style="background:var(--bg-subtle);border-radius:8px;padding:10px;text-align:center;">
        <div style="font-size:20px;font-weight:800;color:var(--orange);">${r.equivalents.membersAdded}</div>
        <div style="font-size:10.5px;color:var(--text-secondary);">อะไหล่ที่เพิ่ม</div>
      </div>
    </div>
  `;
  document.getElementById('parts-import-result-overlay').classList.remove('hidden');
}

init();
