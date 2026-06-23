/* ── Goods Receipt page logic ── */
let allParts = [], allVendors = [], allLocations = [], allReceipts = [];
let lineCount = 0;

initLayout();
applyLang();
function onLangChange() { renderLines(); renderHistory(); }

window.onload = async function () {
    await Promise.all([loadParts(), loadVendors(), loadLocations()]);
    onSourceChange();
    addLine();
    fetchHistory();
};

async function loadParts() {
    allParts = await api.parts.getAll({ isActive: true });
}
async function loadVendors() {
    allVendors = await api.vendors.getAll({ isActive: true });
}
async function loadLocations() {
    allLocations = await api.locations.getAll({ isActive: true });
    const opts = allLocations.map(l => `<option value="${l.id}">${l.name} (${l.code})</option>`).join('');
    document.getElementById('f-location').innerHTML = opts;
    document.getElementById('csv-location').innerHTML = opts;
}

function onSourceChange() {
    const source = document.getElementById('f-source').value;
    const vendorGroup = document.getElementById('vendor-group');
    const vendorSel   = document.getElementById('f-vendor');
    const refLabel     = document.getElementById('refdoc-label');

    if (source === 'GRG') {
        vendorGroup.style.display = 'none';
        refLabel.textContent = t('gr.refdoc.grg');
    } else {
        vendorGroup.style.display = '';
        refLabel.textContent = t('gr.refdoc.local');
        vendorSel.innerHTML = allVendors
            .filter(v => v.vendorType === 'LOCAL')
            .map(v => `<option value="${v.id}">${v.name}</option>`).join('');
    }
}

function addLine() {
    lineCount++;
    const wrap = document.getElementById('lines-wrap');
    const row = document.createElement('div');
    row.className = 'line-row';
    row.id = `line-${lineCount}`;
    row.innerHTML = `
        <div class="form-group">
            <label class="form-label" data-i18n="gr.lbl.part">Part</label>
            <select class="form-select line-part">
                ${allParts.map(p => `<option value="${p.partNo}">${p.partName} (${p.partNo})</option>`).join('')}
            </select>
        </div>
        <div class="form-group">
            <label class="form-label" data-i18n="gr.lbl.qty">Qty</label>
            <input class="form-input line-qty" type="number" min="1" value="1">
        </div>
        <div class="form-group">
            <label class="form-label" data-i18n="gr.lbl.condition">Condition</label>
            <select class="form-select line-condition">
                <option value="Good" data-i18n="gr.condition.good">Good</option>
                <option value="Defective" data-i18n="gr.condition.defective">Defective</option>
            </select>
        </div>
        <div class="form-group">
            <label class="form-label" data-i18n="gr.lbl.sn">Serial No.</label>
            <input class="form-input line-sn">
        </div>
        <div class="form-group">
            <label class="form-label" data-i18n="gr.lbl.remarks">Remarks</label>
            <input class="form-input line-remarks" data-i18n-ph="gr.lbl.remarks.ph">
        </div>
        <button type="button" class="btn-remove-line" onclick="removeLine(${lineCount})">✕</button>
    `;
    wrap.appendChild(row);
    applyLang();
}

function removeLine(id) {
    const row = document.getElementById(`line-${id}`);
    if (row && document.querySelectorAll('.line-row').length > 1) row.remove();
    else showToast(t('gr.err.minline'), 'error');
}

function renderLines() { /* re-apply translations on lang switch, lines keep their values */ }

async function submitReceipt(event) {
    event.preventDefault();

    const lines = Array.from(document.querySelectorAll('.line-row')).map(row => ({
        partNo:    row.querySelector('.line-part').value,
        qty:       parseInt(row.querySelector('.line-qty').value, 10) || 0,
        condition: row.querySelector('.line-condition').value,
        serialNo:  row.querySelector('.line-sn').value || null,
        isManualAdjust: false,
        remarks:   row.querySelector('.line-remarks').value || null,
    }));

    if (lines.some(l => l.qty <= 0)) {
        showToast(t('gr.err.qty'), 'error');
        return;
    }

    const source = document.getElementById('f-source').value;
    const dto = {
        source,
        vendorId: source === 'LocalVendor' ? parseInt(document.getElementById('f-vendor').value, 10) : null,
        refDocument: document.getElementById('f-refdoc').value || null,
        locationId: parseInt(document.getElementById('f-location').value, 10),
        receivedBy: document.getElementById('f-receivedby').value || null,
        handlingCost: parseFloat(document.getElementById('f-handlingcost').value) || 0,
        lines
    };

    try {
        await api.goodsReceipt.create(dto);
        showToast(t('toast.saved'), 'success');
        document.getElementById('gr-form').reset();
        document.getElementById('lines-wrap').innerHTML = '';
        lineCount = 0;
        addLine();
        onSourceChange();
        await loadParts();
        fetchHistory();
    } catch (e) {
        showToast(e.message || t('toast.error'), 'error');
    }
}

