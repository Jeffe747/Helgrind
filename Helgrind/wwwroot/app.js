const state = {
    configuration: { routes: [], clusters: [], settings: {} },
    selected: null,
    ui: loadUiState(),
};

const elements = {
    routeList: document.getElementById("route-list"),
    clusterList: document.getElementById("cluster-list"),
    headerSelection: document.getElementById("header-selection"),
    dashboardStatus: document.getElementById("dashboard-status"),
    dashboardStatusLabel: document.getElementById("dashboard-status-label"),
    triggerUpdate: document.getElementById("trigger-update"),
    editorTitle: document.getElementById("editor-title"),
    emptyState: document.getElementById("empty-state"),
    routeEditor: document.getElementById("route-editor"),
    clusterEditor: document.getElementById("cluster-editor"),
    destinationList: document.getElementById("destination-list"),
    template: document.getElementById("destination-row-template"),
    statusMessage: document.getElementById("status-message"),
    publicHttpsEndpoint: document.getElementById("public-https-endpoint"),
    adminHttpsEndpoint: document.getElementById("admin-https-endpoint"),
    environmentName: document.getElementById("environment-name"),
    adminNetworkPolicy: document.getElementById("admin-network-policy"),
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
        destinations: [{ destinationId: "destination1", address: "https://service.internal:5001" }],
    };
    state.configuration.clusters.push(nextCluster);
    selectItem("cluster", nextCluster.clusterId);
    render();
});

document.getElementById("delete-selected").addEventListener("click", () => {
    if (!state.selected) {
        return;
    }

    if (state.selected.type === "route") {
        state.configuration.routes = state.configuration.routes.filter(route => route.routeId !== state.selected.id);
    } else {
        state.configuration.clusters = state.configuration.clusters.filter(cluster => cluster.clusterId !== state.selected.id);
        state.configuration.routes = state.configuration.routes.filter(route => route.clusterId !== state.selected.id);
    }

    state.selected = null;
    render();
});

document.getElementById("save-config").addEventListener("click", saveConfiguration);
document.getElementById("apply-config").addEventListener("click", applyConfiguration);
document.getElementById("export-config").addEventListener("click", exportConfiguration);
document.getElementById("import-config").addEventListener("click", () => document.getElementById("import-file").click());
document.getElementById("import-file").addEventListener("change", importConfiguration);
elements.triggerUpdate.addEventListener("click", triggerSelfUpdate);
document.getElementById("add-destination").addEventListener("click", () => {
    const cluster = getSelectedCluster();
    if (!cluster) {
        return;
    }

    cluster.destinations.push({
        destinationId: `destination${cluster.destinations.length + 1}`,
        address: "",
    });
    renderDestinationList(cluster);
});

document.getElementById("certificate-form").addEventListener("submit", uploadCertificate);

bindRouteEditor();
bindClusterEditor();
bindFilters();

loadConfiguration();

async function loadConfiguration() {
    const response = await fetch("/api/admin/configuration");
    state.configuration = await response.json();
    state.selected = null;
    render();
}

async function saveConfiguration() {
    const response = await fetch("/api/admin/configuration", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(state.configuration),
    });

    state.configuration = await response.json();
    setStatus("Draft saved to SQLite.");
    render();
}

async function applyConfiguration() {
    await saveConfiguration();

    const response = await fetch("/api/admin/apply", { method: "POST" });
    const result = await response.json();
    const message = result.validationErrors?.length
        ? `${result.statusMessage} ${result.validationErrors.join(" ")}`
        : result.statusMessage;
    setStatus(message);
    await loadConfiguration();
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

    const response = await fetch("/api/admin/certificate", {
        method: "POST",
        body: formData,
    });
    const result = await response.json();
    setStatus(result.statusMessage);
    event.target.reset();
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
            route.order = Number.parseInt(document.getElementById("route-order").value || "0", 10);
            state.selected.id = route.routeId;
            renderListsOnly();
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
            renderListsOnly();
        });
    });
}

function render() {
    renderHeaderState();
    renderFilterState();
    renderListsOnly();
    renderEditor();
    renderSettings();
}

