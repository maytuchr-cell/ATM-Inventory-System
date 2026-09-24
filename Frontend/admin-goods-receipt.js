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

function escapeHtmlAttr(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
}
function partLabel(p) { return `${p.partName} (${p.partNo})`; }

// Resolve whatever text currently sits in a .line-part input back to a real PartNo — used as a
// fallback for someone who typed an exact PartNo/PartName without opening the dropdown (picking
// a suggestion already stamps data-partno directly, see selectPartOption).
function resolvePartNo(text) {
    text = (text || '').trim();
    if (!text) return null;
    if (allParts.some(p => p.partNo === text)) return text;
    const byLabel = allParts.find(p => partLabel(p) === text);
    if (byLabel) return byLabel.partNo;
    const byName = allParts.find(p => p.partName === text);
    return byName ? byName.partNo : null;
}

// ── Part search dropdown (event-delegated so it works for every .line-row, including ones
// added after page load) — native <input list=datalist> looked right but Chrome shows the
// *entire* list instead of filtering once the field already has a value, which is exactly the
// unusable state an admin lands in after picking a part and re-opening the field. ──
const PART_RESULT_LIMIT = 40;

function renderPartDropdown(input) {
    const dropdown = input.nextElementSibling;
    const q = input.value.trim().toLowerCase();
    const matches = (q
        ? allParts.filter(p => p.partNo.toLowerCase().includes(q) || p.partName.toLowerCase().includes(q))
        : allParts
    ).slice(0, PART_RESULT_LIMIT);

    dropdown.innerHTML = matches.length
        ? matches.map(p => `
            <div class="part-option" data-partno="${escapeHtmlAttr(p.partNo)}" data-label="${escapeHtmlAttr(partLabel(p))}">
                ${p.partName} <span class="pn">(${p.partNo})</span>
            </div>`).join('')
        : `<div class="part-option-empty">ไม่พบอะไหล่ที่ตรงกับ "${input.value.trim()}"</div>`;
    dropdown.hidden = false;
}

function selectPartOption(input, opt) {
    input.value = opt.dataset.label;
    input.dataset.partno = opt.dataset.partno;
    input.nextElementSibling.hidden = true;
}

document.addEventListener('input', e => {
    if (!e.target.matches('.line-part')) return;
    e.target.dataset.partno = ''; // typing again invalidates any previously picked part
    renderPartDropdown(e.target);
});
document.addEventListener('focusin', e => {
    if (e.target.matches('.line-part')) renderPartDropdown(e.target);
});
document.addEventListener('mousedown', e => {
    const opt = e.target.closest('.part-option');
    if (opt) {
        e.preventDefault(); // keep focus so the subsequent focusout on the input doesn't race the click
        selectPartOption(opt.closest('.part-dropdown').previousElementSibling, opt);
    }
});
document.addEventListener('focusout', e => {
    if (!e.target.matches('.line-part')) return;
    const input = e.target;
    setTimeout(() => { // let a mousedown-selected option register first
        input.nextElementSibling.hidden = true;
        if (!input.dataset.partno) {
            const resolved = resolvePartNo(input.value);
            if (resolved) { input.dataset.partno = resolved; input.value = partLabel(allParts.find(p => p.partNo === resolved)); }
        }
    }, 150);
});
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

