/* ── Centralized API client ──
   Uses the page's own hostname so this works both from the PC (localhost) and from a phone
   on the same network hitting the PC's LAN IP — only the port differs (backend runs on 5128).
   Falls back to localhost when hostname is unavailable (e.g. file:// or a sandboxed preview frame). */
const API_BASE = `http://${location.hostname || 'localhost'}:5128/api`;

async function apiFetch(path, options = {}) {
  const url = API_BASE + path;
  const token = localStorage.getItem('authToken');
  const res = await fetch(url, {
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { 'Authorization': 'Bearer ' + token } : {}),
      ...(options.headers || {})
    },
    ...options
  });
  if (res.status === 401) {
    // Token missing/expired — bounce to login
    localStorage.removeItem('authToken');
    if (!location.pathname.endsWith('login.html')) location.href = 'login.html';
    const err = new Error('Session expired. Please sign in again.');
    err.status = 401;
    throw err;
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    const err = new Error(body.message || `HTTP ${res.status}`);
    err.status = res.status;
    throw err;
  }
  if (res.status === 204) return null;
  return res.json();
}

// Same auth/error handling as apiFetch, but for multipart/form-data uploads — must NOT set
// Content-Type manually, or the browser can't attach its own multipart boundary.
async function apiUpload(path, formData) {
  const url = API_BASE + path;
  const token = localStorage.getItem('authToken');
  const res = await fetch(url, {
    method: 'POST',
    headers: { ...(token ? { 'Authorization': 'Bearer ' + token } : {}) },
    body: formData
  });
  if (res.status === 401) {
    localStorage.removeItem('authToken');
    if (!location.pathname.endsWith('login.html')) location.href = 'login.html';
    const err = new Error('Session expired. Please sign in again.');
    err.status = 401;
    throw err;
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    const err = new Error(body.message || `HTTP ${res.status}`);
    err.status = res.status;
    throw err;
  }
  if (res.status === 204) return null;
  return res.json();
}

