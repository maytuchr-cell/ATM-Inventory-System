/* ── Equivalent Groups page logic ── */
let groups = [];
let editingGroupId = null;

initLayout();
applyLang();
function onLangChange() { renderGroups(); }

window.onload = fetchGroups;

async function fetchGroups() {
    try {
        groups = await api.equivalentGroups.getAll();
        renderGroups();
    } catch {
        document.getElementById('groups-container').innerHTML =
            `<div class="empty-state" style="color:var(--red)">${t('error.connect')}</div>`;
    }
}

function renderGroups() {
    const container = document.getElementById('groups-container');
    if (!groups.length) {
        container.innerHTML = `<div class="empty-state">${t('eg.empty')}</div>`;
        return;
    }
    container.innerHTML = groups.map(g => `
        <div class="group-card" id="card-${g.id}">
            <div class="group-header">
                <div>
                    <div class="group-name">${g.name}</div>
                    ${g.description ? `<div class="group-desc">${g.description}</div>` : ''}
                </div>
                <div class="group-actions">
                    <button class="btn btn-sm" onclick="openEditModal(${g.id})">${t('btn.edit')}</button>
                    <button class="btn btn-sm btn-danger" onclick="deleteGroup(${g.id})">${t('btn.delete')}</button>
                </div>
            </div>
            <div class="members-row">
                ${g.members.map(m => `
                    <span class="member-chip">
                        ${m.partNo}${m.partName !== m.partNo ? ` — ${m.partName}` : ''}
                        <span class="rm" onclick="removeMember(${g.id}, ${m.id})" title="Remove">✕</span>
                    </span>`).join('')}
                ${g.members.length === 0 ? `<span style="color:var(--text-secondary);font-size:12px">No members yet</span>` : ''}
            </div>
            <div class="add-member-row">
                <input class="form-input" id="part-input-${g.id}" placeholder="${t('eg.lbl.addpart')}" style="max-width:200px">
                <button class="btn btn-sm btn-primary" onclick="addMember(${g.id})">${t('eg.btn.addpart')}</button>
            </div>
        </div>`).join('');
}

function openNewGroupModal() {
    editingGroupId = null;
    document.getElementById('g-name').value = '';
    document.getElementById('g-desc').value = '';
    document.getElementById('modal-title').setAttribute('data-i18n', 'eg.modal.new');
    document.getElementById('modal-title').textContent = t('eg.modal.new');
    document.getElementById('group-modal').style.display = 'flex';
}

function openEditModal(id) {
    const g = groups.find(x => x.id === id);
    if (!g) return;
    editingGroupId = id;
    document.getElementById('g-name').value = g.name;
    document.getElementById('g-desc').value = g.description || '';
    document.getElementById('modal-title').setAttribute('data-i18n', 'eg.modal.edit');
    document.getElementById('modal-title').textContent = t('eg.modal.edit');
    document.getElementById('group-modal').style.display = 'flex';
}

function closeModal() {
    document.getElementById('group-modal').style.display = 'none';
}

async function saveGroup() {
    const name = document.getElementById('g-name').value.trim();
    const desc = document.getElementById('g-desc').value.trim();
    if (!name) { showToast('Group name is required.', 'error'); return; }

    try {
        if (editingGroupId) {
            await api.equivalentGroups.update(editingGroupId, { name, description: desc || null });
            showToast('Group updated.', 'success');
        } else {
            await api.equivalentGroups.create({ name, description: desc || null });
            showToast('Group created.', 'success');
        }
        closeModal();
        await fetchGroups();
    } catch (e) {
        showToast(e.message, 'error');
    }
}

async function deleteGroup(id) {
    if (!confirm('Delete this group?')) return;
    try {
        await api.equivalentGroups.remove(id);
        showToast('Group deleted.', 'success');
        await fetchGroups();
    } catch (e) {
        showToast(e.message, 'error');
    }
}

async function addMember(groupId) {
    const input = document.getElementById(`part-input-${groupId}`);
    const partNo = input.value.trim().toUpperCase();
    if (!partNo) return;
    try {
        await api.equivalentGroups.addMember(groupId, partNo);
        showToast(`${partNo} added.`, 'success');
        input.value = '';
        await fetchGroups();
    } catch (e) {
        showToast(e.message, 'error');
    }
}

async function handleImportFile(input) {
    const file = input.files[0];
    input.value = ''; // allow re-selecting the same file next time
    if (!file) return;
    if (!file.name.toLowerCase().endsWith('.xlsx')) {
        showToast('รองรับเฉพาะไฟล์ .xlsx', 'error');
        return;
    }
    showToast('กำลัง Import...', 'info');
    try {
        const result = await api.equivalentGroups.importExcel(file);
        showImportResult(result);
        await fetchGroups();
    } catch (e) {
        showToast(e.message || 'Import failed.', 'error');
    }
}

function showImportResult(r) {
    document.getElementById('import-result-body').innerHTML = `
        <div class="import-stats">
            <div class="import-stat"><div class="import-stat-num">${r.groupsCreated}</div><div class="import-stat-label">กลุ่มใหม่</div></div>
            <div class="import-stat"><div class="import-stat-num">${r.groupsMerged}</div><div class="import-stat-label">รวมเข้ากลุ่มเดิม</div></div>
            <div class="import-stat"><div class="import-stat-num">${r.membersAdded}</div><div class="import-stat-label">อะไหล่ที่เพิ่ม</div></div>
            <div class="import-stat"><div class="import-stat-num">${r.rowsProcessed}</div><div class="import-stat-label">แถวที่ประมวลผล</div></div>
        </div>
        ${r.notFoundCount > 0 ? `
            <div style="font-size:12.5px;color:var(--text-secondary);margin-bottom:6px;">
                ⚠ ${r.notFoundCount} Part No. ในไฟล์ไม่มีในระบบ (ข้ามไป) — ตัวอย่าง:
            </div>
            <div class="import-notfound">${r.notFoundSample.join(', ')}${r.notFoundCount > r.notFoundSample.length ? ', …' : ''}</div>
        ` : `<div style="font-size:12.5px;color:var(--green);">Part No. ทุกตัวในไฟล์พบในระบบ ✓</div>`}
    `;
    document.getElementById('import-modal').style.display = 'flex';
}

async function removeMember(groupId, memberId) {
    try {
        await api.equivalentGroups.removeMember(groupId, memberId);
        showToast('Member removed.', 'success');
        await fetchGroups();
    } catch (e) {
        showToast(e.message, 'error');
    }
}
