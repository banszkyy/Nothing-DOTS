'use strict'

import * as vscode from 'vscode'
import { ProviderResult } from 'vscode'
import * as fs from 'fs'
import * as os from 'os'
import { extensionId } from './utils'
import { debuggerHub, GameUnit, log, token } from './extension'
import path from 'path'
import { PacketInspectorPanel, PacketSource } from './logs-view'

/*
type UnitModule = {
    type: string
    address: number
    fields: Record<string, UnitModuleType>
}

type UnitModuleType = {
    type: string
    offset: number
}

type UnitModuleTreeViewItem = UnitModule | UnitModuleType

let unitModulesTreeView: vscode.TreeView<UnitModuleTreeViewItem> | null = null
*/

export function startDebugging(token: string, ghost: string, name: string) {
    log.debug(`[Debugger] Start debuging ghost ${ghost} ...`)
    vscode.debug.startDebugging(undefined, {
        type: extensionId,
        name: name,
        request: 'attach',
        ghost: ghost,
        token: token,
        sessionId: ghost,
    })
        .then(result => {
            if (!result) {
                vscode.window.showErrorMessage('Failed to start debugging')
                log.warn('[Debugger] Failed to start debugging')
            } else {
                log.info('[Debugger] Debugging started')

                /*
                const unitModulesTreeDataProvider = new class implements vscode.TreeDataProvider<UnitModuleTreeViewItem> {
                    constructor() { }

                    private _onDidChangeTreeData = new vscode.EventEmitter<UnitModuleTreeViewItem | undefined>();
                    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

                    refresh(): void {
                        this._onDidChangeTreeData.fire(undefined)
                    }

                    getTreeItem(element: UnitModuleTreeViewItem): vscode.TreeItem {
                        if ('address' in element) {
                            return new vscode.TreeItem(`${element.type} ${element.address}`, vscode.TreeItemCollapsibleState.None)
                        } else {
                            return new vscode.TreeItem(`${element.type} ${element.offset}`, vscode.TreeItemCollapsibleState.None)
                        }
                    }

                    getChildren(element?: GameUnit): Thenable<UnitModuleTreeViewItem[]> {
                        if (!element) {
                            if (!languageClient.client) return Promise.resolve([])
                            if (!token) return Promise.resolve([])
                            return languageClient.client.client.sendRequest('modules', { token: token, entity:  })
                                .then(() => {
                                    return []
                                })
                        }
                        return Promise.resolve([])
                    }
                }()
                if (unitModulesTreeView) {
                    unitModulesTreeView.dispose()
                }

                unitModulesTreeView = vscode.window.createTreeView('unitModules', { treeDataProvider: unitModulesTreeDataProvider })
                */
            }
        }, error => {
            log.error(`[Debugger] Failed to start debugging`, error)
        })
}

export type UnitLogItem = {
    logType: string,
    index: number,
    entity: string,
    timestamp: number,
    content?: any,
}

export type UnitLogItemExtended = UnitLogItem & {
    id: number
}

