'use strict';

document.addEventListener('DOMContentLoaded', () => {
    const $ = id => document.getElementById(id);
    const pageSize = 100;
    let offset = 0;
    let requestId = 0;

    function dateBoundary(value, nextDay = false) {
        const date = new Date(`${value}T00:00:00`);
        if (nextDay) date.setDate(date.getDate() + 1);
        return date.toISOString();
    }

    async function load() {
        const current = ++requestId;
        const parameters = new URLSearchParams({ offset, limit: pageSize, includeSteps: $('historySteps').checked, includeSkipped: $('historySkipped').checked });
        if ($('historyRepository').value.trim()) parameters.set('repository', $('historyRepository').value.trim());
        if ($('historyFrom').value) parameters.set('from', dateBoundary($('historyFrom').value));
        if ($('historyTo').value) parameters.set('to', dateBoundary($('historyTo').value, true));
        if ($('historyResult').value) parameters.set('status', $('historyResult').value);
        $('historyStatus').textContent = 'Loading history…';
        try {
            const response = await fetch(`/api/history?${parameters}`);
            const page = await response.json();
            if (!response.ok) throw new Error(page.error || 'History could not be loaded.');
            if (current !== requestId) return;
            const rows = document.createDocumentFragment();
            for (const entry of page.entries) {
                const row = document.createElement('tr');
                row.title = entry.batchId ? `Batch: ${entry.batchId}` : `Operation: ${entry.id}`;
                const cells = [new Date(entry.startedAt).toLocaleString(), entry.isBatch ? 'Batch' : entry.repositoryName,
                    `${entry.isBatch ? 'Batch ' : ''}${entry.action}`, `${entry.fromVersion || '—'} → ${entry.toVersion || '—'}`,
                    entry.status === 'PartialFailure' ? 'Partial failure' : entry.status, entry.summary];
                cells.forEach((text, index) => {
                    const cell = document.createElement('td');
                    cell.textContent = text;
                    if (index === 4) cell.className = `history-result ${entry.status.toLowerCase()}`;
                    row.appendChild(cell);
                });
                rows.appendChild(row);
            }
            $('historyRows').replaceChildren(rows);
            $('historyStatus').textContent = page.total
                ? `${offset + 1}–${offset + page.entries.length} of ${page.total} entries`
                : 'No history matches these filters.';
            $('historyPrevious').disabled = offset === 0;
            $('historyNext').disabled = offset + page.entries.length >= page.total;
        } catch (error) {
            if (current === requestId) $('historyStatus').textContent = error.message;
        }
    }

    document.querySelector('[data-tab="history"]').addEventListener('click', () => { offset = 0; load(); });
    $('refreshHistory').addEventListener('click', () => { offset = 0; load(); });
    $('historyRepository').addEventListener('input', () => { offset = 0; load(); });
    for (const id of ['historyFrom', 'historyTo', 'historyResult', 'historySteps', 'historySkipped'])
        $(id).addEventListener('change', () => { offset = 0; load(); });
    $('historyPrevious').addEventListener('click', () => { offset = Math.max(0, offset - pageSize); load(); });
    $('historyNext').addEventListener('click', () => { offset += pageSize; load(); });
    $('clearHistory').addEventListener('click', async () => {
        if (!window.confirm('Clear completed history? Running operations are kept.')) return;
        try {
            const response = await fetch('/api/history?confirm=true', { method: 'DELETE' });
            if (!response.ok) throw new Error('History could not be cleared.');
            offset = 0;
            await load();
        } catch (error) { $('historyStatus').textContent = error.message; }
    });
});