async function fetchHistory() {
    try {
        allReceipts = await api.goodsReceipt.getAll();
        renderHistory();
    } catch {
        document.getElementById('history-tbody').innerHTML =
            `<tr><td colspan="6" class="empty-state" style="color:var(--red)">${t('error.connect')}</td></tr>`;
    }
}

// ── Excel / CSV import ──────────────────────────────────────────────────────
let _parsedLines = []; // rows ready to submit

function onFileChange(input) {
    _parsedLines = [];
    document.getElementById('xl-preview-wrap').style.display = 'none';
    document.getElementById('btn-import').disabled = true;
    document.getElementById('csv-result').innerHTML = '';
    if (!input.files.length) return;

    const file = input.files[0];
    const isCsv = file.name.toLowerCase().endsWith('.csv');
    const reader = new FileReader();

    reader.onload = e => {
        try {
            let rows;
            if (isCsv) {
                rows = parseCsvRows(e.target.result);
            } else {
                const wb = XLSX.read(e.target.result, { type: 'array' });
                const ws = wb.Sheets[wb.SheetNames[0]];
                rows = XLSX.utils.sheet_to_json(ws, { header: 1, defval: '' });
            }
            _parsedLines = buildLines(rows, isCsv);
            showPreview(_parsedLines);
        } catch (err) {
            document.getElementById('csv-result').innerHTML =
                `<span style="color:var(--red)">❌ อ่านไฟล์ไม่ได้: ${err.message}</span>`;
        }
    };

    if (isCsv) reader.readAsText(file, 'utf-8');
    else        reader.readAsArrayBuffer(file);
}

function parseCsvRows(text) {
    return text.split('\n').map(l => l.split(',').map(c => c.trim().replace(/^"|"$/g, '')));
}

function buildLines(rows, isCsv) {
    const lines = [];

    if (isCsv) {
        // CSV: PartNo, SerialNo, Qty, Condition — skip header row
        for (const r of rows) {
            if (!r || r.length < 2) continue;
            const partNo = String(r[0] || '').trim();
            if (!partNo || /^(partno|part.?no|part.?number)$/i.test(partNo)) continue;
            const qty = parseInt(r[2]);
            if (!qty || qty <= 0) continue;
            lines.push({
                partNo,
                serialNo:  String(r[1] || '').trim() || null,
                qty,
                condition: normalizeCondition(r[3]),
                partName:  '',
                remarks:   null,
                isManualAdjust: false,
            });
        }
        return lines;
    }

    // Excel format: No. | Part Number | D1 Part Description | Serial Number | Quantity | Status
    // Step 1: find the actual header row (contains "Part Number" in col 1)
    let dataStart = 0;
    for (let i = 0; i < rows.length; i++) {
        const r = rows[i];
        if (!r) continue;
        const col1 = String(r[1] || '').trim().toLowerCase();
        if (col1.includes('part number') || col1.includes('part no')) {
            dataStart = i + 1; // data starts on the next row
            break;
        }
    }

    // Step 2: parse data rows — skip if qty is not a valid positive number
    for (let i = dataStart; i < rows.length; i++) {
        const r = rows[i];
        if (!r || r.length < 5) continue;
        const partNo = String(r[1] || '').trim();
        if (!partNo) continue;
        const qty = parseInt(r[4]);
        if (!qty || qty <= 0 || qty > 9999) continue; // skips footer/total rows
        if (partNo.length < 5) continue;               // skips short footer labels like "GRG."
        lines.push({
            partNo,
            partName:  String(r[2] || '').trim(),
            serialNo:  String(r[3] || '').trim() || null,
            qty,
            condition: normalizeCondition(r[5]),
            remarks:   null,
            isManualAdjust: false,
        });
    }
    return lines;
}

function normalizeCondition(val) {
    const s = String(val || '').trim().toLowerCase();
    if (s === 'defective' || s === 'bad' || s === 'ng') return 'Defective';
    return 'Good';
}

