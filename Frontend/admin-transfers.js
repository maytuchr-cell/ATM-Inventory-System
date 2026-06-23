/* ── Stock Transfers page logic ── */
let allParts = [], allLocations = [], allTransfers = [];

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
    const opts = allLocations.map(l => `<option value="${l.id}">${l.name} (${l.code})</option>`).join('');
    document.getElementById('f-from').innerHTML = opts;
    document.getElementById('f-to').innerHTML   = opts;
}

async function submitTransfer(event) {
    event.preventDefault();
    const fromId = parseInt(document.getElementById('f-from').value, 10);
    const toId   = parseInt(document.getElementById('f-to').value, 10);

    if (fromId === toId) {
        showToast(t('xfer.err.sameloc'), 'error');
        return;
    }

    const dto = {
        partNo: document.getElementById('f-part').value,
        qty: parseInt(document.getElementById('f-qty').value, 10),
        condition: document.getElementById('f-condition').value,
        fromLocationId: fromId,
        toLocationId: toId,
        requestedBy: document.getElementById('f-requestedby').value || null,
    };

    try {
        await api.transfers.create(dto);
        showToast(t('toast.saved'), 'success');
        document.getElementById('xfer-form').reset();
        fetchQueue();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}

async function fetchQueue() {
    try {
        allTransfers = await api.transfers.getAll();
        renderQueue();
    } catch {
        document.getElementById('xfer-tbody').innerHTML =
            `<tr><td colspan="8" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

function statusBadge(status) {
    const map = {
        Pending:   ['badge-yellow', 'xfer.status.pending'],
        Approved:  ['badge-orange', 'xfer.status.approved'],
        InTransit: ['badge-orange', 'xfer.status.intransit'],
        Received:  ['badge-green',  'xfer.status.received'],
    };
    const [cls, key] = map[status] || ['badge-gray', status];
    return `<span class="badge ${cls}">${t(key)}</span>`;
}

function renderQueue() {
    const tbody = document.getElementById('xfer-tbody');
    if (!allTransfers.length) {
        tbody.innerHTML = `<tr><td colspan="8" class="empty-state">${t('xfer.empty')}</td></tr>`;
        return;
    }
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    tbody.innerHTML = allTransfers.map(tr => {
        let actions = '';
        if (tr.status === 'Pending') {
            actions = `<button class="btn btn-primary" onclick="doAction(${tr.id},'approve')">${t('xfer.btn.approve')}</button>`;
        } else if (tr.status === 'Approved') {
            actions = `<button class="btn btn-primary" onclick="doAction(${tr.id},'confirmSend')">${t('xfer.btn.send')}</button>`;
        } else if (tr.status === 'InTransit') {
            actions = `<button class="btn btn-primary" onclick="doAction(${tr.id},'receive')">${t('xfer.btn.receive')}</button>`;
        } else {
            actions = `<span style="color:var(--text-secondary)">—</span>`;
        }
        return `
            <tr>
                <td><span class="id-chip">XF-${tr.id.toString().padStart(4,'0')}</span></td>
                <td class="fw-600">${tr.partName}</td>
                <td>${tr.qty}</td>
                <td>${tr.fromLocation}</td>
                <td>${tr.toLocation}</td>
                <td>${statusBadge(tr.status)}</td>
                <td>${new Date(tr.createdAt).toLocaleString(locale)}</td>
                <td><div class="xfer-actions">${actions}</div></td>
            </tr>`;
    }).join('');
}

async function doAction(id, action) {
    const userName = localStorage.getItem('userEmail') || 'admin';
    try {
        await api.transfers[action](id, userName);
        showToast(t('toast.saved'), 'success');
        fetchQueue();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}
