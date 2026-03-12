const state = {
    configuration: { routes: [], clusters: [], settings: {} },
    selected: null,
    ui: loadUiState(),
    activeView: "dashboard",
    telemetry: {
        summary: null,
        events: { page: 1, pageSize: 25, totalCount: 0, events: [] },
        topSources: [],
        topTargets: [],
        trends: [],
        isLoading: false,
        lastLoadedUtc: null,
        error: "",
    },
};

let statusToastTimer = null;
let statusToastPinned = false;
let telemetryRefreshTimer = null;
const draftKeyStore = {
    route: new WeakMap(),
    cluster: new WeakMap(),
};
const savedDraftSnapshots = {
    route: new Map(),
    cluster: new Map(),
};
const deletedDraftKeys = {
    route: new Set(),
    cluster: new Set(),
};
let nextDraftKey = 1;

const elements = {
    routeList: document.getElementById("route-list"),
    clusterList: document.getElementById("cluster-list"),
    headerSelection: document.getElementById("header-selection"),
    dashboardStatus: document.getElementById("dashboard-status"),
    dashboardStatusLabel: document.getElementById("dashboard-status-label"),
    triggerUpdate: document.getElementById("trigger-update"),
    tabDashboard: document.getElementById("tab-dashboard"),
    tabEmpty: document.getElementById("tab-empty"),
    dashboardView: document.getElementById("dashboard-view"),
    emptyView: document.getElementById("empty-view"),
    editorTitle: document.getElementById("editor-title"),
    emptyState: document.getElementById("empty-state"),
    routeEditor: document.getElementById("route-editor"),
    clusterEditor: document.getElementById("cluster-editor"),
    destinationList: document.getElementById("destination-list"),
    template: document.getElementById("destination-row-template"),
    headerVersion: document.getElementById("header-version"),
    statusToast: document.getElementById("status-toast"),
    publicHttpsEndpoint: document.getElementById("public-https-endpoint"),
    environmentName: document.getElementById("environment-name"),
    certificateState: document.getElementById("certificate-state"),
    lastApplied: document.getElementById("last-applied"),
    certificateDescription: document.getElementById("certificate-description"),
    restartBanner: document.getElementById("restart-banner"),
    restartHint: document.getElementById("restart-hint"),
    routeFiltersPanel: document.getElementById("route-filters"),
    clusterFiltersPanel: document.getElementById("cluster-filters"),
    routeFiltersToggle: document.getElementById("toggle-route-filters"),
    clusterFiltersToggle: document.getElementById("toggle-cluster-filters"),
    routeSearch: document.getElementById("route-search"),
    clusterSearch: document.getElementById("cluster-search"),
    routeFilterLinked: document.getElementById("route-filter-linked"),
    routeFilterHosts: document.getElementById("route-filter-hosts"),
    clusterFilterReferenced: document.getElementById("cluster-filter-referenced"),
    clusterFilterHealth: document.getElementById("cluster-filter-health"),
    routeVisibleCount: document.getElementById("route-visible-count"),
    clusterVisibleCount: document.getElementById("cluster-visible-count"),
    telemetryHours: document.getElementById("telemetry-hours"),
    telemetryRefresh: document.getElementById("telemetry-refresh"),
    telemetrySmoke: document.getElementById("telemetry-smoke"),
    telemetrySubtitle: document.getElementById("telemetry-subtitle"),
    telemetryEventCount: document.getElementById("telemetry-event-count"),
    telemetryHighRiskCount: document.getElementById("telemetry-high-risk-count"),
    telemetrySourceCount: document.getElementById("telemetry-source-count"),
    telemetryLatestEvent: document.getElementById("telemetry-latest-event"),
    telemetryAlertState: document.getElementById("telemetry-alert-state"),
    telemetryLastUpdated: document.getElementById("telemetry-last-updated"),
    telemetryPageLabel: document.getElementById("telemetry-page-label"),
    telemetryPrevPage: document.getElementById("telemetry-prev-page"),
    telemetryNextPage: document.getElementById("telemetry-next-page"),
    telemetryEventsBody: document.getElementById("telemetry-events-body"),
    telemetryTopSources: document.getElementById("telemetry-top-sources"),
    telemetryTopTargets: document.getElementById("telemetry-top-targets"),
    telemetryTrend: document.getElementById("telemetry-trend"),
    telemetryGraphCopy: document.getElementById("telemetry-graph-copy"),
    telemetryRiskFilter: document.getElementById("telemetry-risk-filter"),
    telemetryCategoryFilter: document.getElementById("telemetry-category-filter"),
    healthCheckDetailFields: Array.from(document.querySelectorAll(".health-check-grid label:not(.checkbox-row)")),
};

document.getElementById("add-route").addEventListener("click", () => {
    const nextRoute = {
        routeId: `route${state.configuration.routes.length + 1}`,
        clusterId: state.configuration.clusters[0]?.clusterId ?? "cluster1",
        path: "{**catch-all}",
        hosts: [],
        order: 0,
    };
    state.configuration.routes.push(nextRoute);
    selectItem("route", nextRoute.routeId);
    render();
});

document.getElementById("add-cluster").addEventListener("click", () => {
    const nextCluster = {
        clusterId: `cluster${state.configuration.clusters.length + 1}`,
        loadBalancingPolicy: "",
        healthCheck: {
            enabled: false,
            interval: "00:00:10",
            timeout: "00:00:03",
            policy: "ConsecutiveFailures",
            path: "",
            query: "",
        },
        consecutiveFailuresThreshold: 1,
        destinations: [],
    };
    nextCluster.destinations.push({
        destinationId: buildDefaultDestinationId(nextCluster),
        address: "https://service.internal:5001",
    });
    state.configuration.clusters.push(nextCluster);
    selectItem("cluster", nextCluster.clusterId);
    render();
});

document.getElementById("delete-selected").addEventListener("click", async () => {
    if (!state.selected) {
        return;
    }

    const deleteButton = document.getElementById("delete-selected");
    const previousConfiguration = cloneConfiguration(state.configuration);
    const previousSelection = state.selected ? { ...state.selected } : null;
    const deletingRoute = state.selected.type === "route";

    deleteButton.disabled = true;

    try {
        if (deletingRoute) {
            const route = getSelectedRoute();
            if (route) {
                markDraftDeleted("route", route);
            }

            state.configuration.routes = state.configuration.routes.filter(route => route.routeId !== state.selected.id);
        } else {
            const cluster = getSelectedCluster();
            if (cluster) {
                markDraftDeleted("cluster", cluster);
            }

            state.configuration.routes
                .filter(route => route.clusterId === state.selected.id)
                .forEach(route => markDraftDeleted("route", route));
            state.configuration.clusters = state.configuration.clusters.filter(cluster => cluster.clusterId !== state.selected.id);
            state.configuration.routes = state.configuration.routes.filter(route => route.clusterId !== state.selected.id);
        }

        state.selected = null;
        render();

        await saveAndApplyConfiguration(deletingRoute ? "Route deleted." : "Cluster deleted.", null, true);
    } catch (error) {
        state.configuration = previousConfiguration;
        captureSavedDraftSnapshots();
        state.selected = previousSelection;
        render();
        setStatus(`Could not delete ${deletingRoute ? "route" : "cluster"}: ${error.message}`);
    } finally {
        deleteButton.disabled = false;
    }
});

