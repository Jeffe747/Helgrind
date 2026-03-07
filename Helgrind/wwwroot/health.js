const healthElements = {
    environment: document.getElementById("health-environment"),
    updated: document.getElementById("health-updated"),
    message: document.getElementById("health-message"),
    listenerStatusList: document.getElementById("listener-status-list"),
    certificateStatusList: document.getElementById("certificate-status-list"),
    proxySummary: document.getElementById("proxy-summary"),
    routeList: document.getElementById("health-route-list"),
    refreshButton: document.getElementById("refresh-health"),
};

healthElements.refreshButton.addEventListener("click", loadHealth);

loadHealth();

async function loadHealth() {
    healthElements.message.textContent = "Refreshing admin health…";

    const response = await fetch("/api/admin/status");
    const status = await response.json();

    healthElements.environment.textContent = status.environmentName || "Unknown";
    healthElements.updated.textContent = status.generatedUtc
        ? new Date(status.generatedUtc).toLocaleString()
        : "Unknown";
    healthElements.message.textContent = "Admin health is current.";

    renderListeners(status);
    renderCertificate(status.certificate);
    renderProxy(status.proxy);
}

function renderListeners(status) {
    const listeners = [status.publicListener, status.adminListener];
    healthElements.listenerStatusList.innerHTML = listeners.map(listener => `
        <article class="health-item">
            <div>
                <p class="eyebrow">${escapeHtml(listener.name)}</p>
                <h3>${escapeHtml(listener.endpoint)}</h3>
            </div>
            <p><strong>Status:</strong> ${escapeHtml(listener.status)}</p>
            <p>${escapeHtml(listener.exposure)}</p>
        </article>
    `).join("");
}

function renderCertificate(certificate) {
    const name = certificate.activeCertificate?.displayName || "Fallback temporary certificate";
    const thumbprint = certificate.activeCertificate?.thumbprint || "N/A";
    healthElements.certificateStatusList.innerHTML = `
        <article class="health-item">
            <div>
                <p class="eyebrow">Served Certificate</p>
                <h3>${escapeHtml(name)}</h3>
            </div>
            <p><strong>Status:</strong> ${escapeHtml(certificate.status)}</p>
            <p><strong>Thumbprint:</strong> ${escapeHtml(thumbprint)}</p>
            <p><strong>Restart Required:</strong> ${certificate.restartRequired ? "Yes" : "No"}</p>
            <p>${escapeHtml(certificate.restartHint)}</p>
        </article>
    `;
}

function renderProxy(proxy) {
    healthElements.proxySummary.innerHTML = `
        <div class="health-stat"><span class="label">Routes</span><strong>${proxy.routeCount}</strong></div>
        <div class="health-stat"><span class="label">Clusters</span><strong>${proxy.clusterCount}</strong></div>
        <div class="health-stat"><span class="label">Destinations</span><strong>${proxy.destinationCount}</strong></div>
        <div class="health-stat"><span class="label">Last Applied</span><strong>${proxy.lastAppliedUtc ? new Date(proxy.lastAppliedUtc).toLocaleString() : "Never"}</strong></div>
    `;

    if (!proxy.loadedRoutes.length) {
        healthElements.routeList.innerHTML = '<div class="empty-state">No routes are currently loaded.</div>';
        return;
    }

    healthElements.routeList.innerHTML = proxy.loadedRoutes.map(route => `
        <article class="health-item route-item">
            <div>
                <p class="eyebrow">${escapeHtml(route.routeId)}</p>
                <h3>${escapeHtml(route.hosts.join(", ") || "No hosts")}</h3>
            </div>
            <p><strong>Cluster:</strong> ${escapeHtml(route.clusterId)}</p>
            <p><strong>Path:</strong> ${escapeHtml(route.path)}</p>
        </article>
    `).join("");
}

function escapeHtml(value) {
    return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}