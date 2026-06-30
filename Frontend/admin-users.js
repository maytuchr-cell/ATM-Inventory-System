let allUsers = [];
let allRoles = ['SystemAdmin', 'Staff', 'Auditor', 'Tech'];
let editingId = null;

async function init() {
  initLayout();
  applyLang();
  // Guard: only SystemAdmin should be here (backend enforces too).
  const role = (localStorage.getItem('userRole') || '').toLowerCase();
  if (role !== 'systemadmin' && role !== 'admin') {
    document.querySelector('.page-body').innerHTML =
      `<div class="card"><div class="empty-state">${t('users.denied') || 'Access denied — SystemAdmin only.'}</div></div>`;
    document.querySelector('.topbar-controls').innerHTML = '';
    return;
  }
  try { allRoles = await api.users.roles(); } catch (e) { /* keep default */ }
  await loadUsers();
}

async function loadUsers() {
  try {
    allUsers = await api.users.getAll();
    renderTable();
  } catch (e) {
    showToast(t('toast.network'), 'error');
    document.getElementById('users-tbody').innerHTML =
      `<tr><td colspan="6" class="empty-state">${t('error.connect')}</td></tr>`;
  }
}

const roleBadge = (role) => {
  const cls = { SystemAdmin: 'badge-red', Staff: 'badge-orange', Auditor: 'badge-gray', Tech: 'badge-green' }[role] || 'badge-gray';
  return `<span class="badge ${cls}">${role}</span>`;
};

function renderTable() {
  const tbody = document.getElementById('users-tbody');
  if (!allUsers.length) {
    tbody.innerHTML = `<tr><td colspan="6" class="empty-state">${t('users.empty') || 'No users.'}</td></tr>`;
    return;
  }
  tbody.innerHTML = allUsers.map((u, i) => {
    const statusBadge = u.isActive
      ? `<span class="badge badge-green">${t('lbl.active')}</span>`
      : `<span class="badge badge-gray">${t('lbl.inactive')}</span>`;
    return `<tr>
      <td>${i + 1}</td>
      <td><strong>${u.email}</strong></td>
      <td>${u.name ?? '—'}</td>
      <td>${roleBadge(u.role)}</td>
      <td>${statusBadge}</td>
      <td style="display:flex;gap:6px;">
        <button class="btn btn-secondary btn-xs" onclick="openModal(${u.id})">${t('btn.edit')}</button>
        <button class="btn btn-secondary btn-xs" onclick="resetPw(${u.id})">${t('users.resetpw') || 'Reset PW'}</button>
      </td>
    </tr>`;
  }).join('');
}

function fillRoles(selected) {
  const sel = document.getElementById('f-role');
  sel.innerHTML = allRoles.map(r => `<option value="${r}" ${r === selected ? 'selected' : ''}>${r}</option>`).join('');
}

function openModal(id = null) {
  editingId = id;
  const title = document.getElementById('modal-title');
  const pwGroup = document.getElementById('pw-group');
  const activeGroup = document.getElementById('active-group');
  document.getElementById('user-form').reset();

  if (id) {
    const u = allUsers.find(x => x.id === id);
    if (!u) return;
    title.textContent = t('users.edit') || 'Edit User';
    document.getElementById('f-email').value = u.email;
    document.getElementById('f-email').disabled = true;   // email is the identity — not editable
    document.getElementById('f-name').value = u.name ?? '';
    fillRoles(u.role);
    document.getElementById('f-active').checked = u.isActive;
    pwGroup.classList.add('hidden');         // password changed via Reset PW, not here
    activeGroup.classList.remove('hidden');
  } else {
    title.textContent = t('users.add') || 'Add User';
    document.getElementById('f-email').disabled = false;
    fillRoles('Staff');
    pwGroup.classList.remove('hidden');
    document.getElementById('pw-hint').textContent = t('users.pwhint') || 'At least 6 characters';
    activeGroup.classList.add('hidden');
  }
  document.getElementById('modal-overlay').classList.remove('hidden');
}

function closeModal() {
  document.getElementById('modal-overlay').classList.add('hidden');
  editingId = null;
}

async function saveUser(e) {
  e.preventDefault();
  try {
    if (editingId) {
      await api.users.update(editingId, {
        name:     document.getElementById('f-name').value.trim(),
        role:     document.getElementById('f-role').value,
        isActive: document.getElementById('f-active').checked,
      });
    } else {
      await api.users.create({
        email:    document.getElementById('f-email').value.trim(),
        name:     document.getElementById('f-name').value.trim(),
        role:     document.getElementById('f-role').value,
        password: document.getElementById('f-password').value,
      });
    }
    showToast(t('toast.saved'), 'success');
    closeModal();
    await loadUsers();
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

async function resetPw(id) {
  const pw = prompt(t('users.resetpw.prompt') || 'New password (min 6 chars):');
  if (pw == null) return;
  if (pw.length < 6) { showToast(t('users.pwhint') || 'At least 6 characters', 'error'); return; }
  try {
    await api.users.resetPassword(id, pw);
    showToast(t('toast.saved'), 'success');
  } catch (err) {
    showToast(err.message || t('toast.error'), 'error');
  }
}

function onLangChange() { applyLang(); renderTable(); }

init();