document.getElementById("export-config").addEventListener("click", exportConfiguration);
document.getElementById("import-config").addEventListener("click", () => document.getElementById("import-file").click());
document.getElementById("import-file").addEventListener("change", importConfiguration);
elements.triggerUpdate.addEventListener("click", triggerSelfUpdate);
elements.tabDashboard.addEventListener("click", () => setActiveView("dashboard"));
elements.tabEmpty.addEventListener("click", () => setActiveView("empty"));
elements.telemetryRefresh.addEventListener("click", () => loadTelemetry({ force: true }));
elements.telemetrySmoke.addEventListener("click", runTelemetrySmokeTest);
elements.telemetryHours.addEventListener("change", () => {
    state.ui.telemetry.hours = Number.parseInt(elements.telemetryHours.value || "24", 10);
    state.ui.telemetry.page = 1;
    persistUiState();
    loadTelemetry({ force: true });
});
elements.telemetryRiskFilter.addEventListener("change", () => {
    state.ui.telemetry.riskLevel = elements.telemetryRiskFilter.value;
    state.ui.telemetry.page = 1;
    persistUiState();
    loadTelemetry({ force: true });
});
elements.telemetryCategoryFilter.addEventListener("change", () => {
    state.ui.telemetry.category = elements.telemetryCategoryFilter.value;
    state.ui.telemetry.page = 1;
    persistUiState();
    loadTelemetry({ force: true });
});
elements.telemetryPrevPage.addEventListener("click", () => {
    if (state.ui.telemetry.page <= 1) {
        return;
    }

    state.ui.telemetry.page -= 1;
    persistUiState();
    loadTelemetry({ force: true });
});
elements.telemetryNextPage.addEventListener("click", () => {
    const totalPages = Math.max(1, Math.ceil((state.telemetry.events.totalCount || 0) / state.ui.telemetry.pageSize));
    if (state.ui.telemetry.page >= totalPages) {
        return;
    }

    state.ui.telemetry.page += 1;
    persistUiState();
    loadTelemetry({ force: true });
});
document.getElementById("add-destination").addEventListener("click", () => {
    const cluster = getSelectedCluster();
    if (!cluster) {
        return;
    }

    cluster.destinations.push({
        destinationId: buildDefaultDestinationId(cluster),
        address: "",
    });
    renderDestinationList(cluster);
    renderDraftState();
});

document.getElementById("certificate-form").addEventListener("submit", uploadCertificate);
document.getElementById("route-editor").addEventListener("submit", event => saveSelectedConfiguration(event, "route"));
document.getElementById("cluster-editor").addEventListener("submit", event => saveSelectedConfiguration(event, "cluster"));
elements.statusToast.addEventListener("mouseenter", pinStatusToast);
elements.statusToast.addEventListener("click", hideStatusToast);

bindRouteEditor();
bindClusterEditor();
bindFilters();

loadConfiguration();

async function loadConfiguration(selectionToRestore = null) {
    try {
        const response = await fetch("/api/admin/configuration");
        if (!response.ok) {
            const text = await response.text();
            let message = text;
            try { message = JSON.parse(text)?.error || text; } catch { /* ignore */ }
            setStatus(`Could not load configuration: ${message}`);
            return;
        }
        state.configuration = await response.json();
        captureSavedDraftSnapshots();
        state.selected = selectionToRestore;

        if (state.selected?.type === "route" && !getSelectedRoute()) {
            state.selected = null;
        }

        if (state.selected?.type === "cluster" && !getSelectedCluster()) {
            state.selected = null;
        }

        render();

        if (!state.telemetry.lastLoadedUtc || state.activeView === "empty") {
            await loadTelemetry();
        }
    } catch (err) {
        setStatus(`Could not load configuration: ${err.message}`);
    }
}

async function loadTelemetry({ force = false } = {}) {
    const telemetryEnabled = !!state.configuration.settings?.telemetryEnabled;
    if (!telemetryEnabled) {
        state.telemetry.summary = null;
        state.telemetry.events = { page: 1, pageSize: state.ui.telemetry.pageSize, totalCount: 0, events: [] };
        state.telemetry.topSources = [];
        state.telemetry.topTargets = [];
        state.telemetry.trends = [];
        state.telemetry.lastLoadedUtc = null;
        renderTelemetry();
        return;
    }

    const stale = !state.telemetry.lastLoadedUtc || (Date.now() - state.telemetry.lastLoadedUtc) > 15000;
    if (!force && !stale) {
        renderTelemetry();
        return;
    }

    state.telemetry.isLoading = true;
    state.telemetry.error = "";
    renderTelemetry();

    const hours = state.ui.telemetry.hours;
    const page = state.ui.telemetry.page;
    const pageSize = state.ui.telemetry.pageSize;
    const riskLevel = encodeURIComponent(state.ui.telemetry.riskLevel);
    const category = encodeURIComponent(state.ui.telemetry.category);

    try {
        const [summaryResponse, eventsResponse, topSourcesResponse, topTargetsResponse, trendResponse] = await Promise.all([
            fetch(`/api/admin/telemetry/summary?hours=${hours}`),
            fetch(`/api/admin/telemetry/events?hours=${hours}&page=${page}&pageSize=${pageSize}&riskLevel=${riskLevel}&category=${category}`),
            fetch(`/api/admin/telemetry/top-sources?hours=${hours}&limit=6`),
            fetch(`/api/admin/telemetry/top-targets?hours=${hours}&limit=6`),
            fetch(`/api/admin/telemetry/trends?hours=${hours}&bucketMinutes=5`),
        ]);

        state.telemetry.summary = await summaryResponse.json();
        state.telemetry.events = await eventsResponse.json();
        state.telemetry.topSources = await topSourcesResponse.json();
        state.telemetry.topTargets = await topTargetsResponse.json();
        state.telemetry.trends = await trendResponse.json();
        state.telemetry.lastLoadedUtc = Date.now();
    } catch {
        state.telemetry.error = "Could not load telemetry data from the admin API.";
        setStatus(state.telemetry.error);
    } finally {
        state.telemetry.isLoading = false;
        renderTelemetry();
        renderHeaderState();
    }
}

async function runTelemetrySmokeTest() {
    const settings = state.configuration.settings || {};
    const smokePath = settings.telemetrySmokePath || "/__helgrind/telemetry/smoke";
    const publicEndpoint = settings.publicHttpsEndpointDisplay || `https://localhost:${settings.publicHttpsPort ?? 443}`;

    try {
        await fetch(`${publicEndpoint}${smokePath}`, { method: "GET" });
        setStatus("Telemetry smoke test sent to the public listener.");
    } catch {
        setStatus("Telemetry smoke test sent. The public listener may reject the browser fetch, but the request should still be recorded.");
    }

    window.setTimeout(() => {
        loadTelemetry({ force: true });
    }, 800);
}

