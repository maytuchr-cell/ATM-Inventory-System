/* ── Stock Counting page logic ── */
let allCounts = [];
let activeCount = null;

initLayout();
applyLang();
function onLangChange() { renderActive(); renderHistory(); }

window.onload = async function () {
    await checkFreeze();
    onTypeChange();
    await fetchCounts();
};

async function checkFreeze() {
    try {
        const settings = await api.stockCount.settings();
        document.getElementById('freeze-banner').style.display = settings.isFrozen ? 'flex' : 'none';
    } catch {}
}

function onTypeChange() {
    const isAnnual = document.getElementById('f-type').value === 'Annual';
    document.getElementById('samplesize-group').style.display = isAnnual ? 'none' : '';
}

async function startCount(event) {
    event.preventDefault();
    const dto = {
        countType: document.getElementById('f-type').value,
        period: document.getElementById('f-period').value,
        sampleSize: parseInt(document.getElementById('f-samplesize').value, 10) || 10,
        startedBy: document.getElementById('f-startedby').value || null,
    };
    try {
        await api.stockCount.start(dto);
        showToast(t('toast.saved'), 'success');
        document.getElementById('start-form').reset();
        await checkFreeze();
        await fetchCounts();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}

async function fetchCounts() {
    try {
        allCounts = await api.stockCount.getAll();
        activeCount = allCounts.find(c => c.status === 'InProgress') || null;
        document.getElementById('start-card').style.display = activeCount ? 'none' : '';
        document.getElementById('active-card').style.display = activeCount ? '' : 'none';
        renderActive();
        renderHistory();
    } catch {
        document.getElementById('history-tbody').innerHTML =
            `<tr><td colspan="4" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

function varianceClass(v) { return v > 0 ? 'variance-pos' : v < 0 ? 'variance-neg' : 'variance-zero'; }

function renderActive() {
    if (!activeCount) return;
    document.getElementById('active-tbody').innerHTML = activeCount.lines.map(l => `
        <tr>
            <td class="fw-600">${l.partName}</td>
            <td>${l.locationName}</td>
            <td>${l.systemQty}</td>
            <td><input class="qty-input" type="number" min="0" value="${l.physicalQty ?? ''}"
                onchange="submitLine(${l.id}, this.value)"></td>
            <td class="${varianceClass(l.variance)}">${l.variance > 0 ? '+' : ''}${l.variance}</td>
        </tr>`).join('');
}

async function submitLine(lineId, value) {
    const qty = parseInt(value, 10);
    if (isNaN(qty) || qty < 0) return;
    try {
        await api.stockCount.submitLine(activeCount.id, lineId, qty);
        await fetchCounts();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}

async function reconcile() {
    const remarks = document.getElementById('f-remarks').value.trim();
    if (!remarks) {
        showToast(t('sc.reconcile.err'), 'error');
        return;
    }
    try {
        await api.stockCount.reconcile(activeCount.id, remarks, localStorage.getItem('userEmail') || 'admin');
        showToast(t('toast.saved'), 'success');
        document.getElementById('f-remarks').value = '';
        await checkFreeze();
        await fetchCounts();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}

function statusBadge(status) {
    const map = {
        Draft:      ['badge-gray',   'sc.status.draft'],
        InProgress: ['badge-orange', 'sc.status.inprogress'],
        Completed:  ['badge-green',  'sc.status.completed'],
    };
    const [cls, key] = map[status] || ['badge-gray', status];
    return `<span class="badge ${cls}">${t(key)}</span>`;
}

function renderHistory() {
    const tbody = document.getElementById('history-tbody');
    if (!allCounts.length) {
        tbody.innerHTML = `<tr><td colspan="4" class="empty-state">${t('sc.empty')}</td></tr>`;
        return;
    }
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    tbody.innerHTML = allCounts.map(c => `
        <tr>
            <td>${c.countType === 'Annual' ? t('sc.type.annual') : t('sc.type.cycle')}</td>
            <td>${c.period}</td>
            <td>${statusBadge(c.status)}</td>
            <td>${new Date(c.createdAt).toLocaleString(locale)}</td>
        </tr>`).join('');
}
