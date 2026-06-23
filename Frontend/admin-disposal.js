/* ── Disposal page logic ── */
let allParts = [], allLocations = [], allRequests = [];

initLayout();
applyLang();
function onLangChange() { renderQueue(); }

window.onload = async function () {
    await Promise.all([loadParts(), loadLocations()]);
    fetchQueue();
};

async function loadParts() {
    allParts = await api.parts.getAll({ isActive: true });
    document.getElementById('f-part').innerHTML =
        allParts.map(p => `<option value="${p.partNo}">${p.partName} (${p.partNo})</option>`).join('');
}

async function loadLocations() {
    allLocations = await api.locations.getAll({ isActive: true });
    document.getElementById('f-location').innerHTML =
        allLocations.map(l => `<option value="${l.id}">${l.name} (${l.code})</option>`).join('');
}

async function runScan() {
    try {
        const result = await api.disposal.scan();
        const box = document.getElementById('scan-result');
        box.textContent = `✅ ${result.flaggedCount} ${t('disp.scan.result')}${result.movedCount} ${t('disp.col.qty').toLowerCase()} → Scrap`;
        box.classList.add('show');
        fetchQueue();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}

async function submitRequest(event) {
    event.preventDefault();
    const dto = {
        partNo: document.getElementById('f-part').value,
        serialNo: document.getElementById('f-serial').value || null,
        locationId: parseInt(document.getElementById('f-location').value, 10),
        qty: parseInt(document.getElementById('f-qty').value, 10),
        reasonCode: document.getElementById('f-reason').value,
        requestedBy: document.getElementById('f-requestedby').value || null,
    };
    try {
        await api.disposal.create(dto);
        showToast(t('toast.saved'), 'success');
        document.getElementById('disp-form').reset();
        fetchQueue();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}

async function fetchQueue() {
    try {
        allRequests = await api.disposal.getAll();
        renderQueue();
    } catch {
        document.getElementById('disp-tbody').innerHTML =
            `<tr><td colspan="8" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

function statusBadge(status) {
    const map = {
        Pending:  ['badge-yellow', 'disp.status.pending'],
        Approved: ['badge-orange', 'disp.status.approved'],
        Disposed: ['badge-red',    'disp.status.disposed'],
    };
    const [cls, key] = map[status] || ['badge-gray', status];
    return `<span class="badge ${cls}">${t(key)}</span>`;
}

function renderQueue() {
    const tbody = document.getElementById('disp-tbody');
    if (!allRequests.length) {
        tbody.innerHTML = `<tr><td colspan="8" class="empty-state">${t('disp.empty')}</td></tr>`;
        return;
    }
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    tbody.innerHTML = allRequests.map(d => {
        let action = '<span style="color:var(--text-secondary)">—</span>';
        if (d.status === 'Pending') {
            action = `<button class="btn btn-primary" onclick="doAction(${d.id},'approve')">${t('disp.btn.approve')}</button>`;
        } else if (d.status === 'Approved') {
            action = `<button class="btn btn-primary" onclick="doAction(${d.id},'dispose')">${t('disp.btn.dispose')}</button>`;
        }
        return `
            <tr>
                <td class="fw-600">${d.partName}</td>
                <td>${d.serialNo || '—'}</td>
                <td>${d.locationName}</td>
                <td>${d.qty}</td>
                <td>${t('disp.reason.' + d.reasonCode.toLowerCase()) !== 'disp.reason.' + d.reasonCode.toLowerCase()
                      ? t('disp.reason.' + d.reasonCode.toLowerCase()) : d.reasonCode}</td>
                <td>${statusBadge(d.status)}</td>
                <td>${new Date(d.createdAt).toLocaleString(locale)}</td>
                <td>${action}</td>
            </tr>`;
    }).join('');
}

async function doAction(id, action) {
    const userName = localStorage.getItem('userEmail') || 'admin';
    try {
        await api.disposal[action](id, userName);
        showToast(t('toast.saved'), 'success');
        fetchQueue();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}