async function saveConfiguration(allowEmpty = false) {
    state.configuration = sanitizeConfigurationForSave(state.configuration);

    const response = await fetch(`/api/admin/configuration${allowEmpty ? "?allowEmpty=true" : ""}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(state.configuration),
    });

    if (!response.ok) {
        const text = await response.text();
        let message = text;
        try { message = JSON.parse(text)?.error || text; } catch { /* ignore */ }
        throw new Error(message || "Could not save configuration.");
    }

    state.configuration = await response.json();
    captureSavedDraftSnapshots();
    render();
}

async function saveAndApplyConfiguration(statusPrefix, selectionToRestore = state.selected ? { ...state.selected } : null, allowEmpty = false) {
    await saveConfiguration(allowEmpty);

    const response = await fetch("/api/admin/apply", { method: "POST" });
    const result = await response.json();
    const message = result.validationErrors?.length
        ? `${statusPrefix} ${result.statusMessage} ${result.validationErrors.join(" ")}`
        : `${statusPrefix} ${result.statusMessage}`;
    setStatus(message);
    await loadConfiguration(selectionToRestore);
}

async function saveSelectedConfiguration(event, kind) {
    event.preventDefault();
    const selection = state.selected ? { ...state.selected } : null;
    const label = kind === "route" ? "Route saved." : "Cluster saved.";

    try {
        await saveAndApplyConfiguration(label, selection);
    } catch (error) {
        setStatus(`Could not save ${kind}: ${error.message}`);
    }
}

async function exportConfiguration() {
    const response = await fetch("/api/admin/export");
    const payload = await response.json();
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `helgrind-export-${new Date().toISOString().replaceAll(":", "-")}.json`;
    anchor.click();
    URL.revokeObjectURL(url);
    setStatus("Exported current draft as JSON.");
}

async function importConfiguration(event) {
    const file = event.target.files[0];
    if (!file) {
        return;
    }

    const payload = JSON.parse(await file.text());
    const response = await fetch("/api/admin/import", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
    });
    const result = await response.json();
    setStatus(result.statusMessage);
    event.target.value = "";
    await loadConfiguration();
}

async function uploadCertificate(event) {
    event.preventDefault();

    const pemFile = document.getElementById("certificate-pem").files[0];
    const keyFile = document.getElementById("certificate-key").files[0];
    if (!pemFile || !keyFile) {
        setStatus("Choose both a PEM and a key file before uploading.");
        return;
    }

    const formData = new FormData();
    formData.append("displayName", document.getElementById("certificate-name").value);
    formData.append("pemFile", pemFile);
    formData.append("keyFile", keyFile);

    try {
        const response = await fetch("/api/admin/certificate", {
            method: "POST",
            body: formData,
        });
        const result = await response.json();
        setStatus(result.statusMessage || (response.ok ? "Certificate uploaded." : "Certificate upload failed."));
        event.target.reset();
        document.getElementById("certificate-menu")?.removeAttribute("open");
    } catch (err) {
        setStatus(`Certificate upload failed: ${err.message}`);
    }
    await loadConfiguration();
}

async function triggerSelfUpdate() {
    if (!state.configuration.settings?.selfUpdateEnabled) {
        setStatus("Self-update is not configured for this Helgrind instance.");
        return;
    }

    if (!window.confirm("Start the Helgrind update command now? The admin UI will disconnect while the process restarts.")) {
        return;
    }

    elements.triggerUpdate.disabled = true;

    try {
        const response = await fetch("/api/admin/update", { method: "POST" });
        const result = await response.json();
        setStatus(result.statusMessage);
    } catch {
        setStatus("Helgrind started the update command and is restarting.");
    }
}

function bindRouteEditor() {
    ["route-id", "route-cluster-id", "route-path", "route-hosts", "route-order"].forEach(id => {
        document.getElementById(id).addEventListener("input", () => {
            const route = getSelectedRoute();
            if (!route) {
                return;
            }

            route.routeId = document.getElementById("route-id").value.trim();
            route.clusterId = document.getElementById("route-cluster-id").value.trim();
            route.path = document.getElementById("route-path").value.trim() || "{**catch-all}";
            route.hosts = document.getElementById("route-hosts").value
                .split(/\r?\n|,/)
                .map(host => host.trim())
                .filter(Boolean);
            route.order = parseIntegerOrDefault(document.getElementById("route-order").value, 0);
            state.selected.id = route.routeId;
            renderDraftState();
        });
    });
}

function bindClusterEditor() {
    [
        "cluster-id",
        "cluster-load-balancing",
        "health-enabled",
        "health-interval",
        "health-timeout",
        "health-policy",
        "health-path",
        "health-query",
        "health-threshold",
    ].forEach(id => {
        document.getElementById(id).addEventListener(id === "health-enabled" ? "change" : "input", () => {
            const cluster = getSelectedCluster();
            if (!cluster) {
                return;
            }

            cluster.clusterId = document.getElementById("cluster-id").value.trim();
            cluster.loadBalancingPolicy = document.getElementById("cluster-load-balancing").value.trim();
            cluster.healthCheck.enabled = document.getElementById("health-enabled").checked;
            cluster.healthCheck.interval = document.getElementById("health-interval").value.trim();
            cluster.healthCheck.timeout = document.getElementById("health-timeout").value.trim();
            cluster.healthCheck.policy = document.getElementById("health-policy").value.trim();
            cluster.healthCheck.path = document.getElementById("health-path").value.trim();
            cluster.healthCheck.query = document.getElementById("health-query").value.trim();
            const thresholdValue = document.getElementById("health-threshold").value.trim();
            cluster.consecutiveFailuresThreshold = thresholdValue ? Number.parseInt(thresholdValue, 10) : null;
            state.selected.id = cluster.clusterId;
            updateHealthCheckVisibility(cluster.healthCheck.enabled);
            renderDraftState();
        });
    });
}

function render() {
    renderViewTabs();
    renderHeaderState();
    renderFilterState();
    renderListsOnly();
    renderEditor();
    renderSettings();
    renderTelemetry();
}

function renderHeaderState() {
    if (state.activeView === "empty") {
        elements.headerSelection.classList.remove("unsaved");
        const summary = state.telemetry.summary;
        if (!state.configuration.settings?.telemetryEnabled) {
            elements.headerSelection.textContent = "Telemetry is disabled for this Helgrind instance.";
        } else if (summary) {
            elements.headerSelection.textContent = `${summary.eventCount} suspicious event(s) in the last ${summary.windowHours}h | ${summary.highRiskEventCount} high risk | ${summary.uniqueSourceCount} source(s)`;
        } else {
            elements.headerSelection.textContent = "Loading telemetry from the public listener.";
        }
        return;
    }

    const route = getSelectedRoute();
    const cluster = getSelectedCluster();
    const settings = state.configuration.settings || {};
    const selectedDirty = route ? isDraftDirty("route", route) : cluster ? isDraftDirty("cluster", cluster) : false;
    const dirtyRouteCount = getDirtyDraftCount("route");
    const dirtyClusterCount = getDirtyDraftCount("cluster");

    if (route) {
        elements.headerSelection.textContent = `Route ${route.routeId || "untitled"} -> ${route.clusterId || "no cluster"} | ${route.hosts.join(", ") || "no hosts"}${selectedDirty ? " | unsaved" : ""}`;
    } else if (cluster) {
        elements.headerSelection.textContent = `Cluster ${cluster.clusterId || "untitled"} | ${cluster.destinations.length} destination(s) | health ${cluster.healthCheck.enabled ? "enabled" : "disabled"}${selectedDirty ? " | unsaved" : ""}`;
    } else {
        const dirtySummary = buildDirtySummary(dirtyRouteCount, dirtyClusterCount);
        elements.headerSelection.textContent = `${settings.publicHttpsEndpointDisplay || "Public proxy"} | ${settings.adminHttpsEndpointDisplay || "Admin dashboard"}${dirtySummary ? ` | ${dirtySummary}` : ""}`;
    }

    elements.headerSelection.classList.toggle("unsaved", selectedDirty || dirtyRouteCount > 0 || dirtyClusterCount > 0);

    elements.dashboardStatus.classList.remove("live", "reconnecting", "error");
    if (settings.usingFallbackCertificate) {
        elements.dashboardStatus.classList.add("error");
        elements.dashboardStatusLabel.textContent = "TEMP CERT";
        return;
    }

    if (settings.certificateRestartRequired) {
        elements.dashboardStatus.classList.add("reconnecting");
        elements.dashboardStatusLabel.textContent = "RESTART NEEDED";
        return;
    }

    elements.dashboardStatus.classList.add("live");
    elements.dashboardStatusLabel.textContent = "LAN ONLY";
}

function renderListsOnly() {
    const visibleRoutes = getFilteredRoutes();
    const visibleClusters = getFilteredClusters();
    const dirtyRouteCount = getDirtyDraftCount("route");
    const dirtyClusterCount = getDirtyDraftCount("cluster");

    renderList(elements.routeList, visibleRoutes, "route", route => ({
        title: route.routeId || "Untitled route",
        subtitle: `${route.hosts.join(", ") || "No hosts"}`,
        meta: `Cluster ${route.clusterId || "none"} | Path ${route.path || "{**catch-all}"}`,
        count: route.hosts.length,
        dirty: isDraftDirty("route", route),
    }));
    renderList(elements.clusterList, visibleClusters, "cluster", cluster => ({
        title: cluster.clusterId || "Untitled cluster",
        subtitle: `${cluster.destinations.length} destination(s)`,
        meta: cluster.healthCheck.enabled ? `Health ${cluster.healthCheck.path || "/"}` : "Health disabled",
        count: cluster.destinations.length,
        dirty: isDraftDirty("cluster", cluster),
    }));

    elements.routeVisibleCount.textContent = `${visibleRoutes.length} / ${state.configuration.routes.length} shown${dirtyRouteCount ? ` | ${dirtyRouteCount} unsaved` : ""}`;
    elements.clusterVisibleCount.textContent = `${visibleClusters.length} / ${state.configuration.clusters.length} shown${dirtyClusterCount ? ` | ${dirtyClusterCount} unsaved` : ""}`;
}

function renderList(container, items, type, formatter) {
    container.innerHTML = "";
    if (!items.length) {
        const empty = document.createElement("div");
        empty.className = "list-empty";
        empty.textContent = "No items match the active filters.";
        container.appendChild(empty);
        return;
    }

    items.forEach(item => {
        const id = type === "route" ? item.routeId : item.clusterId;
        const entry = formatter(item);
        const button = document.createElement("button");
        button.type = "button";
        button.className = `item-card ${state.selected?.type === type && state.selected?.id === id ? "selected" : ""} ${entry.dirty ? "unsaved" : ""}`;
        button.innerHTML = `
            <div class="item-main">
                <div class="item-title-row">
                    <strong>${escapeHtml(entry.title)}</strong>
                    ${entry.dirty ? '<span class="dirty-badge">Unsaved</span>' : ""}
                </div>
                <span>${escapeHtml(entry.subtitle)}</span>
                <span class="item-meta">${escapeHtml(entry.meta || "")}</span>
            </div>
            <div class="item-right">
                <span class="count">${escapeHtml(String(entry.count ?? ""))}</span>
            </div>`;
        button.addEventListener("click", () => {
            selectItem(type, id);
            render();
        });
        container.appendChild(button);
    });
}

function renderEditor() {
    const route = getSelectedRoute();
    const cluster = getSelectedCluster();
    elements.emptyState.classList.toggle("hidden", !!route || !!cluster);
    elements.routeEditor.classList.toggle("hidden", !route);
    elements.clusterEditor.classList.toggle("hidden", !cluster);

    if (route) {
        elements.editorTitle.textContent = `Route: ${route.routeId || "Untitled"}${isDraftDirty("route", route) ? " · Unsaved" : ""}`;
        document.getElementById("route-id").value = route.routeId;
        document.getElementById("route-cluster-id").value = route.clusterId;
        document.getElementById("route-path").value = route.path;
        document.getElementById("route-hosts").value = route.hosts.join("\n");
        document.getElementById("route-order").value = route.order;
        updateEditorDraftState();
        return;
    }

    if (cluster) {
        elements.editorTitle.textContent = `Cluster: ${cluster.clusterId || "Untitled"}${isDraftDirty("cluster", cluster) ? " · Unsaved" : ""}`;
        document.getElementById("cluster-id").value = cluster.clusterId;
        document.getElementById("cluster-load-balancing").value = cluster.loadBalancingPolicy || "";
        document.getElementById("health-enabled").checked = !!cluster.healthCheck.enabled;
        document.getElementById("health-interval").value = cluster.healthCheck.interval || "00:00:10";
        document.getElementById("health-timeout").value = cluster.healthCheck.timeout || "00:00:03";
        document.getElementById("health-policy").value = cluster.healthCheck.policy || "ConsecutiveFailures";
        document.getElementById("health-path").value = cluster.healthCheck.path || "";
        document.getElementById("health-query").value = cluster.healthCheck.query || "";
        document.getElementById("health-threshold").value = cluster.consecutiveFailuresThreshold ?? "";
        updateHealthCheckVisibility(cluster.healthCheck.enabled);
        renderDestinationList(cluster);
        updateEditorDraftState();
        return;
    }

    elements.editorTitle.textContent = "Select a route or cluster";
    elements.destinationList.innerHTML = "";
    updateHealthCheckVisibility(false);
    updateEditorDraftState();
}

function updateHealthCheckVisibility(isEnabled) {
    elements.healthCheckDetailFields.forEach(field => {
        field.classList.toggle("hidden", !isEnabled);
    });
}

function renderDestinationList(cluster) {
    elements.destinationList.innerHTML = "";
    cluster.destinations.forEach((destination, index) => {
        const row = elements.template.content.firstElementChild.cloneNode(true);
        const idInput = row.querySelector(".destination-id");
        const addressInput = row.querySelector(".destination-address");
        idInput.value = destination.destinationId;
        addressInput.value = destination.address;

        idInput.addEventListener("input", () => {
            destination.destinationId = idInput.value.trim();
            renderDraftState();
        });

        addressInput.addEventListener("input", () => {
            destination.address = addressInput.value.trim();
            renderDraftState();
        });

        row.querySelector(".remove-destination").addEventListener("click", () => {
            cluster.destinations.splice(index, 1);
            renderDestinationList(cluster);
            renderDraftState();
        });

        elements.destinationList.appendChild(row);
    });
}

function renderSettings() {
    const settings = state.configuration.settings || {};
    elements.publicHttpsEndpoint.textContent = settings.publicHttpsEndpointDisplay || `https://localhost:${settings.publicHttpsPort ?? 443}`;
    elements.environmentName.textContent = settings.environmentName || "Unknown";
    elements.headerVersion.textContent = settings.runtimeVersionDisplay || "";
    elements.headerVersion.title = settings.runtimeVersionDetails || settings.runtimeVersionDisplay || "";
    elements.headerVersion.classList.toggle("hidden", !settings.runtimeVersionDisplay);
    elements.certificateState.textContent = settings.usingFallbackCertificate
        ? "Temporary certificate"
        : settings.activeCertificate?.displayName || "Uploaded certificate";
    elements.lastApplied.textContent = settings.lastAppliedUtc ? new Date(settings.lastAppliedUtc).toLocaleString() : "Never";
    elements.certificateDescription.textContent = settings.certificateStatus || "Upload a PEM fullchain and key to replace the active TLS certificate.";
    elements.restartBanner.classList.toggle("hidden", !settings.certificateRestartRequired);
    elements.restartHint.textContent = settings.restartHint || "Restart the process after replacing the certificate so Kestrel loads the stored PEM and key.";
    elements.triggerUpdate.classList.toggle("hidden", !settings.selfUpdateEnabled);
    elements.triggerUpdate.disabled = !settings.selfUpdateEnabled;
    elements.triggerUpdate.textContent = settings.selfUpdateButtonLabel || "Update Helgrind";
    elements.triggerUpdate.title = settings.selfUpdateStatus || "";
}

function renderTelemetry() {
    const settings = state.configuration.settings || {};
    const telemetryEnabled = !!settings.telemetryEnabled;
    const summary = state.telemetry.summary;
    const events = state.telemetry.events;
    const totalPages = Math.max(1, Math.ceil((events.totalCount || 0) / state.ui.telemetry.pageSize));

    elements.telemetryHours.value = String(state.ui.telemetry.hours);
    elements.telemetryRiskFilter.value = state.ui.telemetry.riskLevel;
    elements.telemetryCategoryFilter.value = state.ui.telemetry.category;
    elements.telemetryGraphCopy.textContent = `Suspicious requests per 5 minutes across the last ${state.ui.telemetry.hours} hour(s).`;
    elements.telemetryRefresh.disabled = !telemetryEnabled || state.telemetry.isLoading;
    elements.telemetrySmoke.disabled = !telemetryEnabled;
    elements.telemetryPrevPage.disabled = state.telemetry.isLoading || state.ui.telemetry.page <= 1;
    elements.telemetryNextPage.disabled = state.telemetry.isLoading || state.ui.telemetry.page >= totalPages;
    elements.telemetryPageLabel.textContent = `Page ${state.ui.telemetry.page} of ${totalPages}`;
    elements.telemetrySubtitle.textContent = telemetryEnabled
        ? `Suspicious public traffic only. Retention: ${settings.telemetryRetentionDays || 30} days.`
        : "Telemetry is disabled in configuration.";

    if (!telemetryEnabled) {
        elements.telemetryEventCount.textContent = "Off";
        elements.telemetryHighRiskCount.textContent = "Off";
        elements.telemetrySourceCount.textContent = "Off";
        elements.telemetryLatestEvent.textContent = "Disabled";
        elements.telemetryAlertState.textContent = "Enable telemetry in configuration to populate this view.";
        elements.telemetryLastUpdated.textContent = "Telemetry is disabled.";
        renderTelemetryList(elements.telemetryTopSources, []);
        renderTelemetryList(elements.telemetryTopTargets, []);
        renderTrend([]);
        renderTelemetryEvents([]);
        return;
    }

    elements.telemetryEventCount.textContent = summary ? String(summary.eventCount) : state.telemetry.isLoading ? "..." : "0";
    elements.telemetryHighRiskCount.textContent = summary ? String(summary.highRiskEventCount) : state.telemetry.isLoading ? "..." : "0";
    elements.telemetrySourceCount.textContent = summary ? String(summary.uniqueSourceCount) : state.telemetry.isLoading ? "..." : "0";
    elements.telemetryLatestEvent.textContent = formatTimestamp(summary?.latestEventUtc, true) || (state.telemetry.isLoading ? "Loading" : "Never");
    elements.telemetryAlertState.textContent = settings.telemetryAlertingEnabled
        ? buildAlertSummary(summary)
        : "Webhook alerts inactive.";
    elements.telemetryLastUpdated.textContent = state.telemetry.error
        ? state.telemetry.error
        : state.telemetry.isLoading
            ? "Refreshing telemetry..."
            : `Updated ${formatRelativeTime(state.telemetry.lastLoadedUtc)}. Filters: ${state.ui.telemetry.riskLevel}/${state.ui.telemetry.category}.`;

    renderTelemetryEvents(events.events || []);
    renderTelemetryList(elements.telemetryTopSources, state.telemetry.topSources, renderTopSourceItem);
    renderTelemetryList(elements.telemetryTopTargets, state.telemetry.topTargets, renderTopTargetItem);
    renderTrend(state.telemetry.trends || []);
}

function renderTelemetryEvents(events) {
    elements.telemetryEventsBody.innerHTML = "";
    if (!events.length) {
        const row = document.createElement("tr");
        row.innerHTML = `<td colspan="5" class="telemetry-empty-row">No suspicious events in this window.</td>`;
        elements.telemetryEventsBody.appendChild(row);
        return;
    }

    events.forEach(event => {
        const row = document.createElement("tr");
        const occurredAt = formatTimestamp(event.occurredUtc, false) || "Unknown";
        const riskLevel = event.riskLevel || "Low";
        const remoteAddress = event.remoteAddress || "unknown";
        const sourceSummary = `${event.method || "GET"} ${String(event.statusCode ?? "")}`.trim();
        const host = event.host || "unknown";
        const path = event.path || "/";
        const category = event.category || "Unknown";
        const reason = event.reason || "";
        row.innerHTML = `
            <td><span class="telemetry-time" title="${escapeHtml(occurredAt)}">${escapeHtml(occurredAt)}</span></td>
            <td><span class="telemetry-risk telemetry-risk-${escapeHtml(riskLevel.toLowerCase())}">${escapeHtml(riskLevel)}</span></td>
            <td>
                <strong class="telemetry-cell-primary" title="${escapeHtml(remoteAddress)}">${escapeHtml(remoteAddress)}</strong>
                <span class="telemetry-cell-secondary" title="${escapeHtml(sourceSummary)}">${escapeHtml(sourceSummary)}</span>
            </td>
            <td>
                <strong class="telemetry-cell-primary" title="${escapeHtml(host)}">${escapeHtml(host)}</strong>
                <span class="telemetry-cell-secondary" title="${escapeHtml(path)}">${escapeHtml(path)}</span>
            </td>
            <td>
                <strong class="telemetry-cell-primary" title="${escapeHtml(category)}">${escapeHtml(category)}</strong>
                <span class="telemetry-cell-secondary" title="${escapeHtml(reason)}">${escapeHtml(reason)}</span>
            </td>`;
        elements.telemetryEventsBody.appendChild(row);
    });
}

function renderTelemetryList(container, items, formatter = value => value) {
    container.innerHTML = "";
    if (!items.length) {
        const empty = document.createElement("div");
        empty.className = "list-empty telemetry-list-empty";
        empty.textContent = "No telemetry data yet.";
        container.appendChild(empty);
        return;
    }

    items.forEach(item => {
        container.appendChild(formatter(item));
    });
}

function renderTopSourceItem(item) {
    const element = document.createElement("div");
    element.className = "telemetry-list-item";
    element.innerHTML = `
        <div>
            <strong>${escapeHtml(item.remoteAddress || "unknown")}</strong>
            <span>${escapeHtml(item.highestRiskLevel || "Low")} risk | last ${escapeHtml(formatTimestamp(item.lastSeenUtc, false) || "unknown")}</span>
        </div>
        <span class="count">${escapeHtml(String(item.eventCount || 0))}</span>`;
    return element;
}

function renderTopTargetItem(item) {
    const element = document.createElement("div");
    element.className = "telemetry-list-item";
    element.innerHTML = `
        <div>
            <strong>${escapeHtml(item.path || "/")}</strong>
            <span>${escapeHtml(item.host || "unknown")} | ${escapeHtml(item.highestRiskLevel || "Low")} risk</span>
        </div>
        <span class="count">${escapeHtml(String(item.eventCount || 0))}</span>`;
    return element;
}

function renderTrend(buckets) {
    elements.telemetryTrend.innerHTML = "";
    if (!buckets.length) {
        const empty = document.createElement("div");
        empty.className = "list-empty telemetry-list-empty";
        empty.textContent = "No 5-minute request data yet.";
        elements.telemetryTrend.appendChild(empty);
        return;
    }

    const width = 560;
    const height = 220;
    const padding = { top: 16, right: 16, bottom: 34, left: 36 };
    const maxCount = Math.max(...buckets.map(bucket => bucket.eventCount || 0), 1);
    const usableWidth = width - padding.left - padding.right;
    const usableHeight = height - padding.top - padding.bottom;
    const getX = index => padding.left + ((usableWidth / Math.max(1, buckets.length - 1)) * index);
    const getY = count => padding.top + usableHeight - ((count / maxCount) * usableHeight);

    const points = buckets
        .map((bucket, index) => `${getX(index).toFixed(2)},${getY(bucket.eventCount || 0).toFixed(2)}`)
        .join(" ");
    const areaPoints = `${padding.left},${height - padding.bottom} ${points} ${getX(buckets.length - 1).toFixed(2)},${height - padding.bottom}`;

    const shell = document.createElement("div");
    shell.className = "telemetry-graph";

    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
    svg.setAttribute("class", "telemetry-graph-svg");
    svg.setAttribute("role", "img");
    svg.setAttribute("aria-label", "Suspicious requests per 5 minutes graph");

    [0, 0.5, 1].forEach(step => {
        const y = padding.top + (usableHeight * step);
        const line = document.createElementNS("http://www.w3.org/2000/svg", "line");
        line.setAttribute("x1", String(padding.left));
        line.setAttribute("x2", String(width - padding.right));
        line.setAttribute("y1", y.toFixed(2));
        line.setAttribute("y2", y.toFixed(2));
        line.setAttribute("class", "telemetry-grid-line");
        svg.appendChild(line);
    });

    const area = document.createElementNS("http://www.w3.org/2000/svg", "polygon");
    area.setAttribute("points", areaPoints);
    area.setAttribute("class", "telemetry-area-fill");
    svg.appendChild(area);

    const polyline = document.createElementNS("http://www.w3.org/2000/svg", "polyline");
    polyline.setAttribute("points", points);
    polyline.setAttribute("class", "telemetry-line");
    svg.appendChild(polyline);

    buckets.forEach((bucket, index) => {
        const circle = document.createElementNS("http://www.w3.org/2000/svg", "circle");
        circle.setAttribute("cx", getX(index).toFixed(2));
        circle.setAttribute("cy", getY(bucket.eventCount || 0).toFixed(2));
        circle.setAttribute("r", "3");
        circle.setAttribute("class", bucket.highRiskEventCount ? "telemetry-point high-risk" : "telemetry-point");

        const title = document.createElementNS("http://www.w3.org/2000/svg", "title");
        title.textContent = `${formatBucketLabel(bucket.bucketUtc)}: ${bucket.eventCount || 0} requests, ${bucket.highRiskEventCount || 0} high risk`;
        circle.appendChild(title);
        svg.appendChild(circle);
    });

    shell.appendChild(svg);

    const labels = document.createElement("div");
    labels.className = "telemetry-graph-labels";
    labels.innerHTML = `
        <span>${escapeHtml(formatBucketLabel(buckets[0].bucketUtc))}</span>
        <span>Peak ${escapeHtml(String(maxCount))}</span>
        <span>${escapeHtml(formatBucketLabel(buckets[buckets.length - 1].bucketUtc))}</span>`;
    shell.appendChild(labels);

    const summary = document.createElement("div");
    summary.className = "telemetry-graph-summary";
    const totalRequests = buckets.reduce((sum, bucket) => sum + (bucket.eventCount || 0), 0);
    const totalHighRisk = buckets.reduce((sum, bucket) => sum + (bucket.highRiskEventCount || 0), 0);
    summary.innerHTML = `
        <span><strong>${escapeHtml(String(totalRequests))}</strong> suspicious requests</span>
        <span><strong>${escapeHtml(String(totalHighRisk))}</strong> high risk</span>
        <span><strong>${escapeHtml(String(buckets.length))}</strong> five-minute buckets</span>`;
    shell.appendChild(summary);

    elements.telemetryTrend.appendChild(shell);
}

function renderViewTabs() {
    const dashboardActive = state.activeView === "dashboard";
    elements.tabDashboard.classList.toggle("active", dashboardActive);
    elements.tabEmpty.classList.toggle("active", !dashboardActive);
    elements.tabDashboard.setAttribute("aria-selected", String(dashboardActive));
    elements.tabEmpty.setAttribute("aria-selected", String(!dashboardActive));
    elements.dashboardView.classList.toggle("hidden", !dashboardActive);
    elements.emptyView.classList.toggle("hidden", dashboardActive);
}

function renderFilterState() {
    elements.routeFiltersPanel.classList.toggle("hidden", !state.ui.routeFilters.expanded);
    elements.clusterFiltersPanel.classList.toggle("hidden", !state.ui.clusterFilters.expanded);
    elements.routeFiltersToggle.setAttribute("aria-expanded", String(state.ui.routeFilters.expanded));
    elements.clusterFiltersToggle.setAttribute("aria-expanded", String(state.ui.clusterFilters.expanded));
    elements.routeFiltersToggle.classList.toggle("active", state.ui.routeFilters.expanded);
    elements.clusterFiltersToggle.classList.toggle("active", state.ui.clusterFilters.expanded);

    elements.routeSearch.value = state.ui.routeFilters.search;
    elements.clusterSearch.value = state.ui.clusterFilters.search;
    elements.routeFilterLinked.checked = state.ui.routeFilters.selectedClusterOnly;
    elements.routeFilterHosts.checked = state.ui.routeFilters.hostsOnly;
    elements.clusterFilterReferenced.checked = state.ui.clusterFilters.referencedOnly;
    elements.clusterFilterHealth.checked = state.ui.clusterFilters.healthEnabledOnly;
}

function selectItem(type, id) {
    state.selected = { type, id };
}

function renderDraftState() {
    renderHeaderState();
    renderListsOnly();
    updateEditorDraftState();
}

function setActiveView(view) {
    state.activeView = view;
    renderViewTabs();
    renderHeaderState();
    if (view === "empty") {
        ensureTelemetryAutoRefresh();
        loadTelemetry();
        return;
    }

    stopTelemetryAutoRefresh();
}

function ensureDraftKey(kind, item) {
    const keyStore = draftKeyStore[kind];
    if (keyStore.has(item)) {
        return keyStore.get(item);
    }

    const key = `${kind}:${nextDraftKey++}`;
    keyStore.set(item, key);
    return key;
}

function captureSavedDraftSnapshots() {
    savedDraftSnapshots.route.clear();
    savedDraftSnapshots.cluster.clear();
    deletedDraftKeys.route.clear();
    deletedDraftKeys.cluster.clear();

    state.configuration.routes.forEach(route => {
        savedDraftSnapshots.route.set(ensureDraftKey("route", route), snapshotRoute(route));
    });

    state.configuration.clusters.forEach(cluster => {
        savedDraftSnapshots.cluster.set(ensureDraftKey("cluster", cluster), snapshotCluster(cluster));
    });
}

function isDraftDirty(kind, item) {
    const key = ensureDraftKey(kind, item);
    const savedSnapshot = savedDraftSnapshots[kind].get(key);
    if (!savedSnapshot) {
        return true;
    }

    return (kind === "route" ? snapshotRoute(item) : snapshotCluster(item)) !== savedSnapshot;
}

function getDirtyDraftCount(kind) {
    const items = kind === "route" ? state.configuration.routes : state.configuration.clusters;
    return items.filter(item => isDraftDirty(kind, item)).length + deletedDraftKeys[kind].size;
}

function markDraftDeleted(kind, item) {
    const key = ensureDraftKey(kind, item);
    if (savedDraftSnapshots[kind].has(key)) {
        deletedDraftKeys[kind].add(key);
    }
}

function snapshotRoute(route) {
    return JSON.stringify({
        routeId: route.routeId || "",
        clusterId: route.clusterId || "",
        path: route.path || "{**catch-all}",
        hosts: [...(route.hosts || [])],
        order: Number.isFinite(route.order) ? route.order : 0,
    });
}

function snapshotCluster(cluster) {
    return JSON.stringify({
        clusterId: cluster.clusterId || "",
        loadBalancingPolicy: cluster.loadBalancingPolicy || "",
        healthCheck: {
            enabled: !!cluster.healthCheck?.enabled,
            interval: cluster.healthCheck?.interval || "",
            timeout: cluster.healthCheck?.timeout || "",
            policy: cluster.healthCheck?.policy || "",
            path: cluster.healthCheck?.path || "",
            query: cluster.healthCheck?.query || "",
        },
        consecutiveFailuresThreshold: cluster.consecutiveFailuresThreshold ?? null,
        destinations: (cluster.destinations || []).map(destination => ({
            destinationId: destination.destinationId || "",
            address: destination.address || "",
        })),
    });
}

function updateEditorDraftState() {
    const route = getSelectedRoute();
    const cluster = getSelectedCluster();
    const routeDirty = !!route && isDraftDirty("route", route);
    const clusterDirty = !!cluster && isDraftDirty("cluster", cluster);
    const routeSaveButton = document.getElementById("save-route");
    const clusterSaveButton = document.getElementById("save-cluster");

    elements.routeEditor.classList.toggle("editor-unsaved", routeDirty);
    elements.clusterEditor.classList.toggle("editor-unsaved", clusterDirty);
    routeSaveButton.classList.toggle("attention", routeDirty);
    clusterSaveButton.classList.toggle("attention", clusterDirty);
    routeSaveButton.textContent = routeDirty ? "Save Route Changes" : "Save Route";
    clusterSaveButton.textContent = clusterDirty ? "Save Cluster Changes" : "Save Cluster";
}

function buildDirtySummary(dirtyRouteCount, dirtyClusterCount) {
    const parts = [];

    if (dirtyRouteCount) {
        parts.push(`${dirtyRouteCount} unsaved route${dirtyRouteCount === 1 ? "" : "s"}`);
    }

    if (dirtyClusterCount) {
        parts.push(`${dirtyClusterCount} unsaved cluster${dirtyClusterCount === 1 ? "" : "s"}`);
    }

    return parts.join(" | ");
}

function buildDefaultDestinationId(cluster) {
    const clusterPrefix = (cluster?.clusterId || "cluster")
        .trim()
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "") || "cluster";
    const existingIds = new Set((cluster?.destinations || []).map(destination => (destination.destinationId || "").toLowerCase()));

    let nextNumber = Math.max(1, (cluster?.destinations?.length || 0) + 1);
    let candidate = `${clusterPrefix}-destination${nextNumber}`;

    while (existingIds.has(candidate.toLowerCase())) {
        nextNumber += 1;
        candidate = `${clusterPrefix}-destination${nextNumber}`;
    }

    return candidate;
}

