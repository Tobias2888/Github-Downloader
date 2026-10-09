'use strict';

(() => {
    const $ = (id) => document.getElementById(id);

    /* ---------- Utilities ---------- */

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    async function apiRequest(path, { method = 'GET', body } = {}) {
        const options = { method, headers: {} };
        if (body !== undefined) {
            options.headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }
        const res = await fetch(`/api/${path}`, options);
        if (!res.ok) {
            let detail = `Request failed (${res.status})`;
            try {
                const data = await res.json();
                if (data && data.error) detail = data.error;
            } catch { /* ignore */ }
            throw new Error(detail);
        }
        if (res.status === 204) return null;
        try {
            return await res.json();
        } catch {
            return null;
        }
    }

    function createToast(message, type = 'info') {
        const container = $('toastContainer');
        const toast = document.createElement('div');
        toast.className = `toast ${type}`;
        toast.textContent = message;
        container.appendChild(toast);
        setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transition = 'opacity 0.3s ease';
            setTimeout(() => toast.remove(), 300);
        }, 3500);
    }

    function confirmDialog(message) {
        return window.confirm(message);
    }

    function repoGithubLink(repo) {
        if (repo.gitHubLink) return repo.gitHubLink;
        return `https://github.com/${repo.name || ''}`;
    }

    function repoBaseUrl(url) {
        return (url || '')
            .replace('api.github.com', 'github.com')
            .replace('/releases/latest', '');
    }

    function linkifyChangelog(text, baseUrl) {
        const esc = escapeHtml(text || '(No changelog available)');
        return esc.replace(/#(\d+)/g, (match, num) =>
            `<a href="${escapeHtml(baseUrl)}/issues/${num}" target="_blank" rel="noopener">#${num}</a>`);
    }

    async function withProgress(title, work) {
        const container = $('progressContainer');
        const fill = $('progressFill');
        $('progressTitle').textContent = title;
        $('progressPercent').textContent = '...';
        fill.classList.add('indeterminate');
        $('progressDetails').textContent = '';
        container.style.display = 'block';
        try {
            return await work({
                detail: (text) => { $('progressDetails').textContent = text; },
                percent: (p) => {
                    fill.classList.remove('indeterminate');
                    fill.style.width = `${p}%`;
                    $('progressPercent').textContent = `${p}%`;
                },
            });
        } finally {
            setTimeout(() => { container.style.display = 'none'; }, 1200);
        }
    }

    function setBusy(button, busy, busyText) {
        if (!button) return;
        if (busy) {
            button.dataset.originalLabel = button.textContent.trim();
            button.textContent = busyText;
            button.disabled = true;
        } else {
            if (button.dataset.originalLabel) button.textContent = button.dataset.originalLabel;
            button.disabled = false;
        }
    }

    /* ---------- State ---------- */

    let repos = [];
    let updates = [];
    let currentRepo = null;

    /* ---------- Tabs ---------- */

    function switchTab(tabName) {
        document.querySelectorAll('.tab').forEach((t) => t.classList.toggle('active', t.dataset.tab === tabName));
        document.querySelectorAll('.tab-panel').forEach((p) => p.classList.toggle('active', p.id === `${tabName}-panel`));
    }

    /* ---------- Status / Health ---------- */

    async function refreshHealth() {
        const text = $('statusText');
        try {
            const health = await apiRequest('health');
            text.textContent = `${health.repoCount} repos`;
            $('appVersion').textContent = `v${health.version}`;
        } catch {
            text.textContent = 'Offline';
        }
    }

    /* ---------- Repositories ---------- */

    async function loadRepos() {
        const data = await apiRequest('repositories');
        repos = data?.repos ?? [];
        renderReposTable();
        renderConfigPreview();
    }

    function renderReposTable() {
        const tbody = $('repoTableBody');
        const empty = $('emptyRepos');
        tbody.innerHTML = '';

        if (repos.length === 0) {
            empty.style.display = 'flex';
            return;
        }
        empty.style.display = 'none';

        for (const repo of repos) {
            const tr = document.createElement('tr');

            const selectedAsset = repo.assetNames.length > 0
                ? repo.assetNames[repo.downloadAssetIndex] || repo.assetNames[0]
                : '';
            const isUpToDate = repo.isUpToDate;

            tr.innerHTML = `
                <td>
                    <div class="repo-name">
                        <strong>${escapeHtml(repo.name || repo.url)}</strong>
                        <a href="${escapeHtml(repoGithubLink(repo))}" target="_blank" rel="noopener">${escapeHtml(repo.url)}</a>
                    </div>
                </td>
                <td><span class="tag tag-current">${escapeHtml(repo.currentInstallTag || '—')}</span></td>
                <td>
                    ${isUpToDate
                        ? `<span class="tag tag-current">${escapeHtml(repo.tag || '—')}</span>`
                        : `<span class="tag tag-update">${escapeHtml(repo.tag || '—')}</span>`}
                </td>
                <td>
                    ${isUpToDate
                        ? '<span class="tag tag-current">Up to date</span>'
                        : '<span class="tag tag-update">Update available</span>'}
                </td>
                <td>
                    ${selectedAsset
                        ? `<span class="asset-chip selected" title="${escapeHtml(selectedAsset)}">${escapeHtml(selectedAsset)}</span>`
                        : '<span class="mono" style="color:var(--text-soft)">—</span>'}
                </td>
                <td><span class="path" title="${escapeHtml(repo.downloadPath)}">${escapeHtml(repo.downloadPath || '—')}</span></td>
                <td>
                    <div class="row-actions">
                        <button class="btn-link" data-action="details" data-url="${escapeHtml(repo.url)}">Details</button>
                        <button class="btn-link" data-action="changelog" data-url="${escapeHtml(repo.url)}">Changelog</button>
                        <button class="btn" data-action="download" data-url="${escapeHtml(repo.url)}">Download</button>
                        <button class="btn-link danger" data-action="delete" data-url="${escapeHtml(repo.url)}">Delete</button>
                    </div>
                </td>`;

            tbody.appendChild(tr);
        }
    }

    /* ---------- Config preview ---------- */

    async function renderConfigPreview() {
        const preview = $('configPreview');
        try {
            preview.textContent = JSON.stringify(repos, null, 2);
        } catch {
            preview.textContent = 'Failed to load configuration';
        }
    }

    /* ---------- Updates ---------- */

    async function checkAllUpdates() {
        const btn = $('checkAllUpdatesBtn');
        setBusy(btn, true, 'Checking...');
        try {
            const result = await withProgress('Checking for updates', async () => {
                const response = await apiRequest('updates/search', { body: { repoUrls: null } });
                return response;
            });
            updates = result?.updates ?? [];
            renderUpdatesTable();
            toast(`Checked ${result?.totalChecked ?? 0} repositories`, updates.some(u => u.hasUpdate) ? 'info' : 'success');
            await loadRepos();
        } catch (err) {
            createToast(`Check failed: ${err.message}`, 'error');
        } finally {
            setBusy(btn, false);
        }
    }

    function renderUpdatesTable() {
        const tbody = $('updatesTableBody');
        const empty = $('emptyUpdates');
        const available = updates.filter(u => u.hasUpdate);
        tbody.innerHTML = '';

        if (available.length === 0) {
            empty.style.display = 'flex';
            return;
        }
        empty.style.display = 'none';

        for (const update of available) {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td>
                    <div class="repo-name">
                        <strong>${escapeHtml(update.repoName)}</strong>
                        <span class="mono" style="color:var(--text-soft)">${escapeHtml(repoBaseUrl(update.repoUrl))}</span>
                    </div>
                </td>
                <td><span class="tag tag-current">${escapeHtml(update.currentTag || '—')}</span></td>
                <td><span class="tag tag-update">${escapeHtml(update.latestTag || '—')}</span></td>
                <td>
                    <div class="asset-list">
                        ${(update.assetNames || []).map(a => `<span class="asset-chip">${escapeHtml(a)}</span>`).join('') || '<span class="mono" style="color:var(--text-soft)">—</span>'}
                    </div>
                </td>
                <td><button class="btn-link" data-action="changelog" data-url="${escapeHtml(update.repoUrl)}">View</button></td>
                <td>
                    <div class="row-actions">
                        <button class="btn" data-action="download" data-url="${escapeHtml(update.repoUrl)}">Download</button>
                        <button class="btn btn-success" data-action="install" data-url="${escapeHtml(update.repoUrl)}">Install</button>
                    </div>
                </td>`;
            tbody.appendChild(tr);
        }
    }

    /* ---------- Download / Install ---------- */

    async function downloadRepos(urls) {
        const result = await withProgress('Downloading updates', async () => {
            const response = await apiRequest('updates/download', { body: { repoUrls: urls, downloadAnyways: false } });
            return response;
        });
        createToast(result?.message || 'Download complete', 'success');
        if (result?.downloadedAssets?.length) {
            $('progressDetails').textContent = result.downloadedAssets
                .map(a => `${a.repoName}: ${a.assetName} → ${a.localPath}`)
                .join('\n');
        }
        await loadRepos();
        await refreshHealth();
    }

    async function installRepos(urls) {
        const result = await withProgress('Installing updates', async () => {
            const response = await apiRequest('updates/install', { body: { repoUrls: urls, downloadAnyways: false } });
            return response;
        });
        createToast(result?.message || 'Install complete', 'success');
        if (result?.installedAssets?.length) {
            $('progressDetails').textContent = result.installedAssets
                .map(a => `${a.repoName}: ${a.assetName} → ${a.localPath}`)
                .join('\n');
        }
        await loadRepos();
        await refreshHealth();
    }

    /* ---------- Add repo modal ---------- */

    function openAddRepoModal() {
        $('repoUrl').value = '';
        $('repoOwner').value = '';
        $('repoName').value = '';
        showModal('addRepoModal');
        $('repoUrl').focus();
    }

    function closeAddRepoModal() {
        hideModal('addRepoModal');
    }

    async function submitAddRepo() {
        const method = document.querySelector('input[name="addMethod"]:checked').value;
        const btn = $('confirmAddRepo');
        setBusy(btn, true, 'Adding...');
        try {
            let repo;
            if (method === 'name') {
                const owner = $('repoOwner').value.trim();
                const name = $('repoName').value.trim();
                if (!owner || !name) throw new Error('Owner and repository name are required');
                repo = await apiRequest('repositories/by-name', { method: 'POST', body: { publisherName: owner, repoName: name } });
            } else {
                const url = $('repoUrl').value.trim();
                if (!url) throw new Error('A repository URL is required');
                repo = await apiRequest('repositories', { method: 'POST', body: { repoUrl: url } });
            }
            createToast(`Added repository: ${repo.name}`, 'success');
            closeAddRepoModal();
            switchTab('repositories');
            await loadRepos();
            await refreshHealth();
        } catch (err) {
            createToast(`Failed to add repository: ${err.message}`, 'error');
        } finally {
            setBusy(btn, false);
        }
    }

    /* ---------- Details modal ---------- */

    function openRepoDetails(repo) {
        currentRepo = repo;
        $('repoDetailsTitle').textContent = repo.name || repo.url;

        const body = $('repoDetailsBody');
        const selectedAssetIndex = repo.assetNames.length > 0
            ? Math.min(repo.downloadAssetIndex, repo.assetNames.length - 1)
            : 0;

        body.innerHTML = `
            <div>
                <div class="detail-section-title">Repository</div>
                <p class="repo-desc">${escapeHtml(repo.description || 'No description available')}</p>
                <p class="repo-desc">
                    <a href="${escapeHtml(repoGithubLink(repo))}" target="_blank" rel="noopener" style="color:var(--accent)">View on GitHub</a>
                    ${repo.releaseDate ? ` · Released ${escapeHtml(repo.releaseDate.replace('T', ' ').replace('Z', ''))}` : ''}
                </p>
            </div>

            <div>
                <div class="detail-section-title">Version</div>
                <div class="field-row">
                    <span style="color:var(--text-muted); font-size:13px">Target release tag</span>
                    <select id="detailTargetTag" style="max-width:60%">
                        ${repo.tags.map(t => `<option value="${escapeHtml(t)}" ${t === repo.targetTag ? 'selected' : ''}>${escapeHtml(t)}</option>`).join('')}
                    </select>
                </div>
                <div class="field-row" style="margin-top:10px">
                    <span style="color:var(--text-muted); font-size:13px">Asset to download</span>
                    <select id="detailAssetIndex" style="max-width:60%">
                        ${(repo.assetNames || []).map((a, i) => `<option value="${i}" ${i === selectedAssetIndex ? 'selected' : ''}>${escapeHtml(a)}</option>`).join('') || '<option value="0">— no assets —</option>'}
                    </select>
                </div>
                <p class="repo-desc" style="margin-top:8px">
                    <span class="tag tag-current">${escapeHtml(repo.currentInstallTag || '—')}</span>
                    <span class="tag tag-update">${escapeHtml(repo.tag || '—')}</span>
                </p>
            </div>

            <div>
                <div class="detail-section-title">Download Settings</div>
                <label class="checkbox-label field-row" style="justify-content:space-between">
                    <span>Save file anyway</span>
                    <span class="switch"><input type="checkbox" id="detailSaveAnyway" ${repo.saveFileAnyway ? 'checked' : ''}><span class="slider"></span></span>
                </label>
                <div class="form-group" style="margin-top:12px">
                    <label for="detailDownloadPath">Download location</label>
                    <input type="text" id="detailDownloadPath" value="${escapeHtml(repo.downloadPath || '')}">
                </div>
                <div class="form-group" style="margin-top:12px">
                    <label for="detailNewFileName">Rename file (leave empty to keep original name)</label>
                    <input type="text" id="detailNewFileName" placeholder="new-filename.ext" value="${escapeHtml(repo.newFileName || '')}">
                </div>
                <label class="checkbox-label field-row" style="justify-content:space-between; margin-top:12px">
                    <span>Exclude from Download All</span>
                    <span class="switch"><input type="checkbox" id="detailExcluded" ${repo.excludedFromDownloadAll ? 'checked' : ''}><span class="slider"></span></span>
                </label>
            </div>`;

        showModal('repoDetailsModal');
    }

    async function saveRepoDetails() {
        if (!currentRepo) return;
        const repo = currentRepo;

        const body = {
            downloadPath: $('detailDownloadPath').value,
            targetTag: $('detailTargetTag').value,
            downloadAssetIndex: parseInt($('detailAssetIndex').value, 10) || 0,
            saveFileAnyway: $('detailSaveAnyway').checked,
            newFileName: $('detailNewFileName').value,
            excludedFromDownloadAll: $('detailExcluded').checked,
        };

        const btn = document.querySelector('#repoDetailsModal .modal-footer .btn-primary');
        setBusy(btn, true, 'Saving...');
        try {
            await apiRequest(`repositories/${encodeURIComponent(repo.url)}`, { method: 'PUT', body });
            await withProgress('Updating repository details', async () => {
                await apiRequest('updates/search', { body: { repoUrls: [repo.url] } });
            });
            createToast('Repository updated', 'success');
            hideModal('repoDetailsModal');
            await loadRepos();
        } catch (err) {
            createToast(`Failed to update repository: ${err.message}`, 'error');
        } finally {
            setBusy(btn, false);
        }
    }

    /* ---------- Changelog modal ---------- */

    function openChangelog(repoUrl, changelog) {
        const repo = repos.find(r => r.url === repoUrl);
        const baseUrl = repo ? repoGithubLink(repo) : repoBaseUrl(repoUrl);
        $('changelogTitle').textContent = `Changelog — ${repo?.name || repoUrl}`;
        $('changelogContent').innerHTML = linkifyChangelog(changelog, baseUrl);
        showModal('changelogModal');
    }

    /* ---------- Config tab ---------- */

    async function exportConfig() {
        const path = $('exportPath').value.trim();
        const btn = $('exportConfigBtn');
        setBusy(btn, true, 'Exporting...');
        try {
            const result = await apiRequest('config/export', { method: 'POST', body: { destinationPath: path } });
            createToast(result?.message || 'Configuration exported', 'success');
        } catch (err) {
            createToast(`Export failed: ${err.message}`, 'error');
        } finally {
            setBusy(btn, false);
        }
    }

    async function importConfig() {
        const path = $('importPath').value.trim();
        const btn = $('importConfigBtn');
        setBusy(btn, true, 'Importing...');
        try {
            const result = await apiRequest('config/import', { method: 'POST', body: { sourcePath: path } });
            createToast(result?.message || 'Configuration imported', 'success');
            await loadRepos();
            await refreshHealth();
            switchTab('repositories');
        } catch (err) {
            createToast(`Import failed: ${err.message}`, 'error');
        } finally {
            setBusy(btn, false);
        }
    }

    /* ---------- Settings tab ---------- */

    async function loadSettingsInfo() {
        try {
            const info = await apiRequest('settings/info');
            $('infoVersion').textContent = info.version;
            $('infoRepoCount').textContent = String(info.repoCount);
            $('infoPlatform').textContent = info.platform;
            $('infoDataDir').textContent = info.dataDirectory;
            $('appVersion').textContent = `v${info.version}`;
        } catch { /* keep defaults */ }
    }

    async function savePat() {
        const pat = $('patInput').value.trim();
        if (!pat) {
            createToast('Please enter a personal access token', 'error');
            return;
        }
        const btn = $('savePatBtn');
        setBusy(btn, true, 'Saving...');
        try {
            await apiRequest('settings/pat', { method: 'POST', body: { pat } });
            $('patInput').value = '';
            $('removePatBtn').disabled = false;
            createToast('Personal access token saved', 'success');
        } catch (err) {
            createToast(`Failed to save token: ${err.message}`, 'error');
        } finally {
            setBusy(btn, false);
        }
    }

    async function removePat() {
        const btn = $('removePatBtn');
        setBusy(btn, true, 'Removing...');
        try {
            await apiRequest('settings/pat', { method: 'DELETE' });
            $('removePatBtn').disabled = true;
            createToast('Personal access token removed', 'info');
        } catch (err) {
            createToast(`Failed to remove token: ${err.message}`, 'error');
        } finally {
            setBusy(btn, false);
        }
    }

    async function clearAllData() {
        if (!confirmDialog('This will remove all tracked repositories and cached files. This action is irreversible. Continue?')) return;
        const btn = $('clearAllDataBtn');
        setBusy(btn, true, 'Clearing...');
        try {
            const result = await apiRequest('settings/clear-data', { method: 'POST' });
            createToast(result?.message || 'All data cleared', 'success');
            repos = [];
            updates = [];
            renderReposTable();
            renderUpdatesTable();
            renderConfigPreview();
            await loadSettingsInfo();
            await refreshHealth();
        } catch (err) {
            createToast(`Failed to clear data: ${err.message}`, 'error');
        } finally {
            setBusy(btn, false);
        }
    }

    /* ---------- Modal helpers ---------- */

    function showModal(id) {
        const overlay = $('modalOverlay');
        overlay.classList.add('visible');
        document.querySelectorAll('.modal').forEach(m => m.classList.remove('open'));
        $(id).classList.add('open');
    }

    function hideModal(id) {
        const overlay = $('modalOverlay');
        $(id).classList.remove('open');
        if (!document.querySelector('.modal.open')) overlay.classList.remove('visible');
    }

    /* ---------- Event delegation ---------- */

    function handleRowAction(action, url, sourceElement) {
        switch (action) {
            case 'details': {
                const repo = repos.find(r => r.url === url);
                if (repo) openRepoDetails(repo);
                break;
            }
            case 'changelog': {
                const repo = repos.find(r => r.url === url);
                const update = updates.find(u => u.repoUrl === url);
                const changelog = repo?.latestChangelog ?? update?.changelog;
                openChangelog(url, changelog);
                break;
            }
            case 'download': {
                const repoEntry = repos.find(r => r.url === url);
                createToast(`Downloading ${repoEntry?.name || url}...`, 'info');
                downloadRepos([url]);
                break;
            }
            case 'install': {
                const repoEntry = repos.find(r => r.url === url);
                createToast(`Installing ${repoEntry?.name || url}...`, 'info');
                installRepos([url]);
                break;
            }
            case 'delete': {
                deleteRepo(url);
                break;
            }
            default:
                break;
        }
    }

    async function deleteRepo(url) {
        const repo = repos.find(r => r.url === url);
        if (!confirmDialog(`Remove "${repo?.name || url}" from tracking?`)) return;
        try {
            await apiRequest(`repositories/${encodeURIComponent(url)}`, { method: 'DELETE' });
            createToast('Repository removed', 'success');
            await loadRepos();
            await refreshHealth();
        } catch (err) {
            createToast(`Failed to remove repository: ${err.message}`, 'error');
        }
    }

    /* ---------- Initialization ---------- */

    function bindEvents() {
        document.querySelectorAll('.tab').forEach((tab) => {
            tab.addEventListener('click', () => switchTab(tab.dataset.tab));
        });

        $('addRepoBtn').addEventListener('click', openAddRepoModal);
        $('addFirstRepoBtn').addEventListener('click', openAddRepoModal);
        $('closeAddRepoModal').addEventListener('click', closeAddRepoModal);
        $('cancelAddRepo').addEventListener('click', closeAddRepoModal);
        $('confirmAddRepo').addEventListener('click', submitAddRepo);

        document.querySelectorAll('input[name="addMethod"]').forEach((radio) => {
            radio.addEventListener('change', () => {
                const urlMethod = radio.value === 'url';
                $('urlMethodGroup').style.display = urlMethod ? 'flex' : 'none';
                $('nameMethodGroup').style.display = urlMethod ? 'none' : 'flex';
            });
        });

        $('repoTableBody').addEventListener('click', (e) => {
            const btn = e.target.closest('[data-action]');
            if (!btn) return;
            handleRowAction(btn.dataset.action, btn.dataset.url, btn);
        });

        $('updatesTableBody').addEventListener('click', (e) => {
            const btn = e.target.closest('[data-action]');
            if (!btn) return;
            handleRowAction(btn.dataset.action, btn.dataset.url, btn);
        });

        $('checkAllUpdatesBtn').addEventListener('click', checkAllUpdates);
        $('downloadAllBtn').addEventListener('click', () => {
            const urls = repos.map(r => r.url);
            if (urls.length === 0) {
                createToast('No repositories to download', 'info');
                return;
            }
            downloadRepos(urls);
        });
        $('installAllBtn').addEventListener('click', () => {
            const urls = repos.map(r => r.url);
            if (urls.length === 0) {
                createToast('No repositories to install', 'info');
                return;
            }
            installRepos(urls);
        });

        $('closeRepoDetailsModal').addEventListener('click', () => hideModal('repoDetailsModal'));
        $('closeRepoDetails').addEventListener('click', () => hideModal('repoDetailsModal'));
        document.querySelector('#repoDetailsModal .modal-footer .btn-primary').addEventListener('click', saveRepoDetails);

        $('closeChangelogModal').addEventListener('click', () => hideModal('changelogModal'));
        $('closeChangelog').addEventListener('click', () => hideModal('changelogModal'));

        $('exportConfigBtn').addEventListener('click', exportConfig);
        $('importConfigBtn').addEventListener('click', importConfig);

        $('savePatBtn').addEventListener('click', savePat);
        $('removePatBtn').addEventListener('click', removePat);
        $('togglePatVisibility').addEventListener('click', () => {
            const input = $('patInput');
            input.type = input.type === 'password' ? 'text' : 'password';
        });
        $('clearAllDataBtn').addEventListener('click', clearAllData);

        $('modalOverlay').addEventListener('click', () => {
            document.querySelectorAll('.modal').forEach(m => m.classList.remove('open'));
            $('modalOverlay').classList.remove('visible');
        });

        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                document.querySelectorAll('.modal').forEach(m => m.classList.remove('open'));
                $('modalOverlay').classList.remove('visible');
            }
        });

        $('patInput').addEventListener('input', () => {
            $('savePatBtn').disabled = $('patInput').value.trim().length === 0;
        });
    }

    async function init() {
        bindEvents();

        await refreshHealth();
        await loadSettingsInfo();

        try {
            const patStatus = await apiRequest('settings/pat');
            $('removePatBtn').disabled = !patStatus?.hasPat;
        } catch { /* ignore */ }

        try {
            await loadRepos();
        } catch (err) {
            createToast(`Failed to load repositories: ${err.message}`, 'error');
        }
    }

    document.addEventListener('DOMContentLoaded', init);
})();