const api = {
  parts: {
    getAll:  (params = {}) => apiFetch('/Parts?' + new URLSearchParams(params)),
    getById: (id)          => apiFetch(`/Parts/${id}`),
    create:  (data)        => apiFetch('/Parts',      { method: 'POST',   body: JSON.stringify(data) }),
    update:  (id, data)    => apiFetch(`/Parts/${id}`, { method: 'PUT',    body: JSON.stringify(data) }),
    remove:  (id)          => apiFetch(`/Parts/${id}`, { method: 'DELETE' }),
    restore: (id)          => apiFetch(`/Parts/${id}/restore`, { method: 'PATCH' }),
    holders: (id)          => apiFetch(`/Parts/${id}/holders`),
    uploadImage: (id, file) => { const fd = new FormData(); fd.append('file', file); return apiUpload(`/Parts/${id}/images`, fd); },
    deleteImage: (id, imageId) => apiFetch(`/Parts/${id}/images/${imageId}`, { method: 'DELETE' }),
  },
  categories: {
    getAll:  (params = {}) => apiFetch('/Categories?' + new URLSearchParams(params)),
    getById: (id)          => apiFetch(`/Categories/${id}`),
    create:  (data)        => apiFetch('/Categories',      { method: 'POST',   body: JSON.stringify(data) }),
    update:  (id, data)    => apiFetch(`/Categories/${id}`, { method: 'PUT',    body: JSON.stringify(data) }),
    remove:  (id)          => apiFetch(`/Categories/${id}`, { method: 'DELETE' }),
  },
  locations: {
    getAll:  (params = {}) => apiFetch('/Locations?' + new URLSearchParams(params)),
    getById: (id)          => apiFetch(`/Locations/${id}`),
    create:  (data)        => apiFetch('/Locations',      { method: 'POST',   body: JSON.stringify(data) }),
    update:  (id, data)    => apiFetch(`/Locations/${id}`, { method: 'PUT',    body: JSON.stringify(data) }),
    remove:  (id)          => apiFetch(`/Locations/${id}`, { method: 'DELETE' }),
  },
  vendors: {
    getAll:  (params = {}) => apiFetch('/Vendors?' + new URLSearchParams(params)),
    getById: (id)          => apiFetch(`/Vendors/${id}`),
    create:  (data)        => apiFetch('/Vendors',      { method: 'POST',   body: JSON.stringify(data) }),
    update:  (id, data)    => apiFetch(`/Vendors/${id}`, { method: 'PUT',    body: JSON.stringify(data) }),
    remove:  (id)          => apiFetch(`/Vendors/${id}`, { method: 'DELETE' }),
  },
  tickets: {
    getAll:       ()             => apiFetch('/Ticket'),
    sync:         (data)         => apiFetch('/Ticket/sync',            { method: 'POST', body: JSON.stringify(data) }),
    createAdditionalWithdraw: (data) => apiFetch('/Ticket/additional-withdraw', { method: 'POST', body: JSON.stringify(data) }),
    submitWithdraw: (id, dto)    => apiFetch(`/Ticket/${id}/withdraw`,   { method: 'PUT',  body: JSON.stringify(dto) }),
    approve:      (id)           => apiFetch(`/Ticket/${id}/approve`,   { method: 'PUT' }),
    reject:       (id, reason)   => apiFetch(`/Ticket/${id}/reject`,    { method: 'PUT',  body: JSON.stringify({ reason }) }),
    cancel:       (id)           => apiFetch(`/Ticket/${id}/cancel`,    { method: 'PUT' }),
    receive:      (id)           => apiFetch(`/Ticket/${id}/receive`,   { method: 'PUT' }),
    submitReturn: (id, dto)      => apiFetch(`/Ticket/${id}/return`,    { method: 'PUT',  body: JSON.stringify(dto) }),
    approveReturn:(id)           => apiFetch(`/Ticket/${id}/approve-return`, { method: 'PUT' }),
    ship:         (id)           => apiFetch(`/Ticket/${id}/ship`,      { method: 'PUT' }),
    confirmReturn:(id)           => apiFetch(`/Ticket/${id}/confirm-return`, { method: 'PUT' }),
    substitutePart:(id, lineId, partNo) => apiFetch(`/Ticket/${id}/lines/${lineId}/substitute`, { method: 'PUT', body: JSON.stringify({ partNo }) }),
    uploadAttachment: (file) => { const fd = new FormData(); fd.append('file', file); return apiUpload('/Ticket/upload', fd); },
    remove: (id) => apiFetch(`/Ticket/${id}`, { method: 'DELETE' }),
  },
  goodsReceipt: {
    getAll:  (params = {}) => apiFetch('/GoodsReceipt?' + new URLSearchParams(params)),
    create:  (data)        => apiFetch('/GoodsReceipt', { method: 'POST', body: JSON.stringify(data) }),
  },
  returns: {
    getAll:   ()     => apiFetch('/Return'),
    onHand:   ()     => apiFetch('/Return/on-hand'),
    create:   (data) => apiFetch('/Return', { method: 'POST', body: JSON.stringify(data) }),
  },
  transfers: {
    getAll:      (params = {}) => apiFetch('/StockTransfer?' + new URLSearchParams(params)),
    create:      (data)        => apiFetch('/StockTransfer', { method: 'POST', body: JSON.stringify(data) }),
    approve:     (id, userName) => apiFetch(`/StockTransfer/${id}/approve`,      { method: 'PUT', body: JSON.stringify({ userName }) }),
    confirmSend: (id, userName) => apiFetch(`/StockTransfer/${id}/confirm-send`, { method: 'PUT', body: JSON.stringify({ userName }) }),
    receive:     (id, userName) => apiFetch(`/StockTransfer/${id}/receive`,      { method: 'PUT', body: JSON.stringify({ userName }) }),
  },
  stockCount: {
    getAll:     ()     => apiFetch('/StockCount'),
    settings:   ()     => apiFetch('/StockCount/settings'),
    start:      (data) => apiFetch('/StockCount/start', { method: 'POST', body: JSON.stringify(data) }),
    submitLine: (countId, lineId, physicalQty) =>
      apiFetch(`/StockCount/${countId}/lines/${lineId}`, { method: 'PUT', body: JSON.stringify({ physicalQty }) }),
    reconcile:  (countId, remarks, userName) =>
      apiFetch(`/StockCount/${countId}/reconcile`, { method: 'PUT', body: JSON.stringify({ remarks, userName }) }),
  },
  disposal: {
    getAll:  ()     => apiFetch('/Disposal'),
    scan:    ()     => apiFetch('/Disposal/scan', { method: 'POST' }),
    create:  (data) => apiFetch('/Disposal', { method: 'POST', body: JSON.stringify(data) }),
    approve: (id, userName) => apiFetch(`/Disposal/${id}/approve`, { method: 'PUT', body: JSON.stringify({ userName }) }),
    dispose: (id, userName) => apiFetch(`/Disposal/${id}/dispose`, { method: 'PUT', body: JSON.stringify({ userName }) }),
  },
  dashboard: {
    alerts:    () => apiFetch('/Dashboard/alerts'),
    stock:     () => apiFetch('/Dashboard/stock'),
    aging:     (days) => apiFetch('/Dashboard/aging?' + new URLSearchParams({ days: days ?? 30 })),
    topBottom: () => apiFetch('/Dashboard/top-bottom'),
    recurrentFailures: (days) => apiFetch('/Dashboard/recurrent-failures?' + new URLSearchParams({ days: days ?? 30 })),
  },
  reports: {
    auditChecklist: (params = {}) => apiFetch('/Report/audit-checklist?' + new URLSearchParams(params)),
    lifecycle:      ()             => apiFetch('/Report/lifecycle'),
  },
  equivalentGroups: {
    getAll:       ()           => apiFetch('/EquivalentGroup'),
    create:       (data)       => apiFetch('/EquivalentGroup', { method: 'POST', body: JSON.stringify(data) }),
    update:       (id, data)   => apiFetch(`/EquivalentGroup/${id}`, { method: 'PUT',    body: JSON.stringify(data) }),
    remove:       (id)         => apiFetch(`/EquivalentGroup/${id}`, { method: 'DELETE' }),
    addMember:    (id, partNo) => apiFetch(`/EquivalentGroup/${id}/members`, { method: 'POST', body: JSON.stringify({ partNo }) }),
    removeMember: (id, memberId) => apiFetch(`/EquivalentGroup/${id}/members/${memberId}`, { method: 'DELETE' }),
    forPart:      (partNo)     => apiFetch(`/EquivalentGroup/for-part/${encodeURIComponent(partNo)}`),
    importExcel:  (file)       => { const fd = new FormData(); fd.append('file', file); return apiUpload('/EquivalentGroup/import', fd); },
  },
  tracking: {
    bySerial: (sn) => apiFetch(`/Tracking/serial/${encodeURIComponent(sn)}`),
  },
  users: {
    getAll:        ()           => apiFetch('/Users'),
    roles:         ()           => apiFetch('/Users/roles'),
    create:        (data)       => apiFetch('/Users', { method: 'POST', body: JSON.stringify(data) }),
    update:        (id, data)   => apiFetch(`/Users/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    resetPassword: (id, password) => apiFetch(`/Users/${id}/password`, { method: 'PUT', body: JSON.stringify({ password }) }),
  },
  atmModels: {
    getAll:        (params = {}) => apiFetch('/AtmModel?' + new URLSearchParams(params)),
    getById:       (id)          => apiFetch(`/AtmModel/${id}`),
    getCompatible: (id)          => apiFetch(`/AtmModel/${id}/parts`),
    create:        (data)        => apiFetch('/AtmModel', { method: 'POST', body: JSON.stringify(data) }),
    update:        (id, data)    => apiFetch(`/AtmModel/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    remove:        (id)          => apiFetch(`/AtmModel/${id}`, { method: 'DELETE' }),
    addPart:       (id, partNo)  => apiFetch(`/AtmModel/${id}/parts`, { method: 'POST', body: JSON.stringify({ partNo }) }),
    removePart:    (id, partId)  => apiFetch(`/AtmModel/${id}/parts/${partId}`, { method: 'DELETE' }),
  },
};