function parseIntegerOrDefault(value, defaultValue) {
    const parsedValue = Number.parseInt(value ?? "", 10);
    return Number.isFinite(parsedValue) ? parsedValue : defaultValue;
}

function sanitizeConfigurationForSave(configuration) {
    const nextConfiguration = cloneConfiguration(configuration);

    nextConfiguration.routes = (nextConfiguration.routes || []).map(route => ({
        ...route,
        routeId: route.routeId || "",
        clusterId: route.clusterId || "",
        path: route.path || "{**catch-all}",
        hosts: Array.isArray(route.hosts) ? route.hosts : [],
        order: Number.isFinite(route.order) ? route.order : 0,
    }));

    nextConfiguration.clusters = (nextConfiguration.clusters || []).map(cluster => ({
        ...cluster,
        clusterId: cluster.clusterId || "",
        loadBalancingPolicy: cluster.loadBalancingPolicy || "",
        healthCheck: {
            enabled: !!cluster.healthCheck?.enabled,
            interval: cluster.healthCheck?.interval || "00:00:10",
            timeout: cluster.healthCheck?.timeout || "00:00:03",
            policy: cluster.healthCheck?.policy || "ConsecutiveFailures",
            path: cluster.healthCheck?.path || "",
            query: cluster.healthCheck?.query || "",
        },
        consecutiveFailuresThreshold: Number.isFinite(cluster.consecutiveFailuresThreshold)
            ? cluster.consecutiveFailuresThreshold
            : null,
        destinations: (cluster.destinations || []).map(destination => ({
            ...destination,
            destinationId: destination.destinationId || "",
            address: destination.address || "",
        })),
    }));

    nextConfiguration.settings ??= {};
    return nextConfiguration;
}