function showPreview(lines) {
    const tbody = document.getElementById('xl-preview-tbody');
    tbody.innerHTML = lines.map((l, i) => `
        <tr>
            <td style="color:var(--text-muted)">${i + 1}</td>
            <td><code>${l.partNo}</code></td>
            <td style="font-size:12px;color:var(--text-secondary)">${l.partName || '—'}</td>
            <td style="font-size:12px">${l.serialNo || '—'}</td>
            <td>${l.qty}</td>
            <td><span class="badge ${l.condition === 'Good' ? 'badge-green' : 'badge-red'}">${l.condition}</span></td>
        </tr>`).join('');

    document.getElementById('xl-preview-count').textContent = `พบ ${lines.length} รายการ — ตรวจสอบแล้วกด Import`;
    document.getElementById('xl-preview-wrap').style.display = '';
    document.getElementById('btn-import').disabled = lines.length === 0;
}

function clearPreview() {
    _parsedLines = [];
    document.getElementById('xl-preview-wrap').style.display = 'none';
    document.getElementById('btn-import').disabled = true;
    document.getElementById('csv-file').value = '';
    document.getElementById('csv-result').innerHTML = '';
}

async function importFile() {
    if (!_parsedLines.length) { showToast('ไม่มีข้อมูล กรุณาเลือกไฟล์ก่อน', 'error'); return; }
    const locationId = parseInt(document.getElementById('csv-location').value, 10);
    const source     = document.getElementById('csv-source').value;
    const receivedBy = document.getElementById('csv-receivedby').value.trim();
    const resultDiv  = document.getElementById('csv-result');

    if (!locationId) { showToast('กรุณาเลือก Target Location', 'error'); return; }

    resultDiv.innerHTML = '<span style="color:var(--text-secondary)">กำลัง import…</span>';
    document.getElementById('btn-import').disabled = true;

    const dto = {
        source,
        locationId,
        receivedBy: receivedBy || null,
        vendorId: null,
        refDocument: null,
        handlingCost: 0,
        lines: _parsedLines.map(l => ({
            partNo:         l.partNo,
            qty:            l.qty,
            condition:      l.condition,
            serialNo:       l.serialNo || null,
            remarks:        l.partName || null,
            isManualAdjust: false,
        })),
    };

    try {
        const res = await api.goodsReceipt.create(dto);
        showToast(`✅ Import สำเร็จ ${_parsedLines.length} รายการ`, 'success');
        let html = `<span style="color:var(--green)">✅ Import สำเร็จ ${_parsedLines.length} รายการ — Receipt No: ${res.receiptNo || '—'}</span>`;
        if (res.autoCreatedCount > 0) {
            html += `<div style="margin-top:8px;color:var(--orange);font-size:12px;">⚠️ Auto-created ${res.autoCreatedCount} new parts: ${res.autoCreatedParts.join(', ')}</div>`;
        }
        resultDiv.innerHTML = html;
        clearPreview();
        await loadParts();
        fetchHistory();
    } catch (e) {
        resultDiv.innerHTML = `<span style="color:var(--red)">❌ ${e.message}</span>`;
        showToast(e.message, 'error');
        document.getElementById('btn-import').disabled = false;
    }
}

// legacy CSV via server (kept for backward compat)
async function importCsv() { importFile(); }

function renderHistory() {
    const tbody = document.getElementById('history-tbody');
    if (!allReceipts.length) {
        tbody.innerHTML = `<tr><td colspan="6" class="empty-state">${t('gr.empty')}</td></tr>`;
        return;
    }
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    tbody.innerHTML = allReceipts.map(g => {
        const sourceLabel = g.source === 'GRG' ? t('gr.source.grg') : (g.vendorName || t('gr.source.local'));
        // Group lines by partName+condition, sum qty
        const grouped = {};
        for (const l of g.lines) {
            const key = (l.partName || l.partNo) + '|' + l.condition;
            if (!grouped[key]) grouped[key] = { name: l.partName || l.partNo, qty: 0, defective: l.condition === 'Defective' };
            grouped[key].qty += l.qty;
        }
        const partsSummary = Object.values(grouped)
            .map((x, i, arr) => `<div style="padding:4px 0;${i < arr.length-1 ? 'border-bottom:1px solid var(--border);' : ''}">${x.name} ×${x.qty}${x.defective ? ' <span style="color:var(--red)">⚠</span>' : ''}</div>`)
            .join('');
        return `
            <tr>
                <td><span class="id-chip">${g.receiptNo}</span></td>
                <td>${sourceLabel}</td>
                <td>${g.locationName || '—'}</td>
                <td style="max-width:320px;">${partsSummary}</td>
                <td>${new Date(g.receivedAt).toLocaleString(locale)}</td>
                <td>${g.receivedBy || '—'}</td>
            </tr>`;
    }).join('');
}
