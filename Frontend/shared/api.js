/* ── Centralized API client ── */
const API_BASE = 'http://localhost:5128/api';

async function apiFetch(path, options = {}) {
  const url = API_BASE + path;
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options
  });
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
    getAll:   ()        => apiFetch('/Ticket'),
    create:   (data)    => apiFetch('/Ticket',              { method: 'POST',  body: JSON.stringify(data) }),
    approve:  (id, dto) => apiFetch(`/Ticket/${id}/approve`, { method: 'PUT',   body: JSON.stringify(dto) }),
    reject:   (id)      => apiFetch(`/Ticket/${id}/reject`,  { method: 'PUT' }),
    receive:  (id)      => apiFetch(`/Ticket/${id}/receive`, { method: 'PUT' }),
    doa:      (id)      => apiFetch(`/Ticket/${id}/doa`,     { method: 'PUT' }),
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
  },
  tracking: {
    bySerial: (sn) => apiFetch(`/Tracking/serial/${encodeURIComponent(sn)}`),
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