function cloneConfiguration(configuration) {
    if (typeof structuredClone === "function") {
        return structuredClone(configuration);
    }

    return JSON.parse(JSON.stringify(configuration));
}

function getSelectedRoute() {
    if (state.selected?.type !== "route") {
        return null;
    }

    return state.configuration.routes.find(route => route.routeId === state.selected.id) ?? null;
}

function getSelectedCluster() {
    if (state.selected?.type !== "cluster") {
        return null;
    }

    return state.configuration.clusters.find(cluster => cluster.clusterId === state.selected.id) ?? null;
}

function setStatus(message) {
    if (!message) {
        hideStatusToast();
        return;
    }

    const presentation = describeStatusToast(message);
    clearStatusToastTimer();
    statusToastPinned = false;
    elements.statusToast.dataset.tone = presentation.tone;
    elements.statusToast.classList.remove("hidden", "pinned");
    elements.statusToast.innerHTML = `
        <div class="feedback-toast-body">${escapeHtml(message)}</div>
        <div class="feedback-toast-progress"></div>`;
    scheduleStatusToastHide(getStatusToastDuration());
}

function pinStatusToast() {
    if (elements.statusToast.classList.contains("hidden") || statusToastPinned) {
        return;
    }

    statusToastPinned = true;
    clearStatusToastTimer();
    elements.statusToast.classList.add("pinned");
}

