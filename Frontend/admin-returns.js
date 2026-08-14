/* ── Returns page logic — driven by Ticket (phase = return), not the old ReturnRequest flow ── */
let cachedTickets = [];

initLayout();
applyLang();
function onLangChange() { renderInProgress(); renderHistory(); }

window.onload = function () {
    fetchTickets();
};

async function fetchTickets() {
    try {
        cachedTickets = await api.tickets.getAll();
        renderInProgress();
        renderHistory();
    } catch {
        document.getElementById('onhand-tbody').innerHTML =
            `<tr><td colspan="6" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
        document.getElementById('history-tbody').innerHTML =
            `<tr><td colspan="5" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

const CONDITION_LABEL = { Good: 'ของดี', Defective: 'ของเสีย', Lost: 'สูญหาย' };
const CONDITION_COLOR = { Good: 'var(--green)', Defective: 'var(--orange)', Lost: 'var(--red)' };
function returnLinesHtml(tk) {
    const lines = tk.lines.filter(l => l.lineType === 'Return');
    return lines.length
        ? lines.map(l => `${l.partName} <span style="color:var(--text-muted)">×${l.quantity}</span>${l.condition ? ` <span style="color:${CONDITION_COLOR[l.condition]};font-size:11px;font-weight:600;">${CONDITION_LABEL[l.condition]}</span>` : ''}`).join('<br>')
        : `<span style="color:var(--text-muted)">—</span>`;
}

function renderInProgress() {
    const rows = cachedTickets.filter(tk => tk.phase === 'return' && (tk.status === 'รอ' || tk.status === 'เดินทาง'));
    document.getElementById('onhand-count').textContent =
        rows.length + ' ' + (getLang() === 'th' ? 'รายการ' : 'items');
    const tbody = document.getElementById('onhand-tbody');
    if (!rows.length) {
        tbody.innerHTML = `<tr><td colspan="6" class="empty-state">🎉 ${t('ret.onhand.empty')}</td></tr>`;
        return;
    }
    tbody.innerHTML = rows.map(tk => {
        const badge = tk.status === 'เดินทาง'
            ? `<span class="badge badge-approved"><span class="badge-dot"></span>เดินทาง</span>`
            : `<span class="badge badge-pending"><span class="badge-dot"></span>รอจัดส่ง</span>`;
        const action = tk.status === 'เดินทาง'
            ? `<button class="btn-confirm" onclick="confirmReturn(${tk.ticketId})">
                 <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                 ยืนยันรับคืน
               </button>`
            : `<span style="color:var(--text-muted);font-size:12px;">รอช่างจัดส่ง</span>`;
        return `
            <tr>
                <td><span class="id-chip ticket-id">${tk.externalTicketNo}</span></td>
                <td class="fw-600">${tk.techName}</td>
                <td>${returnLinesHtml(tk)}</td>
                <td>${tk.returnAddress || '—'}</td>
                <td>${badge}</td>
                <td>${action}</td>
            </tr>`;
    }).join('');
}

function renderHistory() {
    const rows = cachedTickets.filter(tk => tk.status === 'คืน');
    const tbody = document.getElementById('history-tbody');
    if (!rows.length) {
        tbody.innerHTML = `<tr><td colspan="5" class="empty-state">${t('ret.history.empty')}</td></tr>`;
        return;
    }
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    tbody.innerHTML = rows.map(tk => `
        <tr>
            <td><span class="id-chip">${tk.externalTicketNo}</span></td>
            <td>${tk.techName || '—'}</td>
            <td>${returnLinesHtml(tk)}</td>
            <td>${tk.returnAddress || '—'}</td>
            <td>${new Date(tk.updatedAt).toLocaleString(locale)}</td>
        </tr>`).join('');
}

async function confirmReturn(id) {
    if (!confirm('ยืนยันว่าของถึง DHL แล้ว? สถานะจะเปลี่ยนเป็น "คืน" และตัดสต็อกเข้าคลัง')) return;
    try {
        await api.tickets.confirmReturn(id);
        await fetchTickets();
        showToast?.('ยืนยันรับคืนแล้ว', 'success');
    } catch (e) {
        showToast?.(e.message || t('toast.error'), 'error');
    }
}
