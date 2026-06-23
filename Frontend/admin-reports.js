/* ── Reports page logic ── */
let currentTab = 'audit';
let auditData = [];
let lifecycleData = [];

initLayout();
applyLang();
function onLangChange() { renderAudit(); renderLifecycle(); }

window.onload = function () {
    fetchAudit();
    fetchLifecycle();
};

function switchTab(tab) {
    currentTab = tab;
    document.getElementById('tab-audit-btn').classList.toggle('active', tab === 'audit');
    document.getElementById('tab-lifecycle-btn').classList.toggle('active', tab === 'lifecycle');
    document.getElementById('panel-audit').classList.toggle('active', tab === 'audit');
    document.getElementById('panel-lifecycle').classList.toggle('active', tab === 'lifecycle');
}

async function fetchAudit() {
    const params = {};
    const from = document.getElementById('f-from').value;
    const to   = document.getElementById('f-to').value;
    const type = document.getElementById('f-type').value;
    if (from) params.from = from;
    if (to)   params.to = to;
    if (type) params.type = type;

    try {
        auditData = await api.reports.auditChecklist(params);
        renderAudit();
    } catch {
        document.getElementById('audit-tbody').innerHTML =
            `<tr><td colspan="9" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

function renderAudit() {
    const tbody = document.getElementById('audit-tbody');
    if (!auditData.length) {
        tbody.innerHTML = `<tr><td colspan="9" class="empty-state">${t('rpt.audit.empty')}</td></tr>`;
        return;
    }
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    tbody.innerHTML = auditData.map(m => `
        <tr>
            <td class="fs-12">${new Date(m.timestamp).toLocaleString(locale)}</td>
            <td><span class="badge badge-gray">${m.movementType}</span></td>
            <td class="fw-600">${m.partName}</td>
            <td>${m.qty}</td>
            <td>${m.condition}</td>
            <td class="fs-12">${m.fromLocation || '—'}</td>
            <td class="fs-12">${m.toLocation || '—'}</td>
            <td class="fs-12">${m.refType ? `${m.refType}${m.refId ? ' #' + m.refId : ''}` : '—'}</td>
            <td class="fs-12">${m.userName || '—'}</td>
        </tr>`).join('');
}

async function fetchLifecycle() {
    try {
        lifecycleData = await api.reports.lifecycle();
        renderLifecycle();
    } catch {
        document.getElementById('lifecycle-tbody').innerHTML =
            `<tr><td colspan="10" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

function renderLifecycle() {
    const tbody = document.getElementById('lifecycle-tbody');
    if (!lifecycleData.length) {
        tbody.innerHTML = `<tr><td colspan="10" class="empty-state">—</td></tr>`;
        return;
    }
    tbody.innerHTML = lifecycleData.map(l => `
        <tr>
            <td class="fw-600">${l.partName}</td>
            <td>${l.received}</td>
            <td>${l.issued}</td>
            <td>${l.returned}</td>
            <td>${l.transferred}</td>
            <td>${l.disposed}</td>
            <td>${l.adjustments}</td>
            <td>${l.expectedTotal}</td>
            <td>${l.actualTotal}</td>
            <td class="${l.reconciled ? 'recon-yes' : 'recon-no'}">${l.reconciled ? t('rpt.lc.yes') : t('rpt.lc.no')}</td>
        </tr>`).join('');
}

function exportExcel() {
    const isAudit = currentTab === 'audit';
    const today = new Date().toISOString().slice(0, 10);

    let rows, sheetName, fileName;
    if (isAudit) {
        sheetName = 'Audit Checklist';
        fileName  = `audit-checklist-${today}.xlsx`;
        rows = [
            ['Date', 'Type', 'Part', 'Qty', 'Condition', 'From', 'To', 'Reference', 'User'],
            ...auditData.map(m => [
                new Date(m.timestamp).toLocaleString(),
                m.movementType, m.partName, m.qty, m.condition,
                m.fromLocation || '', m.toLocation || '',
                m.refType ? `${m.refType}${m.refId ? ' #' + m.refId : ''}` : '',
                m.userName || ''
            ])
        ];
    } else {
        sheetName = 'Lifecycle Summary';
        fileName  = `lifecycle-summary-${today}.xlsx`;
        rows = [
            ['Part', 'Received', 'Issued', 'Returned', 'Transferred', 'Disposed', 'Adjustments', 'Expected Total', 'Actual Total', 'Reconciled'],
            ...lifecycleData.map(l => [
                l.partName, l.received, l.issued, l.returned, l.transferred,
                l.disposed, l.adjustments, l.expectedTotal, l.actualTotal,
                l.reconciled ? 'Yes' : 'No'
            ])
        ];
    }

    const ws = XLSX.utils.aoa_to_sheet(rows);

    // bold header row
    const range = XLSX.utils.decode_range(ws['!ref']);
    for (let c = range.s.c; c <= range.e.c; c++) {
        const cell = ws[XLSX.utils.encode_cell({ r: 0, c })];
        if (cell) cell.s = { font: { bold: true } };
    }

    // auto column width
    ws['!cols'] = rows[0].map((_, ci) => ({
        wch: Math.min(40, Math.max(10, ...rows.map(r => String(r[ci] ?? '').length)))
    }));

    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, sheetName);
    XLSX.writeFile(wb, fileName);
}