function hideStatusToast() {
    clearStatusToastTimer();
    statusToastPinned = false;
    elements.statusToast.classList.remove("pinned");
    elements.statusToast.classList.add("hidden");
    elements.statusToast.dataset.tone = "info";
    elements.statusToast.innerHTML = "";
}

function clearStatusToastTimer() {
    if (statusToastTimer !== null) {
        window.clearTimeout(statusToastTimer);
        statusToastTimer = null;
    }
}

function scheduleStatusToastHide(duration) {
    statusToastTimer = window.setTimeout(() => {
        if (!statusToastPinned) {
            hideStatusToast();
        }
    }, duration);
}

function getStatusToastDuration() {
    return 2000;
}

function describeStatusToast(message) {
    const normalized = message.toLowerCase();

    if (normalized.includes("could not") || normalized.includes("not configured") || normalized.includes("choose both")) {
        return { title: "Action Needs Attention", tone: "error" };
    }

    if (normalized.includes("validation") || normalized.includes("warning") || normalized.includes("restart") || normalized.includes("may reject")) {
        return { title: "Completed With Notes", tone: "warning" };
    }

    if (normalized.includes("saved") || normalized.includes("uploaded") || normalized.includes("exported") || normalized.includes("imported") || normalized.includes("started the update command") || normalized.includes("sent")) {
        return { title: "Action Completed", tone: "success" };
    }

    return { title: "Status Update", tone: "info" };
}

