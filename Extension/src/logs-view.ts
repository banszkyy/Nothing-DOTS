import * as vscode from "vscode"
import { UnitLogItemExtended } from "./debugger-client"

export type WebviewMessage =
    | { type: "ready" }
    | { type: "selectPacket"; id: number }
    | { type: "setFilter"; filter: string }
    | { type: "clearPackets" }

export type ExtensionMessage =
    | { type: "init"; packets: UnitLogItemExtended[] }
    | { type: "packet"; packet: UnitLogItemExtended }
    | { type: "packetBatch"; packets: UnitLogItemExtended[] }
    | { type: "clear" }


export class PacketSource {
    private emitter = new vscode.EventEmitter<UnitLogItemExtended>();
    private nextId = 1;
    private packets: Array<UnitLogItemExtended> = [];

    readonly onPacket = this.emitter.event;

    getAll(): ReadonlyArray<UnitLogItemExtended> {
        return this.packets
    }

    publish(packet: Omit<UnitLogItemExtended, "id">) {
        const v = { ...packet, id: this.nextId++ }
        this.emitter.fire(v)
        this.packets.push(v)
        if (this.packets.length > 500) this.packets.splice(500)
    }

    clear() {
        this.packets = []
    }

    dispose() {
        this.emitter.dispose()
    }
}

export class PacketInspectorPanel {
    public static currentPanel?: PacketInspectorPanel

    private readonly panel: vscode.WebviewPanel
    private readonly extensionUri: vscode.Uri
    private readonly disposables: vscode.Disposable[] = [];
    private readonly source: PacketSource

    private constructor(panel: vscode.WebviewPanel, extensionUri: vscode.Uri, source: PacketSource) {
        this.panel = panel
        this.extensionUri = extensionUri
        this.source = source

        this.panel.webview.html = this.getHtml(extensionUri)

        this.panel.webview.onDidReceiveMessage(message => this.onMessage(message), undefined, this.disposables)

        this.source.onPacket(packet => {
            this.postMessage({ type: "packet", packet })
        }, undefined, this.disposables)

        this.panel.onDidDispose(() => this.dispose(), undefined, this.disposables)
    }

    static createOrShow(extensionUri: vscode.Uri, source: PacketSource) {
        const column = vscode.ViewColumn.One

        if (PacketInspectorPanel.currentPanel) {
            PacketInspectorPanel.currentPanel.panel.reveal(column)
            return
        }

        const panel = vscode.window.createWebviewPanel(
            "packetInspector",
            "Packet Inspector",
            column,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [
                    vscode.Uri.joinPath(extensionUri, "media")
                ]
            }
        )

        PacketInspectorPanel.currentPanel = new PacketInspectorPanel(panel, extensionUri, source)
    }

    private onMessage(message: WebviewMessage) {
        switch (message.type) {
            case "ready":
                this.postMessage({ type: "init", packets: this.source.getAll() })
                break

            case "clearPackets":
                this.source.clear()
                this.postMessage({ type: "clear" })
                break

            case "selectPacket":
                break

            case "setFilter":
                break
        }
    }

    private postMessage(message: unknown) {
        this.panel.webview.postMessage(message)
    }

    private getHtml(extensionUri: vscode.Uri): string {
        const webview = this.panel.webview

        const nonce = getNonce()

        return `<!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline' ${webview.cspSource}; script-src 'nonce-${nonce}'; img-src ${webview.cspSource} data:;">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <link href="${webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, "media", "styles.css"))}" rel="stylesheet">
            <title>Packet Inspector</title>
        </head>
        <body>
            <header class="toolbar">
                <div class="brand">Packet Inspector</div>
                <input id="filter" type="text" placeholder="Filter packets..." value="" />
                <span id="packet-count">0 packets</span>
                <button id="clear">Clear</button>
            </header>

            <main class="workspace">
                <section class="packet-list">
                    <div class="table-header">
                        <span>No.</span>
                        <span>Time</span>
                        <span>Type</span>
                        <span>Context</span>
                        <span>Content</span>
                    </div>

                    <div id="packet-rows"></div>
                </section>

                <section id="packet-details"></section>
            </main>

            <script nonce="${nonce}" type="application/json" id="icons">${JSON.stringify({
            radio: webview.asWebviewUri(vscode.Uri.joinPath(extensionUri, "media", "icons", "radio.svg")).toString(),
            outgoing: webview.asWebviewUri(vscode.Uri.joinPath(extensionUri, "media", "icons", "outgoing.svg")).toString(),
            incoming: webview.asWebviewUri(vscode.Uri.joinPath(extensionUri, "media", "icons", "incoming.svg")).toString(),
        })}</script>
            <script nonce="${nonce}" src="${webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, "media", "main.js"))}"></script>
        </body>
        </html>`
    }

    private dispose() {
        PacketInspectorPanel.currentPanel = undefined

        while (this.disposables.length) {
            this.disposables.pop()?.dispose()
        }
    }
}

function getNonce(): string {
    const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"

    let result = ""
    for (let i = 0; i < 32; i++) {
        result += chars.charAt(Math.floor(Math.random() * chars.length))
    }
    return result
}
