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

const CONDITION_LABEL = { Good: 'ของดี', Bad: 'ของเสีย', Lost: 'สูญหาย' };
const CONDITION_COLOR = { Good: 'var(--green)', Bad: 'var(--orange)', Lost: 'var(--red)' };
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
        tbody.innerHTML = `<tr><td colspan="7" class="empty-state">🎉 ${t('ret.onhand.empty')}</td></tr>`;
        return;
    }
    tbody.innerHTML = rows.map(tk => {
        // Admin can only confirm once the tech has shipped it (เดินทาง = "รอถึง DHL") — a request
        // still sitting at รอ hasn't left the tech's hands yet, so there's nothing to confirm.
        const badge = tk.status === 'เดินทาง'
            ? `<span class="badge badge-approved"><span class="badge-dot"></span>รอถึง DHL</span>`
            : `<span class="badge badge-pending"><span class="badge-dot"></span>รอช่างจัดส่ง</span>`;
        const action = tk.status === 'เดินทาง'
            ? `<button class="btn-confirm" onclick="confirmReturn(${tk.ticketId})">
                 <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                 คืนสำเร็จ
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
                <td><button class="btn-detail" onclick="showDetail(${tk.ticketId})">🔍 ดูรายละเอียด</button></td>
            </tr>`;
    }).join('');
}

function renderHistory() {
    const rows = cachedTickets.filter(tk => tk.status === 'คืน');
    const tbody = document.getElementById('history-tbody');
    if (!rows.length) {
        tbody.innerHTML = `<tr><td colspan="6" class="empty-state">${t('ret.history.empty')}</td></tr>`;
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
            <td><button class="btn-detail" onclick="showDetail(${tk.ticketId})">🔍 ดูรายละเอียด</button></td>
        </tr>`).join('');
}

async function confirmReturn(id) {
    if (!confirm('ยืนยันว่าได้รับอีเมลแจ้งจาก DHL แล้วว่าของถึงคลัง? สถานะจะเปลี่ยนเป็น "คืน" และตัดสต็อกเข้าคลัง')) return;
    try {
        await api.tickets.confirmReturn(id);
        await fetchTickets();
        showToast?.('บันทึกคืนสำเร็จแล้ว', 'success');
    } catch (e) {
        showToast?.(e.message || t('toast.error'), 'error');
    }
}

/* ── Return Detail Modal ── */
const IMG_BASE = API_BASE.replace(/\/api$/, '');

function attachmentGalleryHtml(list) {
    if (!list || !list.length) return `<span class="detail-empty">—</span>`;
    return `<div style="display:flex;flex-wrap:wrap;gap:8px;">${list.map(a => `
        <img src="${IMG_BASE}${a.filePath}" alt="${a.fileName}"
             style="width:72px;height:72px;object-fit:cover;border-radius:8px;border:1px solid var(--border);cursor:pointer;"
             onclick="window.open('${IMG_BASE}${a.filePath}','_blank')">`).join('')}</div>`;
}

function showDetail(id) {
    const tk = cachedTickets.find(t => t.ticketId === id);
    if (!tk) return;

    const none = `<span class="detail-empty">Not specified</span>`;
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    const fmt = d => d ? new Date(d).toLocaleString(locale) : '—';
    const returnLines = tk.lines.filter(l => l.lineType === 'Return');
    const lineListHtml = list => list.length
        ? list.map(l => `<div style="display:flex;justify-content:space-between;padding:4px 0;border-bottom:1px solid var(--border);"><span>${l.partName}${l.condition ? ` <span style="color:${CONDITION_COLOR[l.condition]};font-size:11px;font-weight:600;">(${CONDITION_LABEL[l.condition]})</span>` : ''}</span><span class="fw-600">×${l.quantity}</span></div>`).join('')
        : `<span class="detail-empty">—</span>`;

    const statusLabel = tk.status === 'เดินทาง' ? 'รอถึง DHL' : tk.status === 'รอ' ? 'รอช่างจัดส่ง' : tk.status;

    document.getElementById('detailTitle').textContent = `${tk.externalTicketNo} — ${tk.techName}`;
    document.getElementById('detailBody').innerHTML = `
        <div class="detail-section">
            <div class="detail-section-title">Technician</div>
            <div class="detail-grid">
                <div class="detail-field"><label>Name</label><span>${tk.techName || none}</span></div>
                <div class="detail-field"><label>Department</label><span>${tk.techDept || none}</span></div>
                <div class="detail-field"><label>Status</label><span>${statusLabel}</span></div>
                <div class="detail-field"><label>Updated</label><span>${fmt(tk.updatedAt)}</span></div>
            </div>
        </div>

        <hr class="detail-divider">

        <div class="detail-section">
            <div class="detail-section-title">รายการอะไหล่ — คืน</div>
            ${lineListHtml(returnLines)}
        </div>

        <hr class="detail-divider">

        <div class="detail-section">
            <div class="detail-section-title">รูปภาพแนบ — คืน</div>
            ${attachmentGalleryHtml((tk.attachments || []).filter(a => a.phase === 'Return'))}
        </div>

        <hr class="detail-divider">

        <div class="detail-section">
            <div class="detail-section-title">ที่อยู่จัดส่งคืน</div>
            <div class="detail-field"><span>${tk.returnAddress || none}</span></div>
        </div>
    `;

    document.getElementById('detailOverlay').classList.add('open');
}

function closeDetailModal() {
    document.getElementById('detailOverlay').classList.remove('open');
}
function closeDetail(e) {
    if (e.target === document.getElementById('detailOverlay')) closeDetailModal();
}
document.addEventListener('keydown', e => {
    if (e.key === 'Escape') closeDetailModal();
});