function bindFilters() {
    elements.routeFiltersToggle.addEventListener("click", () => {
        state.ui.routeFilters.expanded = !state.ui.routeFilters.expanded;
        persistUiState();
        renderFilterState();
    });

    elements.clusterFiltersToggle.addEventListener("click", () => {
        state.ui.clusterFilters.expanded = !state.ui.clusterFilters.expanded;
        persistUiState();
        renderFilterState();
    });

    elements.routeSearch.addEventListener("input", event => {
        state.ui.routeFilters.search = event.target.value;
        persistUiState();
        renderListsOnly();
    });

    elements.clusterSearch.addEventListener("input", event => {
        state.ui.clusterFilters.search = event.target.value;
        persistUiState();
        renderListsOnly();
    });

    elements.routeFilterLinked.addEventListener("change", event => {
        state.ui.routeFilters.selectedClusterOnly = event.target.checked;
        persistUiState();
        renderListsOnly();
    });

    elements.routeFilterHosts.addEventListener("change", event => {
        state.ui.routeFilters.hostsOnly = event.target.checked;
        persistUiState();
        renderListsOnly();
    });

    elements.clusterFilterReferenced.addEventListener("change", event => {
        state.ui.clusterFilters.referencedOnly = event.target.checked;
        persistUiState();
        renderListsOnly();
    });

    elements.clusterFilterHealth.addEventListener("change", event => {
        state.ui.clusterFilters.healthEnabledOnly = event.target.checked;
        persistUiState();
        renderListsOnly();
    });
}