function renderHeaderState() {
    const route = getSelectedRoute();
    const cluster = getSelectedCluster();
    const settings = state.configuration.settings || {};

    if (route) {
        elements.headerSelection.textContent = `Route ${route.routeId || "untitled"} -> ${route.clusterId || "no cluster"} | ${route.hosts.join(", ") || "no hosts"}`;
    } else if (cluster) {
        elements.headerSelection.textContent = `Cluster ${cluster.clusterId || "untitled"} | ${cluster.destinations.length} destination(s) | health ${cluster.healthCheck.enabled ? "enabled" : "disabled"}`;
    } else {
        elements.headerSelection.textContent = `${settings.publicHttpsEndpointDisplay || "Public proxy"} | ${settings.adminHttpsEndpointDisplay || "Admin dashboard"}`;
    }

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

    renderList(elements.routeList, visibleRoutes, "route", route => ({
        title: route.routeId || "Untitled route",
        subtitle: `${route.hosts.join(", ") || "No hosts"}`,
        meta: `Cluster ${route.clusterId || "none"} | Path ${route.path || "{**catch-all}"}`,
        count: route.hosts.length,
    }));
    renderList(elements.clusterList, visibleClusters, "cluster", cluster => ({
        title: cluster.clusterId || "Untitled cluster",
        subtitle: `${cluster.destinations.length} destination(s)`,
        meta: cluster.healthCheck.enabled ? `Health ${cluster.healthCheck.path || "/"}` : "Health disabled",
        count: cluster.destinations.length,
    }));

    elements.routeVisibleCount.textContent = `${visibleRoutes.length} / ${state.configuration.routes.length} shown`;
    elements.clusterVisibleCount.textContent = `${visibleClusters.length} / ${state.configuration.clusters.length} shown`;
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
        button.className = `item-card ${state.selected?.type === type && state.selected?.id === id ? "selected" : ""}`;
        button.innerHTML = `
            <div class="item-main">
                <strong>${escapeHtml(entry.title)}</strong>
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
        elements.editorTitle.textContent = `Route: ${route.routeId || "Untitled"}`;
        document.getElementById("route-id").value = route.routeId;
        document.getElementById("route-cluster-id").value = route.clusterId;
        document.getElementById("route-path").value = route.path;
        document.getElementById("route-hosts").value = route.hosts.join("\n");
        document.getElementById("route-order").value = route.order;
        return;
    }

    if (cluster) {
        elements.editorTitle.textContent = `Cluster: ${cluster.clusterId || "Untitled"}`;
        document.getElementById("cluster-id").value = cluster.clusterId;
        document.getElementById("cluster-load-balancing").value = cluster.loadBalancingPolicy || "";
        document.getElementById("health-enabled").checked = !!cluster.healthCheck.enabled;
        document.getElementById("health-interval").value = cluster.healthCheck.interval || "00:00:10";
        document.getElementById("health-timeout").value = cluster.healthCheck.timeout || "00:00:03";
        document.getElementById("health-policy").value = cluster.healthCheck.policy || "ConsecutiveFailures";
        document.getElementById("health-path").value = cluster.healthCheck.path || "";
        document.getElementById("health-query").value = cluster.healthCheck.query || "";
        document.getElementById("health-threshold").value = cluster.consecutiveFailuresThreshold ?? "";
        renderDestinationList(cluster);
        return;
    }

    elements.editorTitle.textContent = "Select a route or cluster";
    elements.destinationList.innerHTML = "";
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
            renderListsOnly();
        });

        addressInput.addEventListener("input", () => {
            destination.address = addressInput.value.trim();
        });

        row.querySelector(".remove-destination").addEventListener("click", () => {
            cluster.destinations.splice(index, 1);
            renderDestinationList(cluster);
            renderListsOnly();
        });

        elements.destinationList.appendChild(row);
    });
}

function renderSettings() {
    const settings = state.configuration.settings || {};
    elements.publicHttpsEndpoint.textContent = settings.publicHttpsEndpointDisplay || `https://localhost:${settings.publicHttpsPort ?? 443}`;
    elements.adminHttpsEndpoint.textContent = settings.adminHttpsEndpointDisplay || `https://localhost:${settings.adminHttpsPort ?? 8444}`;
    elements.environmentName.textContent = settings.environmentName || "Unknown";
    elements.adminNetworkPolicy.textContent = settings.adminAccessPolicySummary || "Configured LAN ranges";
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
    elements.statusMessage.textContent = message;
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
        };
    } catch {
        return {
            routeFilters: { expanded: false, search: "", selectedClusterOnly: false, hostsOnly: false },
            clusterFilters: { expanded: false, search: "", referencedOnly: false, healthEnabledOnly: false },
        };
    }
}

function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}