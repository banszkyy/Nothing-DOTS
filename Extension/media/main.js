const vscode = acquireVsCodeApi()

const state = {
    /** @type {ReadonlyArray<import('../src/debugger-client').UnitLogItemExtended>} */
    packets: [],
    /** @type {string | null} */
    selectedPacketId: null,
    /** @type {string} */
    filter: ""
}

const icons = JSON.parse(document.getElementById('icons').innerText)

function render() {
    const filtered = state.packets
        .filter(packet => {
            const query = state.filter.toLowerCase()
            if (!query) return true

            return [
                packet.logType,
                packet.entity,
                packet.content?.['contextualEntity'] ?? packet.content?.['meta']?.['contextualEntity']
            ].some(value => String(value).toLowerCase().includes(query))
        })

    const maxRenderCount = 500

    document.getElementById('filter').value = state.filter
    document.getElementById('packet-count').textContent = `${filtered.length} packets${state.packets.length > filtered.length ? ` (${state.packets.length - filtered.length} hidden)` : ''}${filtered.length > maxRenderCount ? ` (capped to ${maxRenderCount})` : ''}`
    document.getElementById('packet-rows').innerHTML = filtered.slice(Math.max(0, filtered.length - maxRenderCount)).map(packet => `<div class="packet-row ${packet.id === state.selectedPacketId ? "selected" : ""}" data-id="${packet.id}">
            <span>${packet.id}</span>
            <span>${formatTime(packet.timestamp)}</span>
            <span>${({
            'Transmission SendW': `<div class="packet-type-icon" style="mask:url('${icons['radio']}');-webkit-mask:url('${icons['radio']}')"></div>`,
            'Transmission ReceiveW': `<div class="packet-type-icon" style="mask:url('${icons['radio']}');-webkit-mask:url('${icons['radio']}')"></div>`,
        })[packet.logType] ?? escapeHtml(packet.logType)}</span>
            <span>${(() => {
            const localEntity = packet.entity
            const remoteEntity = packet.content?.['contextualEntity'] ?? packet.content?.['meta']?.['contextualEntity']
            return `${escapeHtml(localEntity)} ${({
                'Transmission SendW': `<div class="packet-direction-icon" style="mask:url('${icons['outgoing']}');-webkit-mask:url('${icons['outgoing']}')"></div>`,
                'Transmission ReceiveW': `<div class="packet-direction-icon" style="mask:url('${icons['incoming']}');-webkit-mask:url('${icons['incoming']}')"></div>`,
            })[packet.logType] ?? '...'} ${remoteEntity ? escapeHtml(remoteEntity) : ''}`.trim()
        })()}</span>
            <span>${packet.content?.['data'] ? escapeHtml(atob(packet.content['data']).split('').map(c => c.charCodeAt(0).toString(16).toUpperCase().padStart(2, '0')).join(' ')) : ''}</span>
        </div>
    `).join("")
    document.getElementById('packet-details').innerHTML = renderDetails()

    document.getElementById("filter").addEventListener("input", e => {
        state.filter = e.target.value
        render()
    })

    document.getElementById("clear").addEventListener("click", () => {
        vscode.postMessage({ type: "clearPackets" })
    })

    document.querySelectorAll(".packet-row").forEach(row => {
        row.addEventListener("click", () => {
            state.selectedPacketId = Number(row.dataset.id)

            vscode.postMessage({
                type: "selectPacket",
                id: state.selectedPacketId
            })

            render()
        })
    })
}

function renderDetails() {
    /** @type {import('../src/debugger-client').UnitLogItemExtended} */
    const packet = state.packets.find(p => p.id === state.selectedPacketId)

    if (!packet) {
        return `<div class="empty-state">Select a packet to inspect</div>`
    }

    return `
        <div class="details-header">
            Packet #${packet.id}
        </div>

        <div class="details-content">
            <h3>General</h3>
            <div class="detail-row">
                <span>Index</span>
                <span>${escapeHtml(packet.index)}</span>
            </div>
            <div class="detail-row">
                <span>Type</span>
                <span>${escapeHtml(packet.logType)}</span>
            </div>
            <div class="detail-row">
                <span>Timestamp</span>
                <span>${formatTime(packet.timestamp)}</span>
            </div>

            <h3>Payload</h3>
            <pre class="payload">${escapeHtml(packet.content?.['data'] ? JSON.stringify(packet.content['data']) : 'No payload available')}</pre>
        </div>
    `
}

function formatTime(timestamp) {
    return new Date(timestamp).toLocaleTimeString()
}

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, char => ({
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "'": "&#39;"
    })[char])
}

window.addEventListener("message", event => {
    const message = event.data

    switch (message.type) {
        case "init":
            state.packets = message.packets
            break

        case "packet":
            state.packets.push(message.packet)
            break

        case "packetBatch":
            state.packets.push(...message.packets)
            break

        case "clear":
            state.packets = []
            state.selectedPacketId = null
            break
    }

    render()
})

vscode.postMessage({ type: "ready" })