export function activate(context: vscode.ExtensionContext) {
    log.debug('[Debugger] Activating debugger ...')

    const adapterFactory = new class implements vscode.DebugAdapterDescriptorFactory {
        async createDebugAdapterDescriptor(session: vscode.DebugSession, executable: vscode.DebugAdapterExecutable | undefined): Promise<vscode.DebugAdapterDescriptor> {
            log.trace(`[Debugger] Registering adapter on session`, session)
            if (!debuggerHub) throw new Error('Debugger hub not created')
            return debuggerHub.createInlineImplementation(session)
        }
    }()

    const trackerFactory = new class implements vscode.DebugAdapterTrackerFactory {
        createDebugAdapterTracker(session: vscode.DebugSession): ProviderResult<vscode.DebugAdapterTracker> {
            log.trace(`[Debugger] Registering adapter tracker on session`, session)
            return {
                onWillStartSession() {
                    log.info(`[Debugger] Will start session`)
                },
                onWillStopSession() {
                    log.info(`[Debugger] Will stop session`)
                },
                onError(error: Error): void {
                    log.error(`[Debugger] Error`, error)
                },
                onExit(code, signal) {
                    log.debug(`[Debugger] Exit code: ${code} signal: ${signal}`)
                },
            }
        }
    }()

    log.debug('[Debugger] Registering adapter descriptor factory')
    context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory(extensionId, adapterFactory))
    log.debug('[Debugger] Registering adapter tracker factory')
    context.subscriptions.push(vscode.debug.registerDebugAdapterTrackerFactory(extensionId, trackerFactory))

    if ('dispose' in adapterFactory &&
        typeof adapterFactory.dispose === 'function' &&
        adapterFactory.dispose instanceof Function) {
        context.subscriptions.push(adapterFactory as { dispose(): void })
    }

    if ('dispose' in trackerFactory &&
        typeof trackerFactory.dispose === 'function' &&
        trackerFactory.dispose instanceof Function) {
        context.subscriptions.push(trackerFactory as { dispose(): void })
    }

    context.subscriptions.push(vscode.commands.registerCommand(`${extensionId}.debug.attach`, (ghost: string | GameUnit, name: string) => {
        log.debug('[Debugger] Try to start debugging ...')

        if (!ghost) {
            log.warn('[Debugger] No ghost provided')
            vscode.window.showErrorMessage(`No ghost provided`)
            return
        }

        if (!token) {
            log.warn('[Debugger] Not authenticated')
            vscode.window.showErrorMessage(`Not authenticated`, 'Authenticate')
                .then(v => {
                    if (v === 'Authenticate') {
                        vscode.commands.executeCommand(`${extensionId}.authenticate`)
                    }
                })
            return
        }

        if (typeof ghost === 'object') {
            name = ghost.name
            ghost = ghost.id
        }

        startDebugging(token, ghost, name)
    }))

    context.subscriptions.push(vscode.commands.registerCommand(`${extensionId}.debug.disconnect`, (ghost: string | GameUnit) => {
        if (!ghost) {
            log.warn('[Debugger] No ghost provided')
            vscode.window.showErrorMessage(`No ghost provided`)
            return
        }

        if (typeof ghost === 'object') {
            ghost = ghost.id
        }

        const session = debuggerHub?.sessions.get(ghost)
        if (!session) {
            log.warn(`[Debugger] Session ${ghost} not found`)
            vscode.window.showErrorMessage(`Session ${ghost} not found`)
            return
        }

        session.terminateLocally('Initiated by the user')
    }))

    context.subscriptions.push(vscode.commands.registerCommand(`${extensionId}.unitLog.radio.view`, async (item: UnitLogItem) => {
        const data = item.content?.['data']
        if (!data) return

        const filePath = path.join(fs.mkdtempSync(path.join(os.tmpdir(), extensionId)), 'radio.bin')
        fs.writeFileSync(filePath, Buffer.from(data, 'base64'))
        log.debug(`Radar packet content written to "${filePath}"`)

        const uri = vscode.Uri.file(filePath)

        await vscode.commands.executeCommand('vscode.open', uri, { preview: true } as vscode.TextDocumentShowOptions, 'Radio Packet Content')
    }))

    vscode.debug.onDidStartDebugSession(e => {
        log.trace('[Debugger] Debug session started:', e)
    })
    vscode.debug.onDidChangeActiveDebugSession(e => {
        log.trace('[Debugger] Active debug session changed:', e)
    })

    vscode.debug.onDidTerminateDebugSession(e => {
        log.trace('[Debugger] Debug session terminated:', e)
    })

    const outputChannel = vscode.window.createOutputChannel("Nothingame Debug Host", { log: true })

    const packetSource = new PacketSource()
    context.subscriptions.push(packetSource)

    context.subscriptions.push(vscode.commands.registerCommand(`${extensionId}.unitLog.view`, () => {
        PacketInspectorPanel.createOrShow(context.extensionUri, packetSource)
    }))

    vscode.debug.onDidReceiveDebugSessionCustomEvent(e => {
        if (e.event === "adapterLog") {
            switch (e.body.level) {
                case 'trace': outputChannel.trace(e.body.message); break
                case 'debug': outputChannel.debug(e.body.message); break
                case 'info': outputChannel.info(e.body.message); break
                case 'warn': outputChannel.warn(e.body.message); break
                case 'error': outputChannel.error(e.body.message); break
                default: outputChannel.appendLine(e.body.message); break
            }
        } else if (e.event === "unitLog") {
            for (const v of e.body.logs as ReadonlyArray<UnitLogItem> ?? []) {
                packetSource.publish(v)
            }
        } else {
            log.trace('[Debugger] Custom event received:', e)
        }
    })

    log.info('[Debugger] Activated')
}

export function deactivate() {

}