function getFilteredRoutes() {
    const filters = state.ui.routeFilters;
    const selectedClusterId = getFocusedClusterId();
    const searchNeedle = filters.search.trim().toLowerCase();

    return state.configuration.routes.filter(route => {
        if (filters.selectedClusterOnly && selectedClusterId && route.clusterId !== selectedClusterId) {
            return false;
        }

        if (filters.hostsOnly && !route.hosts.length) {
            return false;
        }

        if (!searchNeedle) {
            return true;
        }

        const haystack = [route.routeId, route.clusterId, route.path, ...(route.hosts || [])]
            .filter(Boolean)
            .join(" ")
            .toLowerCase();
        return haystack.includes(searchNeedle);
    });
}

function getFilteredClusters() {
    const filters = state.ui.clusterFilters;
    const searchNeedle = filters.search.trim().toLowerCase();
    const referencedClusterIds = new Set(state.configuration.routes.map(route => route.clusterId).filter(Boolean));

    return state.configuration.clusters.filter(cluster => {
        if (filters.referencedOnly && !referencedClusterIds.has(cluster.clusterId)) {
            return false;
        }

        if (filters.healthEnabledOnly && !cluster.healthCheck.enabled) {
            return false;
        }

        if (!searchNeedle) {
            return true;
        }

        const haystack = [
            cluster.clusterId,
            cluster.loadBalancingPolicy,
            cluster.healthCheck.path,
            cluster.healthCheck.policy,
            ...cluster.destinations.map(destination => `${destination.destinationId} ${destination.address}`),
        ]
            .filter(Boolean)
            .join(" ")
            .toLowerCase();
        return haystack.includes(searchNeedle);
    });
}

function getFocusedClusterId() {
    if (state.selected?.type === "cluster") {
        return state.selected.id;
    }

    if (state.selected?.type === "route") {
        return getSelectedRoute()?.clusterId ?? null;
    }

    return null;
}

function persistUiState() {
    localStorage.setItem("helgrind-ui", JSON.stringify(state.ui));
}

function loadUiState() {
    try {
        const stored = JSON.parse(localStorage.getItem("helgrind-ui") || "null");
        return {
            routeFilters: {
                expanded: !!stored?.routeFilters?.expanded,
                search: stored?.routeFilters?.search || "",
                selectedClusterOnly: !!stored?.routeFilters?.selectedClusterOnly,
                hostsOnly: !!stored?.routeFilters?.hostsOnly,
            },
            clusterFilters: {
                expanded: !!stored?.clusterFilters?.expanded,
                search: stored?.clusterFilters?.search || "",
                referencedOnly: !!stored?.clusterFilters?.referencedOnly,
                healthEnabledOnly: !!stored?.clusterFilters?.healthEnabledOnly,
            },
            telemetry: {
                hours: Number.parseInt(stored?.telemetry?.hours || "24", 10) || 24,
                page: Number.parseInt(stored?.telemetry?.page || "1", 10) || 1,
                pageSize: 25,
                riskLevel: stored?.telemetry?.riskLevel || "All",
                category: stored?.telemetry?.category || "All",
            },
        };
    } catch {
        return {
            routeFilters: { expanded: false, search: "", selectedClusterOnly: false, hostsOnly: false },
            clusterFilters: { expanded: false, search: "", referencedOnly: false, healthEnabledOnly: false },
            telemetry: { hours: 24, page: 1, pageSize: 25, riskLevel: "All", category: "All" },
        };
    }
}

function ensureTelemetryAutoRefresh() {
    if (telemetryRefreshTimer !== null) {
        return;
    }

    telemetryRefreshTimer = window.setInterval(() => {
        if (document.hidden || state.activeView !== "empty") {
            return;
        }

        loadTelemetry();
    }, 15000);
}

function stopTelemetryAutoRefresh() {
    if (telemetryRefreshTimer === null) {
        return;
    }

    window.clearInterval(telemetryRefreshTimer);
    telemetryRefreshTimer = null;
}

function buildAlertSummary(summary) {
    if (!summary?.alertingConfigured) {
        return "Webhook alerts inactive.";
    }

    const fragments = [`Webhook armed for ${summary.alertMinimumRiskLevel || "High"} risk events.`];
    if (summary.lastAlertSentUtc) {
        fragments.push(`Last delivery ${formatRelativeTime(summary.lastAlertSentUtc)}.`);
    }

    if (summary.alertCooldownUntilUtc && new Date(summary.alertCooldownUntilUtc).getTime() > Date.now()) {
        fragments.push(`Cooldown until ${new Date(summary.alertCooldownUntilUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}.`);
    }

    if (summary.alertStatus) {
        fragments.push(summary.alertStatus);
    }

    return fragments.join(" ");
}

function formatTimestamp(value, allowRelativeFallback) {
    if (!value) {
        return "";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return "";
    }

    if (allowRelativeFallback) {
        return formatRelativeTime(date.getTime());
    }

    return date.toLocaleString();
}

function formatRelativeTime(value) {
    if (!value) {
        return "just now";
    }

    const timestamp = typeof value === "number" ? value : new Date(value).getTime();
    const deltaSeconds = Math.max(0, Math.round((Date.now() - timestamp) / 1000));
    if (deltaSeconds < 5) {
        return "just now";
    }

    if (deltaSeconds < 60) {
        return `${deltaSeconds}s ago`;
    }

    const deltaMinutes = Math.round(deltaSeconds / 60);
    if (deltaMinutes < 60) {
        return `${deltaMinutes}m ago`;
    }

    const deltaHours = Math.round(deltaMinutes / 60);
    if (deltaHours < 48) {
        return `${deltaHours}h ago`;
    }

    const deltaDays = Math.round(deltaHours / 24);
    return `${deltaDays}d ago`;
}

function formatBucketLabel(value) {
    if (!value) {
        return "Unknown";
    }

    const date = new Date(value);
    return date.toLocaleString([], { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
}

function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}
