/* ── Serial No. Tracking page logic ── */
initLayout();
applyLang();
function onLangChange() { /* static page, no re-render needed */ }

async function doSearch() {
    const sn = document.getElementById('sn-input').value.trim();
    if (!sn) { showToast('Please enter a Serial No.', 'error'); return; }

    const area = document.getElementById('result-area');
    area.innerHTML = `<div class="empty-state">${t('connecting')}</div>`;

    try {
        const data = await api.tracking.bySerial(sn);
        renderResult(data);
    } catch (e) {
        if (e.status === 404) {
            area.innerHTML = `<div class="empty-state">${t('trk.notfound')}</div>`;
        } else {
            area.innerHTML = `<div class="empty-state" style="color:var(--red)">${t('error.connect')}</div>`;
        }
    }
}

function renderResult(data) {
    const locale = getLang() === 'th' ? 'th-TH' : 'en-GB';
    const cs = data.currentStatus;
    const conditionColor = cs.condition === 'Good' ? 'var(--green)' : 'var(--red)';

    const statusHtml = `
        <div class="status-cards">
            <div class="stat-card">
                <div class="label" data-i18n="trk.current.location">${t('trk.current.location')}</div>
                <div class="value">${cs.location}</div>
            </div>
            <div class="stat-card">
                <div class="label" data-i18n="trk.current.condition">${t('trk.current.condition')}</div>
                <div class="value" style="color:${conditionColor}">${cs.condition}</div>
            </div>
            <div class="stat-card">
                <div class="label" data-i18n="trk.current.status">${t('trk.current.status')}</div>
                <div class="value">${t('trk.event.' + cs.eventType) || cs.eventType}</div>
            </div>
            <div class="stat-card">
                <div class="label" data-i18n="trk.current.asof">${t('trk.current.asof')}</div>
                <div class="value" style="font-size:13px">${new Date(cs.asOf).toLocaleString(locale)}</div>
            </div>
        </div>`;

    const timelineHtml = data.timeline.map((ev, idx) => {
        const isLatest = idx === data.timeline.length - 1;
        const dotClass = `tl-dot ${ev.condition === 'Good' ? 'good' : 'defective'} ${isLatest ? 'latest' : ''}`;
        const badgeClass = eventBadgeClass(ev.eventType);
        const ref = ev.refType ? `<span class="tl-ref">${ev.refType}${ev.refId ? ' #' + ev.refId : ''}</span>` : '';
        const user = ev.userName ? `<span>${ev.userName}</span>` : '';

        return `
            <div class="tl-item">
                <div class="${dotClass}"></div>
                <div class="tl-content">
                    <div class="tl-meta">
                        <span class="badge-event ${badgeClass}">${t('trk.event.' + ev.eventType) || ev.eventType}</span>
                        <span class="tl-time">${new Date(ev.timestamp).toLocaleString(locale)}</span>
                    </div>
                    <div class="tl-desc">${ev.description}</div>
                    <div class="tl-footer">
                        <span>📍 ${ev.location}</span>
                        <span style="color:${ev.condition==='Good'?'var(--green)':'var(--red)'}">${ev.condition}</span>
                        ${ref}${user}
                    </div>
                </div>
            </div>`;
    }).join('');

    document.getElementById('result-area').innerHTML = `
        <div class="card" style="padding:20px 24px;margin-bottom:20px">
            <div style="font-size:13px;color:var(--text-secondary);margin-bottom:8px">Serial No.</div>
            <div style="font-size:20px;font-weight:700;color:var(--orange)">${data.serialNo}</div>
        </div>
        ${statusHtml}
        <div class="card" style="padding:20px 24px">
            <div style="font-weight:700;margin-bottom:20px;font-size:14px">Timeline (oldest → newest)</div>
            <div class="timeline">${timelineHtml}</div>
        </div>`;
}

function eventBadgeClass(type) {
    const map = {
        'GR': 'gr', 'Issue': 'issue', 'Return': 'return',
        'Transfer': 'transfer', 'Disposal': 'disposal',
        'Adjustment': 'adjust', 'TicketSubmit': 'issue'
    };
    return map[type] || '';
}
