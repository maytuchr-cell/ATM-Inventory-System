/* ── Returns page logic ── */
let onHandList = [], historyList = [];

initLayout();
applyLang();
function onLangChange() { renderOnHand(); renderHistory(); }

window.onload = function () {
    fetchOnHand();
    fetchHistory();
};

async function fetchOnHand() {
    try {
        onHandList = await api.returns.onHand();
        renderOnHand();
    } catch {
        document.getElementById('onhand-tbody').innerHTML =
            `<tr><td colspan="5" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

function renderOnHand() {
    document.getElementById('onhand-count').textContent =
        onHandList.length + ' ' + (getLang() === 'th' ? 'รายการ' : 'items');
    const tbody = document.getElementById('onhand-tbody');
    if (!onHandList.length) {
        tbody.innerHTML = `<tr><td colspan="5" class="empty-state">🎉 ${t('ret.onhand.empty')}</td></tr>`;
        return;
    }
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    tbody.innerHTML = onHandList.map(o => {
        const due = o.dueDate ? new Date(o.dueDate).toLocaleDateString(locale) : '—';
        const statusBadge = o.isOverdue
            ? `<span class="badge badge-danger"><span class="badge-dot"></span>${t('status.overdue')}</span>`
            : `<span class="badge badge-approved"><span class="badge-dot"></span>${t('status.received')}</span>`;
        return `
            <tr class="${o.isOverdue ? 'row-overdue' : ''}">
                <td><span class="id-chip ticket-id">TK-${o.ticketId.toString().padStart(4,'0')}</span></td>
                <td class="fw-600">${o.techName}</td>
                <td>${o.partName || '—'}</td>
                <td>${due}</td>
                <td>${statusBadge}</td>
            </tr>`;
    }).join('');
}

async function fetchHistory() {
    try {
        historyList = await api.returns.getAll();
        renderHistory();
    } catch {
        document.getElementById('history-tbody').innerHTML =
            `<tr><td colspan="7" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

function renderHistory() {
    const tbody = document.getElementById('history-tbody');
    if (!historyList.length) {
        tbody.innerHTML = `<tr><td colspan="7" class="empty-state">${t('ret.history.empty')}</td></tr>`;
        return;
    }
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    tbody.innerHTML = historyList.map(r => `
        <tr>
            <td><span class="id-chip">${r.ticketRef}</span></td>
            <td>${r.techName || '—'}</td>
            <td class="fw-600">${r.partName}</td>
            <td>${r.condition === 'Defective'
                ? `<span class="badge badge-danger"><span class="badge-dot"></span>${t('gr.condition.defective')}</span>`
                : `<span class="badge badge-success"><span class="badge-dot"></span>${t('gr.condition.good')}</span>`}</td>
            <td>${r.sourceType}</td>
            <td>${r.locationTo}</td>
            <td>${new Date(r.createdAt).toLocaleString(locale)}</td>
        </tr>`).join('');
}