function onCsvSourceChange() {
    const source = document.getElementById('csv-source').value;
    const group  = document.getElementById('csv-vendor-group');
    const sel    = document.getElementById('csv-vendor');
    if (source === 'GRG') {
        group.style.display = 'none';
    } else {
        group.style.display = '';
        sel.innerHTML = allVendors
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
        <div class="part-picker">
            <input class="form-input line-part" autocomplete="off" placeholder="พิมพ์ค้นหารหัสหรือชื่ออะไหล่...">
            <div class="part-dropdown" hidden></div>
        </div>
        <input class="form-input line-qty" type="number" min="1" value="1" aria-label="จำนวน">
        <select class="form-select line-condition" aria-label="สภาพ">
            <option value="Good">🟢 ของดี</option>
            <option value="Bad">🔴 ของเสีย</option>
        </select>
        <input class="form-input line-sn" placeholder="ถ้ามี" aria-label="Serial No.">
        <input class="form-input line-remarks" placeholder="ถ้ามี" aria-label="หมายเหตุ">
        <button type="button" class="btn-remove-line" title="ลบรายการ" onclick="removeLine(${lineCount})">✕</button>
    `;
    wrap.appendChild(row);
    updateLineSummary();
}

function removeLine(id) {
    const row = document.getElementById(`line-${id}`);
    if (row && document.querySelectorAll('.line-row').length > 1) row.remove();
    else showToast(t('gr.err.minline'), 'error');
    updateLineSummary();
}

function updateLineSummary() {
    const rows = document.querySelectorAll('.line-row');
    const qty = [...rows].reduce((s, r) => s + (parseInt(r.querySelector('.line-qty').value, 10) || 0), 0);
    document.getElementById('gr-line-summary').innerHTML = `รวม <b>${rows.length}</b> รายการ · <b>${qty}</b> ชิ้น`;
}
document.addEventListener('input', e => { if (e.target.matches('.line-qty')) updateLineSummary(); });

function switchGrTab(tab) {
    ['manual', 'import'].forEach(k => {
        document.getElementById('gr-tab-' + k).classList.toggle('active', k === tab);
        document.getElementById('gr-panel-' + k).style.display = k === tab ? '' : 'none';
    });
}

function onGrDrop(e) {
    e.preventDefault();
    document.getElementById('gr-drop').classList.remove('drag');
    const file = e.dataTransfer.files[0];
    if (!file) return;
    const input = document.getElementById('csv-file');
    const dt = new DataTransfer();
    dt.items.add(file);
    input.files = dt.files;
    onFileChange(input);
}

function setDropFileName(name) {
    const drop = document.getElementById('gr-drop');
    drop.classList.toggle('has-file', !!name);
    document.getElementById('gr-drop-title').textContent = name
        ? `✓ ${name} — คลิกเพื่อเปลี่ยนไฟล์`
        : 'คลิกเพื่อเลือกไฟล์ หรือลากไฟล์มาวางที่นี่';
}

function renderLines() { /* re-apply translations on lang switch, lines keep their values */ }

async function submitReceipt(event) {
    event.preventDefault();

    const rows = Array.from(document.querySelectorAll('.line-row'));
    const unresolved = [];
    const lines = rows.map((row, i) => {
        const partInput = row.querySelector('.line-part');
        const partNo = partInput.dataset.partno || resolvePartNo(partInput.value);
        if (!partNo && partInput.value.trim()) unresolved.push(i + 1);
        return {
            partNo,
            qty:       parseInt(row.querySelector('.line-qty').value, 10) || 0,
            condition: row.querySelector('.line-condition').value,
            serialNo:  row.querySelector('.line-sn').value || null,
            isManualAdjust: false,
            remarks:   row.querySelector('.line-remarks').value || null,
        };
    });

    if (lines.some(l => !l.partNo)) {
        showToast(unresolved.length
            ? `แถวที่ ${unresolved.join(', ')}: กรุณาเลือกอะไหล่จากรายการค้นหา ไม่ใช่พิมพ์เอง`
            : 'กรุณาเลือกอะไหล่ทุกแถว', 'error');
        return;
    }
    if (lines.some(l => l.qty <= 0)) {
        showToast(t('gr.err.qty'), 'error');
        return;
    }

    const receivedByInput = document.getElementById('f-receivedby');
    const receivedBy = receivedByInput.value.trim();
    if (!receivedBy) {
        showToast('กรุณากรอกชื่อผู้รับเข้า', 'error');
        receivedByInput.focus();
        return;
    }

    const source = document.getElementById('f-source').value;
    const dto = {
        source,
        vendorId: source === 'LocalVendor' ? parseInt(document.getElementById('f-vendor').value, 10) : null,
        refDocument: document.getElementById('f-refdoc').value || null,
        locationId: parseInt(document.getElementById('f-location').value, 10),
        receivedBy,
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
    setDropFileName(file.name);
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
    if (s === 'defective' || s === 'bad' || s === 'ng') return 'Bad';
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
            <td><span class="badge ${l.condition === 'Good' ? 'badge-green' : 'badge-red'}">${l.condition === 'Good' ? 'ของดี' : 'ของเสีย'}</span></td>
        </tr>`).join('');

    document.getElementById('xl-preview-count').textContent = `ตรวจสอบรายการ — พบ ${lines.length} รายการ`;
    document.getElementById('xl-preview-wrap').style.display = '';
    document.getElementById('btn-import').disabled = lines.length === 0;
}

function clearPreview() {
    _parsedLines = [];
    document.getElementById('xl-preview-wrap').style.display = 'none';
    document.getElementById('btn-import').disabled = true;
    document.getElementById('csv-file').value = '';
    document.getElementById('csv-result').innerHTML = '';
    setDropFileName(null);
}

async function importFile() {
    if (!_parsedLines.length) { showToast('ไม่มีข้อมูล กรุณาเลือกไฟล์ก่อน', 'error'); return; }
    const locationId = parseInt(document.getElementById('csv-location').value, 10);
    const source     = document.getElementById('csv-source').value;
    const receivedBy = document.getElementById('csv-receivedby').value.trim();
    const resultDiv  = document.getElementById('csv-result');

    if (!locationId) { showToast('กรุณาเลือกคลังปลายทาง', 'error'); return; }
    const vendorId = source === 'LocalVendor' ? parseInt(document.getElementById('csv-vendor').value, 10) : null;
    if (source === 'LocalVendor' && !vendorId) { showToast('กรุณาเลือกผู้จำหน่าย', 'error'); return; }
    if (!receivedBy) {
        showToast('กรุณากรอกชื่อผู้รับเข้า', 'error');
        document.getElementById('csv-receivedby').focus();
        return;
    }

    resultDiv.innerHTML = '<span style="color:var(--text-secondary)">กำลัง import…</span>';
    document.getElementById('btn-import').disabled = true;

    const dto = {
        source,
        locationId,
        receivedBy,
        vendorId,
        refDocument: null,
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
    document.getElementById('gr-history-sub').textContent = `ทั้งหมด ${allReceipts.length} ใบ`;
    tbody.innerHTML = allReceipts.map(g => {
        const sourceLabel = g.source === 'GRG'
            ? '<span class="gr-src grg">GRG</span>'
            : `<span class="gr-src local">${g.vendorName || 'ผู้จำหน่ายในประเทศ'}</span>`;
        // Group lines by partName+condition, sum qty
        const grouped = {};
        for (const l of g.lines) {
            const key = (l.partName || l.partNo) + '|' + l.condition;
            if (!grouped[key]) grouped[key] = { name: l.partName || l.partNo, qty: 0, bad: l.condition === 'Bad' };
            grouped[key].qty += l.qty;
        }
        const partsSummary = Object.values(grouped)
            .map((x, i, arr) => `<div style="padding:4px 0;${i < arr.length-1 ? 'border-bottom:1px solid var(--border);' : ''}">${x.name} ×${x.qty}${x.bad ? ' <span class="badge badge-red" style="font-size:10.5px;">ของเสีย</span>' : ''}</div>`)
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
